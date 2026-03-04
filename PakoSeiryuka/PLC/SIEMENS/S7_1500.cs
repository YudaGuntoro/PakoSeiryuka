using HslCommunication.MQTT;
using HslCommunication.Profinet.Siemens;
using Newtonsoft.Json;
using PakoSeiryuka.MQTT;
using PakoSeiryuka.PLC.SIEMENS.Service;
using System.Threading;
using System.Timers;

namespace PakoSeiryuka.PLC.SIEMENS
{
    public class S7_1500 : BackgroundService
    {
        private readonly ILogger<S7_1500> _logger;
        private readonly IConfiguration _config;
        private readonly MQTTClient _mqtt;
        private readonly MqttBufferSqliteRepository _buffer;

        private int _publishing = 0;

        private readonly string Ip;
        private readonly int Port;

        private SiemensS7Net? plc;

        private readonly System.Timers.Timer _timerConnectPLC;
        private readonly System.Timers.Timer _timerPublishMQTT;

        private Thread? thread;
        private bool firstConnect;

        private bool isThreadRun = false;
        private bool isThreadRunning = false;

        private readonly int timerLoop = 1000; // 1 detik health-check

        private bool isConnectedPLC = false;
        private string PLCMessageStatus = "";

        private ReadDataService? readDataService;

        public string DeviceName { get; private set; } = "SiemensPLC";

        public S7_1500(
            ILogger<S7_1500> logger,
            IConfiguration config,
            MQTTClient mqtt,
            MqttBufferSqliteRepository buffer)
        {
            _logger = logger;
            _config = config;
            _mqtt = mqtt;
            _buffer = buffer;

            DeviceName = "SiemensPLC";

            Ip = _config[$"{DeviceName}:IP"]
                 ?? throw new ArgumentNullException("IP", "IP address cannot be null");

            if (!int.TryParse(_config[$"{DeviceName}:Port"]?.Trim(), out Port))
                throw new ArgumentException($"Invalid Port configuration for {DeviceName}");

            // Timer connect PLC (retry setiap 5 detik)
            _timerConnectPLC = new System.Timers.Timer { Interval = 5000, AutoReset = true };
            _timerConnectPLC.Elapsed += _timerConnectPLC_Elapsed;
            _timerConnectPLC.Start();

            // Timer publish MQTT (tiap 1 detik)
            _timerPublishMQTT = new System.Timers.Timer { Interval = 1000, AutoReset = true };
            _timerPublishMQTT.Elapsed += _timerPublishMQTT_Elapsed;
            _timerPublishMQTT.Start();
        }

        /// <summary>
        /// Publish MQTT berkala:
        /// 1) publish snapshot
        /// 2) kalau snapshot sukses -> flush buffer sqlite (FIFO) lalu delete yg sudah terkirim
        /// 3) publish event queues (cycle, change model, alarm time total, stoploss, dte)
        /// </summary>
        private async void _timerPublishMQTT_Elapsed(object? sender, ElapsedEventArgs e)
        {
            // ✅ anti overlap publish
            if (Interlocked.Exchange(ref _publishing, 1) == 1)
                return;

            try
            {
                if (readDataService == null)
                    return;

                // ================= PUBLISH SNAPSHOT PLC =================
                var snapshotData = new
                {
                    PLC_Ip = Ip,
                    PLC_Port = Port,
                    IsConnected = isConnectedPLC,
                    MessageStatus = PLCMessageStatus,
                    Data = readDataService.SnapshotAll()
                };

                var snapshotPayload = JsonConvert.SerializeObject(snapshotData);
                var snapshotTopic = _config["MQTT:SiemensTopic"] ?? "/siemens";

                var snapshotOk = await _mqtt.PublishAsync(snapshotTopic, snapshotPayload);

                if (!snapshotOk)
                {
                    await _buffer.InsertAsync(snapshotTopic, snapshotPayload);
                    _logger.LogWarning("MQTT DOWN -> snapshot buffered to SQLite");
                    return; // mqtt down, stop tick biar gak kerja 2x
                }

                // ✅ MQTT UP -> flush buffer dulu (bertahap biar gak nge-block lama)
                await FlushMqttBufferIfAnyAsync(batchSize: 200, maxBatchesPerTick: 5);

                // ================= PUBLISH MACHINE CYCLE EVENT =================
                var cycleTopic = _config["MQTT:PerCycleTopic"] ?? "DATA/Machine/Cycle";
                while (readDataService.TryDequeueCycle(out var cycleEvt))
                {
                    var payload = JsonConvert.SerializeObject(cycleEvt);
                    var ok = await _mqtt.PublishAsync(cycleTopic, payload);

                    if (!ok)
                    {
                        await _buffer.InsertAsync(cycleTopic, payload);
                        _logger.LogWarning("Cycle MQTT buffered (machine {MachineNo})", cycleEvt.MachineNo);
                        return; // mqtt down -> stop
                    }
                }

                // ================= PUBLISH CHANGE MODEL EVENT =================
                var changeModelTopic = _config["MQTT:ChangeModelTopic"] ?? "DATA/Machine/ChangeModel";
                while (readDataService.TryDequeueChangeModel(out var evt))
                {
                    var payload = JsonConvert.SerializeObject(evt);
                    var ok = await _mqtt.PublishAsync(changeModelTopic, payload);

                    if (!ok)
                    {
                        await _buffer.InsertAsync(changeModelTopic, payload);
                        _logger.LogWarning("ChangeModel MQTT buffered (machine {MachineNo})", evt.MachineNo);
                        return;
                    }
                }

                // ================= PUBLISH LOADING PARTS EVENT =================
                var loadingTopic = _config["MQTT:LoadingTopic"] ?? "DATA/MachineData/LoadingParts";

                while (readDataService.TryDequeueLoading(out var loadingEvt))
                {
                    // payload sesuai permintaan:
                    // { "LoadMachineNo": XX }
                    var payload = JsonConvert.SerializeObject(new
                    {
                        LoadMachineNo = loadingEvt.LoadMachineNo
                    });

                    var ok = await _mqtt.PublishAsync(loadingTopic, payload);

                    if (!ok)
                    {
                        await _buffer.InsertAsync(loadingTopic, payload);
                        _logger.LogWarning("LoadingParts MQTT buffered (LoadMachineNo {LoadMachineNo})", loadingEvt.LoadMachineNo);
                        return; // mqtt down -> stop tick
                    }
                }


                // ================= PUBLISH ALARM TIME TOTAL EVENT =================
                var alarmTimeTopic = _config["MQTT:AlarmTimeTotalTopic"] ?? "DATA/MachineData/AlarmTimeTotal";
                while (readDataService.TryDequeueAlarmTimeTotal(out var alarmEvt))
                {
                    var payload = JsonConvert.SerializeObject(alarmEvt);
                    var ok = await _mqtt.PublishAsync(alarmTimeTopic, payload);

                    if (!ok)
                    {
                        await _buffer.InsertAsync(alarmTimeTopic, payload);
                        _logger.LogWarning("AlarmTimeTotal MQTT buffered (Machine {MachineNo}, Alarm {AlarmIndex})",
                            alarmEvt.MachineNo, alarmEvt.AlarmIndex);
                        return;
                    }
                }

                // ================= PUBLISH STOPLOSS EVENT =================
                var stopLossTopic = _config["MQTT:StopLossTopic"] ?? "DATA/MachineData/StopLoss";
                while (readDataService.TryDequeueStopLoss(out var stopLossEvt))
                {
                    var payload = JsonConvert.SerializeObject(stopLossEvt);
                    var ok = await _mqtt.PublishAsync(stopLossTopic, payload);

                    if (!ok)
                    {
                        await _buffer.InsertAsync(stopLossTopic, payload);
                        _logger.LogWarning("StopLoss MQTT buffered (Machine {MachineNo})", stopLossEvt.MachineNo);
                        return;
                    }
                }

                // ================= PUBLISH DTE EVENT =================
                var dteTopic = _config["MQTT:DteTopic"] ?? "DATA/MachineData/DTE";
                while (readDataService.TryDequeueDte(out var dteEvt))
                {
                    var payload = JsonConvert.SerializeObject(dteEvt);
                    var ok = await _mqtt.PublishAsync(dteTopic, payload);

                    if (!ok)
                    {
                        await _buffer.InsertAsync(dteTopic, payload);
                        _logger.LogWarning("DTE MQTT buffered (Machine {MachineNo})", dteEvt.MachineNo);
                        return;
                    }
                }

                // ================= PUBLISH UNLOADING PARTS EVENT =================
                var unloadingTopic = _config["MQTT:UnloadingTopic"] ?? "DATA/MachineData/UnloadingParts";

                while (readDataService.TryDequeueUnloading(out var unloadingEvt))
                {
                    // payload sesuai konsep loading:
                    // { "UnloadMachineNo": XX }
                    var payload = JsonConvert.SerializeObject(new
                    {
                        UnloadMachineNo = unloadingEvt.UnloadMachineNo
                    });

                    var ok = await _mqtt.PublishAsync(unloadingTopic, payload);

                    if (!ok)
                    {
                        await _buffer.InsertAsync(unloadingTopic, payload);
                        _logger.LogWarning("UnloadingParts MQTT buffered (UnloadMachineNo {UnloadMachineNo})", unloadingEvt.UnloadMachineNo);
                        return; // mqtt down -> stop tick
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT publish exception");
            }
            finally
            {
                Interlocked.Exchange(ref _publishing, 0);
            }
        }

        /// <summary>
        /// Flush SQLite buffer (FIFO):
        /// - ambil PENDING batch
        /// - publish satu per satu
        /// - kalau semua sukses -> delete ids
        /// - stop kalau mqtt down di tengah
        /// </summary>
        private async Task FlushMqttBufferIfAnyAsync(int batchSize = 200, int maxBatchesPerTick = 5)
        {
            for (int b = 0; b < maxBatchesPerTick; b++)
            {
                var batch = await _buffer.GetPendingAsync(batchSize);
                if (batch == null || batch.Count == 0)
                    return;

                var successIds = new List<long>(batch.Count);

                foreach (var msg in batch)
                {
                    var ok = await _mqtt.PublishAsync(msg.Topic, msg.PayloadJson);
                    if (!ok)
                    {
                        _logger.LogWarning("Flush buffer stopped (MQTT still down).");
                        return;
                    }

                    successIds.Add(msg.Id);
                }

                await _buffer.DeleteManyAsync(successIds);
                _logger.LogInformation("Flushed {Count} buffered messages", successIds.Count);
            }
        }

        /// <summary>
        /// Timer reconnect PLC (retry connect).
        /// Setelah connect sukses:
        /// - buat ReadDataService (kalau belum ada)
        /// - tandai reconnect (AlarmTimeTotal gap)
        /// - start read loop
        /// - start healthcheck thread
        /// - stop timer connect
        /// </summary>
        private async void _timerConnectPLC_Elapsed(object? sender, ElapsedEventArgs e)
        {
            try
            {
                if (firstConnect) return;

                _logger.LogInformation("Trying to connect to PLC {Ip}:{Port} ...", Ip, Port);

                firstConnect = await ConnectToPlc();
                if (!firstConnect)
                {
                    _logger.LogWarning("PLC connection failed, retrying...");
                    return;
                }

                _logger.LogInformation("PLC connected successfully ✅");

                if (plc == null)
                {
                    firstConnect = false;
                    return;
                }

                if (readDataService == null)
                {
                    readDataService = new ReadDataService(plc, _config);
                    _logger.LogInformation("ReadDataService created.");
                }

                // ✅ tandai reconnect untuk AlarmTimeTotal (LOCAL TIME)
                readDataService.MarkPlcReconnected(DateTime.Now);

                readDataService.Start();
                _logger.LogInformation("ReadDataService started.");

                if (!isThreadRunning)
                {
                    isThreadRun = true;
                    thread = new Thread(ThreadReadServer) { IsBackground = true };
                    thread.Start();
                    isThreadRunning = true;
                    _logger.LogInformation("PLC health-check thread started.");
                }

                _timerConnectPLC.Enabled = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connect timer exception");
                firstConnect = false;
                _timerConnectPLC.Enabled = true;
            }
        }

        private async Task<bool> ConnectToPlc()
        {
            try
            {
                plc ??= new SiemensS7Net(SiemensPLCS.S1500, Ip)
                {
                    ConnectTimeOut = 3000,
                    ReceiveTimeOut = 3000
                };

                var connect = await plc.ConnectServerAsync();

                if (connect.IsSuccess)
                {
                    isConnectedPLC = true;
                    PLCMessageStatus = connect.Message;
                    return true;
                }

                isConnectedPLC = false;
                PLCMessageStatus = connect.Message;
                return false;
            }
            catch (Exception ex)
            {
                isConnectedPLC = false;
                PLCMessageStatus = ex.Message;
                _logger.LogError(ex, "PLC CONNECTION EXCEPTION | IP={Ip}", Ip);
                return false;
            }
        }

        /// <summary>
        /// Thread health-check:
        /// baca bit M0.0 tiap 1 detik.
        /// Kalau gagal -> anggap disconnect -> trigger reconnect flow.
        /// </summary>
        private async void ThreadReadServer()
        {
            try
            {
                while (isThreadRun)
                {
                    await Task.Delay(timerLoop);

                    if (plc == null)
                    {
                        MarkDisconnectedAndRetry();
                        return;
                    }

                    var systemUsed = plc.ReadBool("M0.0");
                    PLCMessageStatus = systemUsed.Message;

                    if (!systemUsed.IsSuccess)
                    {
                        MarkDisconnectedAndRetry();
                        return;
                    }

                    isConnectedPLC = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PLC ThreadReadServer exception. Reconnecting...");
                MarkDisconnectedAndRetry();
            }
        }

        /// <summary>
        /// Saat PLC disconnect:
        /// - stop read loop
        /// - tandai disconnect time untuk AlarmTimeTotal gap
        /// - reset tracker stoploss/dte
        /// - enable reconnect timer
        /// </summary>
        private void MarkDisconnectedAndRetry()
        {
            try
            {
                isConnectedPLC = false;

                isThreadRun = false;
                isThreadRunning = false;
                firstConnect = false;

                readDataService?.MarkPlcDisconnected(DateTime.Now);
                readDataService?.ResetStopLossAndDteTrackers();
                readDataService?.ResetAlarmTrackers();

                readDataService?.Stop();

                try
                {
                    plc?.ConnectClose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error while closing PLC connection");
                }

                _timerConnectPLC.Enabled = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MarkDisconnectedAndRetry failed");
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting Siemens S7-1500 service...");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                    await Task.Delay(1000, stoppingToken);
            }
            catch (TaskCanceledException) { }
            finally
            {
                _logger.LogInformation("Stopping Siemens S7-1500 service...");

                isThreadRun = false;
                firstConnect = false;

                _timerConnectPLC.Stop();
                _timerConnectPLC.Enabled = false;

                _timerPublishMQTT.Stop();
                _timerPublishMQTT.Enabled = false;

                if (thread != null && thread.IsAlive)
                    thread.Join(3000);
            }
        }

        public override void Dispose()
        {
            isThreadRun = false;

            if (thread != null && thread.IsAlive)
                thread.Join(2000);

            _timerConnectPLC.Stop();
            _timerConnectPLC.Dispose();

            _timerPublishMQTT.Stop();
            _timerPublishMQTT.Dispose();

            readDataService?.Dispose();

            base.Dispose();
        }
    }
}
