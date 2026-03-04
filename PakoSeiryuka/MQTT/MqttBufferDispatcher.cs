using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PakoSeiryuka.MQTT
{
    public class MqttBufferDispatcher : BackgroundService
    {
        private readonly MQTTClient _mqtt;
        private readonly MqttBufferSqliteRepository _repo;
        private readonly ILogger<MqttBufferDispatcher> _logger;

        public MqttBufferDispatcher(MQTTClient mqtt, MqttBufferSqliteRepository repo, ILogger<MqttBufferDispatcher> logger)
        {
            _mqtt = mqtt;
            _repo = repo;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[SQLiteBuffer] Dispatcher started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!_mqtt.IsConnected)
                    {
                        await Task.Delay(1000, stoppingToken);
                        continue;
                    }

                    var items = await _repo.GetPendingAsync(50);

                    if (items.Count == 0)
                    {
                        await Task.Delay(500, stoppingToken);
                        continue;
                    }

                    foreach (var row in items)
                    {
                        if (stoppingToken.IsCancellationRequested)
                            break;

                        var ok = await _mqtt.PublishAsync(row.Topic, row.PayloadJson, stoppingToken);

                        if (ok)
                        {
                            await _repo.MarkSentAsync(row.Id);
                            _logger.LogInformation("[SQLiteBuffer] SENT id={Id} topic={Topic}", row.Id, row.Topic);
                        }
                        else
                        {
                            await _repo.MarkFailedAsync(row.Id, "Publish failed/disconnected");
                            _logger.LogWarning("[SQLiteBuffer] FAILED id={Id} topic={Topic}", row.Id, row.Topic);
                            break; // stop dulu biar tidak spam
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[SQLiteBuffer] Dispatcher exception");
                }

                await Task.Delay(500, stoppingToken);
            }
        }
    }
}
