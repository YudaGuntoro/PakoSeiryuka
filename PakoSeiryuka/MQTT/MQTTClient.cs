using HslCommunication.MQTT;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PakoSeiryuka.MQTT
{
    /// <summary>
    /// MQTT Client sebagai BackgroundService.
    /// - Auto connect & auto reconnect (jika putus)
    /// - Ada backoff delay supaya tidak spam connect
    /// - Thread-safe: pakai SemaphoreSlim agar connect tidak dobel
    /// - Cross-platform: config dibaca dari IConfiguration (YAML/JSON/ENV)
    /// </summary>
    public class MQTTClient : BackgroundService
    {
        private MqttClient? _mqttClient;             // instance client MQTT (HslCommunication)
        private volatile bool _isConnected;          // status koneksi (volatile biar aman antar thread)

        private readonly string _broker;             // host/ip broker MQTT
        private readonly int _port;                  // port broker MQTT

        private readonly SemaphoreSlim _connectLock = new(1, 1); // lock supaya tidak connect barengan
        private int _failCount = 0;                  // untuk hitung kegagalan connect (backoff delay)

        private readonly ILogger<MQTTClient> _logger;

        /// <summary>
        /// Status MQTT connect (dipakai oleh service lain)
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// Constructor:
        /// - Ambil konfigurasi dari IConfiguration (contoh: appsettings.yaml)
        /// - Tidak pakai Config.Instance (kernel32) supaya aman di Linux
        /// </summary>
        public MQTTClient(IConfiguration config, ILogger<MQTTClient> logger)
        {
            _logger = logger;

            // ambil konfigurasi dari YAML/JSON/ENV
            _broker = config["MQTT:Broker"] ?? "127.0.0.1";
            _port = int.Parse(config["MQTT:Port"] ?? "1883");

            _logger.LogInformation("[MQTT] Config Broker={Broker} Port={Port}", _broker, _port);
        }

        /// <summary>
        /// Background loop utama.
        /// Akan jalan terus selama WorkerService hidup.
        /// Jika MQTT belum connect -> coba connect.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[MQTT] Service running");

            while (!stoppingToken.IsCancellationRequested)
            {
                // kalau disconnected -> coba connect
                if (!_isConnected)
                    await EnsureConnectedAsync(stoppingToken);

                // delay supaya loop tidak "ngebut"
                await Task.Delay(1000, stoppingToken);
            }

            _logger.LogInformation("[MQTT] Service stopping");
        }

        /// <summary>
        /// Stop service:
        /// - close connection secara aman
        /// </summary>
        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[MQTT] Stop requested");

            SafeCloseClient();
            _isConnected = false;

            return base.StopAsync(cancellationToken);
        }

        /// <summary>
        /// Connect logic paling stabil:
        /// - anti double connect (pakai _connectLock)
        /// - reset client kalau gagal
        /// - set handler network error -> otomatis mark disconnected
        /// - backoff delay jika gagal
        /// </summary>
        private async Task EnsureConnectedAsync(CancellationToken ct)
        {
            // kalau ada thread lain sedang connect, jangan connect juga
            if (!await _connectLock.WaitAsync(0, ct))
                return;

            try
            {
                if (_isConnected) return;

                // kalau ada client lama, tutup dulu
                SafeCloseClient();
                _isConnected = false;

                // buat client baru setiap reconnect (lebih aman)
                var clientId = $"PakoSeiryuka-{Guid.NewGuid()}";

                _mqttClient = new MqttClient(new MqttConnectionOptions
                {
                    ClientId = clientId,
                    IpAddress = _broker,
                    Port = _port,
                    KeepAlivePeriod = TimeSpan.FromSeconds(5) // keep alive supaya broker tahu client masih hidup
                });

                // event network error -> mark disconnected
                _mqttClient.OnNetworkError += (_, __) =>
                {
                    _logger.LogWarning("[MQTT] Network error -> mark disconnected");
                    _isConnected = false;
                };

                _logger.LogInformation("[MQTT] Connecting... {Broker}:{Port} ClientId={ClientId}",
                    _broker, _port, clientId);

                // coba connect
                var res = await _mqttClient.ConnectServerAsync();

                if (res.IsSuccess)
                {
                    _isConnected = true;
                    _failCount = 0;
                    _logger.LogInformation("[MQTT] Connected OK ✅");
                }
                else
                {
                    _isConnected = false;
                    _failCount++;

                    _logger.LogWarning("[MQTT] Connect failed: {Msg}", res.Message);

                    // delay pakai backoff supaya tidak spam
                    await Task.Delay(GetBackoffDelay(_failCount), ct);
                }
            }
            catch (Exception ex)
            {
                _isConnected = false;
                _failCount++;

                _logger.LogError(ex, "[MQTT] Connect exception");

                // delay pakai backoff supaya tidak spam
                await Task.Delay(GetBackoffDelay(_failCount), ct);
            }
            finally
            {
                _connectLock.Release();
            }
        }

        /// <summary>
        /// Backoff delay:
        /// semakin sering gagal, semakin lama delay reconnect.
        /// </summary>
        private static TimeSpan GetBackoffDelay(int failCount) => failCount switch
        {
            <= 1 => TimeSpan.FromSeconds(3),
            2 => TimeSpan.FromSeconds(5),
            3 => TimeSpan.FromSeconds(10),
            4 => TimeSpan.FromSeconds(20),
            _ => TimeSpan.FromSeconds(30),
        };

        /// <summary>
        /// Close client dengan aman (tidak bikin crash walau error).
        /// </summary>
        private void SafeCloseClient()
        {
            try { _mqttClient?.ConnectClose(); } catch { }
            _mqttClient = null;
        }

        /// <summary>
        /// Publish data ke broker.
        /// Return true kalau sukses, false kalau gagal.
        /// Jika publish gagal -> mark disconnected biar reconnect loop jalan.
        /// </summary>
        public async Task<bool> PublishAsync(string topic, string payload, CancellationToken ct = default)
        {
            try
            {
                // jika tidak connect, langsung gagal (biar caller bisa buffer)
                if (!_isConnected || _mqttClient == null)
                    return false;

                // pesan MQTT
                var msg = new MqttApplicationMessage
                {
                    Topic = topic.Trim(),
                    Payload = Encoding.UTF8.GetBytes(payload),
                    QualityOfServiceLevel = MqttQualityOfServiceLevel.AtLeastOnce, // QoS 1
                    Retain = false
                };

                // publish ke broker
                var res = await _mqttClient.PublishMessageAsync(msg);

                if (!res.IsSuccess)
                {
                    _logger.LogWarning("[MQTT] Publish failed: {Msg}", res.Message);

                    // mark disconnected supaya reconnect
                    _isConnected = false;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MQTT] Publish exception -> mark disconnected");

                // mark disconnected supaya reconnect
                _isConnected = false;
                return false;
            }
        }
    }
}
