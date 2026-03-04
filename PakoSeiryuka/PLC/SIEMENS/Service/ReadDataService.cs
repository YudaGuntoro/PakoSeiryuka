using HslCommunication.Profinet.Siemens;
using PakoSeiryuka.Dtos;
using PakoSeiryuka.Helper;
using PakoSeiryuka.Model.SIEMENS;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Timers;
using Microsoft.Extensions.Configuration; // pastikan ada

namespace PakoSeiryuka.PLC.SIEMENS.Service
{
    public class ReadDataService : IDisposable
    {
        private readonly SiemensS7Net _plc;
        private readonly System.Timers.Timer _timerLoop;
        private readonly int _plcNo;
        // ==========================================================
        // STOPLOSS (MQTT: DATA/MachineData/StopLoss) - StartStopSignal
        // ==========================================================

        private readonly bool[] _prevStartStop = new bool[5];
        private readonly DateTime?[] _stopLossStartTime = new DateTime?[5];

        private readonly ConcurrentQueue<StopLossEventDto> _stopLossQueue = new();
        public bool TryDequeueStopLoss(out StopLossEventDto evt) => _stopLossQueue.TryDequeue(out evt);


        // ==========================================================
        // DTE (MQTT: DATA/MachineData/DTE) - MachineOn
        // ==========================================================

        private readonly bool[] _prevMachineOn = new bool[5];
        private readonly DateTime?[] _dteStartTime = new DateTime?[5];

        private readonly ConcurrentQueue<DteEventDto> _dteQueue = new();
        public bool TryDequeueDte(out DteEventDto evt) => _dteQueue.TryDequeue(out evt);

        // ==========================================================
        // CYCLE + CHANGE MODEL
        // ==========================================================

        // last counter untuk deteksi naik (publish per cycle)
        private readonly int[] _lastCounter = new int[5];

        private readonly ConcurrentQueue<MachineCycleEvent> _cycleQueue = new();
        public bool TryDequeueCycle(out MachineCycleEvent evt) => _cycleQueue.TryDequeue(out evt);

        // simpan payload terakhir per mesin (untuk ChangeModel saat counter reset ke 0)
        private readonly MachineCycleEvent?[] _lastCyclePayload = new MachineCycleEvent?[5];

        private readonly ConcurrentQueue<MachineCycleEvent> _changeModelQueue = new();
        public bool TryDequeueChangeModel(out MachineCycleEvent evt) => _changeModelQueue.TryDequeue(out evt);

        // ==========================================================
        // ALARM TIME TOTAL (MQTT: DATA/MachineData/AlarmTimeTotal)
        // ==========================================================

        // status alarm sebelumnya (edge detection)
        // index: [machineIndex, alarmIndex]
        private readonly bool[,] _prevMachineAlarms = new bool[5, 48];

        // waktu start alarm saat alarm ON
        // index: [machineIndex, alarmIndex]
        private readonly DateTime?[,] _alarmStartTime = new DateTime?[5, 48];

        // queue event alarm duration (diambil oleh MQTT publisher di S7_1500)
        private readonly ConcurrentQueue<AlarmTimeTotalEventDto> _alarmTimeTotalQueue = new();
        public bool TryDequeueAlarmTimeTotal(out AlarmTimeTotalEventDto evt)
            => _alarmTimeTotalQueue.TryDequeue(out evt);

        // ================= GLOBAL ALARM TRACKER =================
        private readonly bool[] _prevGlobalAlarms = new bool[48];
        private readonly DateTime?[] _globalAlarmStartTime = new DateTime?[48];

        // waktu PLC disconnect/reconnect (LOCAL TIME) untuk hitung gap di tengah alarm
        private DateTime? _lastDisconnect;
        private DateTime? _lastReconnect;

        /// <summary>
        /// Dipanggil dari S7_1500 saat PLC terputus (LOCAL TIME).
        /// </summary>
        public void MarkPlcDisconnected(DateTime now) => _lastDisconnect = now;

        /// <summary>
        /// Dipanggil dari S7_1500 saat PLC berhasil reconnect (LOCAL TIME).
        /// </summary>
        public void MarkPlcReconnected(DateTime now) => _lastReconnect = now;

        // ==========================================================
        // INTERNAL (ANTI OVERLAP + LOCK)
        // ==========================================================

        private int _isReading = 0;     // anti overlap loop timer
        private readonly object _sync = new();

        // ==========================================================
        // PLC ADDRESS CONST
        // ==========================================================

        private const string DB = "DB69";
        private const ushort STR20 = 20;

        private const int TEMP_BASE_ALL = 310;
        private static readonly int[] COOL_BASE = { 374, 418, 462, 506, 550 };

        // Start byte sesuai mapping DB69.DBX594..623 (per mesin)
        private static readonly int[] MACHINE_ALARM_START_B = { 594, 600, 606, 612, 618 };

        // Global alarm sesuai mapping DB69.DBX624..629
        private const int GLOBAL_ALARM_START_B = 624;

        // ==========================================================
        // GLOBAL DATA (Bukan per machine)
        // ==========================================================

        public bool LoadTrigger { get; private set; }
        public bool UnloadTrigger { get; private set; }
        public short LoadMachineNo { get; private set; }
        public short UnloadMachineNo { get; private set; }

        public bool[] GlobalAlarms { get; } = new bool[48];
        public int TemperatureMetal { get; private set; }
        public string[] MaterialQueue { get; } = new string[20];

        // ==========================================================
        // LOADING PARTS (MQTT: DATA/MachineData/LoadingParts)
        // ==========================================================
        private bool _prevLoadTrigger = false;

        private readonly ConcurrentQueue<LoadingPartsEventDto> _loadingQueue = new();
        public bool TryDequeueLoading(out LoadingPartsEventDto evt) => _loadingQueue.TryDequeue(out evt);

        // ==========================================================
        // UNLOADING PARTS (MQTT: DATA/MachineData/UnloadingParts)
        // ==========================================================
        private bool _prevUnloadTrigger = false;

        private readonly ConcurrentQueue<UnloadingPartsEventDto> _unloadingQueue = new();
        public bool TryDequeueUnloading(out UnloadingPartsEventDto evt) => _unloadingQueue.TryDequeue(out evt);

        // ==========================================================
        // MACHINE SNAPSHOT
        // ==========================================================

        public List<DetailsData> Machines { get; } = new()
        {
            new DetailsData(),
            new DetailsData(),
            new DetailsData(),
            new DetailsData(),
            new DetailsData()
        };

        public ReadDataService(SiemensS7Net plc, IConfiguration config)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));

            _plcNo = config.GetValue<int?>("SiemensPLC:PLC") ?? 0;

            _timerLoop = new System.Timers.Timer(200)
            {
                AutoReset = true,
                Enabled = false
            };
            _timerLoop.Elapsed += TimerLoop_Elapsed;
        }

        public void Start() => _timerLoop.Enabled = true;
        public void Stop() => _timerLoop.Enabled = false;

        public PlcSnapshotDto SnapshotAll()
        {
            lock (_sync)
            {
                return new PlcSnapshotDto
                {
                    Machines = Machines.ToList(),
                    LoadTrigger = LoadTrigger,
                    UnloadTrigger = UnloadTrigger,
                    LoadMachineNo = LoadMachineNo,
                    UnloadMachineNo = UnloadMachineNo,
                    TemperatureMetal = TemperatureMetal,
                    MaterialQueue = MaterialQueue.ToArray(),
                    GlobalAlarms = GlobalAlarms.ToArray()
                };
            }
        }

        private void TimerLoop_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (System.Threading.Interlocked.Exchange(ref _isReading, 1) == 1)
                return;

            lock (_sync)
            {
                try
                {
                    // ================= MachineNo =================
                    Machines[0].MachineNo = ReadInt16($"{DB}.DBW0");
                    Machines[1].MachineNo = ReadInt16($"{DB}.DBW2");
                    Machines[2].MachineNo = ReadInt16($"{DB}.DBW4");
                    Machines[3].MachineNo = ReadInt16($"{DB}.DBW6");
                    Machines[4].MachineNo = ReadInt16($"{DB}.DBW8");

                    // ================= TypeProduct STRING[20] =================
                    Machines[0].TypeProduct = ReadS7String($"{DB}.DBB12", STR20);
                    Machines[1].TypeProduct = ReadS7String($"{DB}.DBB34", STR20);
                    Machines[2].TypeProduct = ReadS7String($"{DB}.DBB56", STR20);
                    Machines[3].TypeProduct = ReadS7String($"{DB}.DBB78", STR20);
                    Machines[4].TypeProduct = ReadS7String($"{DB}.DBB100", STR20);

                    // ================= ItemCode STRING[20] =================
                    Machines[0].ItemCode = ReadS7String($"{DB}.DBB122", STR20);
                    Machines[1].ItemCode = ReadS7String($"{DB}.DBB144", STR20);
                    Machines[2].ItemCode = ReadS7String($"{DB}.DBB166", STR20);
                    Machines[3].ItemCode = ReadS7String($"{DB}.DBB188", STR20);
                    Machines[4].ItemCode = ReadS7String($"{DB}.DBB210", STR20);

                    // ================= Mold & SideMold =================
                    for (int i = 0; i < 5; i++)
                    {
                        Machines[i].Mold = ReadInt16($"{DB}.DBW{230 + (i * 2)}");
                        Machines[i].SideMold = ReadInt16($"{DB}.DBW{240 + (i * 2)}");
                    }

                    // ================= Creation Date =================
                    ReadCreationDate(Machines[0], 250, 252, 253, 254, 255, 256, 257, 258);
                    ReadCreationDate(Machines[1], 262, 264, 265, 266, 267, 268, 269, 270);
                    ReadCreationDate(Machines[2], 274, 276, 277, 278, 279, 280, 281, 282);
                    ReadCreationDate(Machines[3], 286, 288, 289, 290, 291, 292, 293, 294);
                    ReadCreationDate(Machines[4], 298, 300, 301, 302, 303, 304, 305, 306);

                    // ================= TempMold (GLOBAL C1..C5) =================
                    var tempChannels = ReadTempMoldAllChannels(TEMP_BASE_ALL);
                    for (int i = 0; i < 5; i++)
                    {
                        if (tempChannels[i] != null)
                        {
                            Machines[i].TemperatureMold[0] = tempChannels[i]![0];
                            Machines[i].TemperatureMold[1] = tempChannels[i]![1];
                            Machines[i].TemperatureMold[2] = tempChannels[i]![2];
                            Machines[i].TemperatureMold[3] = tempChannels[i]![3];
                        }
                    }

                    // ================= Temperature Metal (GLOBAL) =================
                    TemperatureMetal = ReadInt32($"{DB}.DBD350");

                    // ================= Counter (per machine) =================
                    int[] counter =
                    {
                        ReadInt32Sticky($"{DB}.DBD354", _lastCounter[0]),
                        ReadInt32Sticky($"{DB}.DBD358", _lastCounter[1]),
                        ReadInt32Sticky($"{DB}.DBD362", _lastCounter[2]),
                        ReadInt32Sticky($"{DB}.DBD366", _lastCounter[3]),
                        ReadInt32Sticky($"{DB}.DBD370", _lastCounter[4]),
                    };

                    // ================= CycleTime (per machine) =================
                    int[] cycle =
                    {
                        ReadInt16($"{DB}.DBW634"),
                        ReadInt16($"{DB}.DBW636"),
                        ReadInt16($"{DB}.DBW638"),
                        ReadInt16($"{DB}.DBW640"),
                        ReadInt16($"{DB}.DBW642"),
                    };

                    // ================= Start/Stop (per machine) =================
                    bool[] startStop =
                    {
                        ReadBool($"{DB}.DBX5764.0"),
                        ReadBool($"{DB}.DBX5764.1"),
                        ReadBool($"{DB}.DBX5764.2"),
                        ReadBool($"{DB}.DBX5764.3"),
                        ReadBool($"{DB}.DBX5764.4"),
                    };

                    // ================= Machine On (per machine) =================
                    bool[] machineOn =
                    {
                        ReadBool($"{DB}.DBX5786.0"),
                        ReadBool($"{DB}.DBX5786.1"),
                        ReadBool($"{DB}.DBX5786.2"),
                        ReadBool($"{DB}.DBX5786.3"),
                        ReadBool($"{DB}.DBX5786.4"),
                    };

                    // ================= Group STRING[1] (per machine) =================
                    string[] group =
                    {
                        ReadS7String($"{DB}.DBB5768", 1),
                        ReadS7String($"{DB}.DBB5772", 1),
                        ReadS7String($"{DB}.DBB5776", 1),
                        ReadS7String($"{DB}.DBB5780", 1),
                        ReadS7String($"{DB}.DBB5784", 1),
                    };

                    // ================= Metal Weight (per machine, REAL) =================
                    float[] metalWeight =
                    {
                        ReadFloat($"{DB}.DBD5794"),
                        ReadFloat($"{DB}.DBD5798"),
                        ReadFloat($"{DB}.DBD5802"),
                        ReadFloat($"{DB}.DBD5806"),
                        ReadFloat($"{DB}.DBD5810"),
                    };

                    // ================= Material Queue (GLOBAL) =================
                    MaterialQueue[0] = ReadS7String($"{DB}.DBB646", STR20);
                    MaterialQueue[1] = ReadS7String($"{DB}.DBB902", STR20);
                    MaterialQueue[2] = ReadS7String($"{DB}.DBB1158", STR20);
                    MaterialQueue[3] = ReadS7String($"{DB}.DBB1414", STR20);
                    MaterialQueue[4] = ReadS7String($"{DB}.DBB1670", STR20);
                    MaterialQueue[5] = ReadS7String($"{DB}.DBB1926", STR20);
                    MaterialQueue[6] = ReadS7String($"{DB}.DBB2182", STR20);
                    MaterialQueue[7] = ReadS7String($"{DB}.DBB2438", STR20);
                    MaterialQueue[8] = ReadS7String($"{DB}.DBB2694", STR20);
                    MaterialQueue[9] = ReadS7String($"{DB}.DBB2950", STR20);
                    MaterialQueue[10] = ReadS7String($"{DB}.DBB3206", STR20);
                    MaterialQueue[11] = ReadS7String($"{DB}.DBB3462", STR20);
                    MaterialQueue[12] = ReadS7String($"{DB}.DBB3718", STR20);
                    MaterialQueue[13] = ReadS7String($"{DB}.DBB3974", STR20);
                    MaterialQueue[14] = ReadS7String($"{DB}.DBB4230", STR20);
                    MaterialQueue[15] = ReadS7String($"{DB}.DBB4486", STR20);
                    MaterialQueue[16] = ReadS7String($"{DB}.DBB4742", STR20);
                    MaterialQueue[17] = ReadS7String($"{DB}.DBB4998", STR20);
                    MaterialQueue[18] = ReadS7String($"{DB}.DBB5254", STR20);
                    MaterialQueue[19] = ReadS7String($"{DB}.DBB5510", STR20);

                    // ================= Cooling (PER MACHINE) =================
                    var cool = new CoolingBlock[5];
                    for (int i = 0; i < 5; i++)
                        cool[i] = ReadCoolingBlockFromBase(COOL_BASE[i]);

                    // ================= Load/Unload Trigger (GLOBAL) =================
                    LoadTrigger = ReadBool($"{DB}.DBX5788.0");
                    UnloadTrigger = ReadBool($"{DB}.DBX5788.1");

                    // ================= Load/Unload Machine No (GLOBAL) =================
                    LoadMachineNo = ReadInt16($"{DB}.DBW5790");
                    UnloadMachineNo = ReadInt16($"{DB}.DBW5792");
                    // ✅ NEW: LoadingParts event (enqueue saat LoadTrigger rising edge)
                    DetectLoadingPartsPublishOnStart(LoadTrigger, LoadMachineNo);
                    DetectUnloadingPartsPublishOnStart(UnloadTrigger, UnloadMachineNo);
                    // ================= Machine Alarms (per mesin) =================
                    for (int i = 0; i < 5; i++)
                    {
                        int startEven = MACHINE_ALARM_START_B[i];
                        var alarms = ReadAlarm48(startEven);

                        // snapshot
                        for (int a = 0; a < 48; a++)
                            Machines[i].MachineAlarms[a] = alarms[a];

                        // alarm duration publish when OFF
                        DetectAlarmDurationPublishOnEnd(i, Machines[i].MachineNo, alarms);
                    }

                    // ================= Global Alarms (global) =================
                    {
                        var g = ReadAlarm48(GLOBAL_ALARM_START_B);
                        for (int a = 0; a < 48; a++)
                            GlobalAlarms[a] = g[a];

                        // ✅ TAMBAHKAN INI
                        DetectGlobalAlarmDurationPublishOnEnd(g);
                    }

                    // ================= APPLY per machine + DETECT COUNTER NAIK =================
                    for (int i = 0; i < 5; i++)
                    {
                        var newCounter = counter[i];
                        var oldCounter = _lastCounter[i];

                        Machines[i].CounterProduct = newCounter;
                        Machines[i].CycleTime = cycle[i];
                        Machines[i].StartStopSignal = startStop[i];
                        Machines[i].MachineOn = machineOn[i];
                        Machines[i].Group = group[i];
                        Machines[i].MetalWeight = metalWeight[i];
                        Machines[i].Cooling = cool[i];

                        // ✅ NEW: StopLoss & DTE event (enqueue saat END)
                        var mNow = Machines[i];
                        if (mNow.MachineNo > 0)
                        {
                            DetectStopLossPublishOnEnd(i, mNow.MachineNo, mNow.StartStopSignal); // topic: DATA/MachineData/StopLoss
                            DetectDtePublishOnEnd(i, mNow.MachineNo, mNow.MachineOn);// topic: DATA/MachineData/DTE
                        }

                        if (newCounter > oldCounter)
                        {
                            _lastCounter[i] = newCounter;

                            var m = Machines[i];
                            var payload = new MachineCycleEvent
                            {
                                MachineNo = m.MachineNo,
                                TypeProduct = m.TypeProduct,
                                ItemCode = m.ItemCode,
                                Mold = m.Mold,
                                SideMold = m.SideMold,
                                MachineOn = m.MachineOn,
                                CreationDateTime = m.CreationDateTime?.ToString("yyyy-MM-ddTHH:mm:ss"),
                                TemperatureMold = m.TemperatureMold.ToArray(),
                                CounterProduct = m.CounterProduct,
                                CycleTime = m.CycleTime,
                                StartStopSignal = m.StartStopSignal,
                                Group = m.Group,
                                MetalWeight = m.MetalWeight,
                                Cooling = m.Cooling
                            };

                            _lastCyclePayload[i] = payload;
                            _cycleQueue.Enqueue(payload);
                        }
                        else if (newCounter == 0 && oldCounter != 0)
                        {
                            _lastCounter[i] = newCounter;

                            var lastPayload = _lastCyclePayload[i];
                            if (lastPayload != null)
                            {
                                _changeModelQueue.Enqueue(lastPayload);
                                Log.Information("ChangeModel enqueue: machine={MachineNo} old={Old} new={New}", Machines[i].MachineNo, oldCounter, newCounter);
                            }
                            else
                            {
                                Log.Warning("ChangeModel skipped (last payload null): machine={MachineNo} old={Old} new={New}", Machines[i].MachineNo, oldCounter, newCounter);
                            }
                            if (lastPayload != null)
                                _changeModelQueue.Enqueue(lastPayload);
                        }
                        else if (newCounter < oldCounter)
                        {
                            _lastCounter[i] = newCounter;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[SIEMENS] Read loop error");
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _isReading, 0);
                }
            }
        }

        private void DetectGlobalAlarmDurationPublishOnEnd(bool[] currentAlarms)
        {
            var now = DateTime.Now; // LOCAL TIME

            for (int alarmIndex = 0; alarmIndex < 48; alarmIndex++)
            {
                bool prev = _prevGlobalAlarms[alarmIndex];
                bool curr = currentAlarms[alarmIndex];

                // START (0 -> 1)
                if (!prev && curr)
                {
                    _globalAlarmStartTime[alarmIndex] = now;
                }
                // END (1 -> 0)
                else if (prev && !curr)
                {
                    var start = _globalAlarmStartTime[alarmIndex];
                    if (start.HasValue)
                    {
                        int durationSec = (int)Math.Max(0, (now - start.Value).TotalSeconds);

                        bool hasGap = false;
                        int gapSec = 0;

                        if (_lastDisconnect.HasValue && _lastReconnect.HasValue)
                        {
                            var d = _lastDisconnect.Value;
                            var r = _lastReconnect.Value;

                            if (start.Value <= d && r <= now && d < r)
                            {
                                hasGap = true;
                                gapSec = (int)Math.Max(0, (r - d).TotalSeconds);
                            }
                        }

                        _alarmTimeTotalQueue.Enqueue(new AlarmTimeTotalEventDto
                        {
                            PLC = _plcNo,
                            MachineNo = 0, // atau nanti pakai config GlobalAlarm:MachineNo kalau kamu mau
                            AlarmIndex = alarmIndex + 1,
                            StartTime = start.Value,
                            EndTime = now,
                            TotalDuration = durationSec,
                            HasDisconnectGap = hasGap,
                            DisconnectGapSeconds = gapSec
                        });
                    }

                    _globalAlarmStartTime[alarmIndex] = null;
                }

                _prevGlobalAlarms[alarmIndex] = curr;
            }
        }

        /// <summary>
        /// Alarm duration tracker:
        /// - 0 -> 1 : simpan start time
        /// - 1 -> 0 : enqueue AlarmTimeTotalEventDto
        ///
        /// Gap logic:
        /// HasDisconnectGap=true jika PLC disconnect & reconnect terjadi di dalam interval alarm:
        /// startAlarm <= disconnect <= reconnect <= endAlarm
        ///
        /// NOTE: DateTime.Now (LOCAL TIME)
        /// </summary>
        private void DetectAlarmDurationPublishOnEnd(int machineIndex, int machineNo, bool[] currentAlarms)
        {
            var now = DateTime.Now; // LOCAL TIME

            for (int alarmIndex = 0; alarmIndex < 48; alarmIndex++)
            {
                bool prev = _prevMachineAlarms[machineIndex, alarmIndex];
                bool curr = currentAlarms[alarmIndex];

                // START (0 -> 1)
                if (!prev && curr)
                {
                    _alarmStartTime[machineIndex, alarmIndex] = now;
                }
                // END (1 -> 0)
                else if (prev && !curr)
                {
                    var start = _alarmStartTime[machineIndex, alarmIndex];
                    if (start.HasValue)
                    {
                        int durationSec = (int)Math.Max(0, (now - start.Value).TotalSeconds);

                        bool hasGap = false;
                        int gapSec = 0;

                        if (_lastDisconnect.HasValue && _lastReconnect.HasValue)
                        {
                            var d = _lastDisconnect.Value;
                            var r = _lastReconnect.Value;

                            if (start.Value <= d && r <= now && d < r)
                            {
                                hasGap = true;
                                gapSec = (int)Math.Max(0, (r - d).TotalSeconds);
                            }
                        }

                        _alarmTimeTotalQueue.Enqueue(new AlarmTimeTotalEventDto
                        {
                            PLC = _plcNo,
                            MachineNo = machineNo,
                            AlarmIndex = alarmIndex + 1,
                            StartTime = start.Value,
                            EndTime = now,
                            TotalDuration = durationSec,
                            HasDisconnectGap = hasGap,
                            DisconnectGapSeconds = gapSec
                        });
                    }

                    _alarmStartTime[machineIndex, alarmIndex] = null; // reset start
                }

                _prevMachineAlarms[machineIndex, alarmIndex] = curr;
            }
        }

        public void ResetAlarmTrackers()
        {
            // machine alarms
            for (int m = 0; m < 5; m++)
            {
                for (int a = 0; a < 48; a++)
                {
                    _prevMachineAlarms[m, a] = false;
                    _alarmStartTime[m, a] = null;
                }
            }

            // global alarms
            for (int a = 0; a < 48; a++)
            {
                _prevGlobalAlarms[a] = false;
                _globalAlarmStartTime[a] = null;
            }
        }
        /// <summary>
        /// Reset tracker internal untuk event StopLoss (StartStopSignal) dan DTE (MachineOn).
        ///
        /// Kenapa perlu:
        /// - Saat PLC disconnect / aplikasi stop-start, state edge-detection bisa "nyangk`ut"
        ///   (mis: start time masih tersimpan, prev state masih True/False lama).
        /// - Kalau tidak di-reset, setelah reconnect bisa muncul event END palsu,
        ///   durasi salah, atau event tidak pernah ter-publish.
        ///
        /// Kapan dipanggil:
        /// - Idealnya saat PLC terdeteksi disconnect (sebelum reconnect loop dimulai),
        ///   dan/atau saat reconnect sukses jika ingin mulai dari state bersih.
        ///
        /// Efek:
        /// - Menghapus start time yang sedang berjalan (StopLoss/DTE yang sedang aktif akan dianggap selesai/diabaikan),
        ///   sehingga event berikutnya dihitung ulang dari transisi (edge) terbaru.
        /// </summary>
        public void ResetStopLossAndDteTrackers()
        {
            for (int i = 0; i < 5; i++)
            {
                // StopLoss: reset status sebelumnya & waktu mulai
                _prevStartStop[i] = false;
                _stopLossStartTime[i] = null;

                // DTE: reset status sebelumnya & waktu mulai downtime
                _prevMachineOn[i] = false;
                _dteStartTime[i] = null;

                // ✅ reset load & unload trigger edge tracker
                _prevLoadTrigger = false;
                _prevUnloadTrigger = false;

            }
        }

        /// <summary>
        /// StopLoss tracker (StartStopSignal):
        /// - START: False -> True
        /// - END  : True -> False (enqueue StopLossEventDto)
        /// </summary>
        private void DetectStopLossPublishOnEnd(int machineIndex, int machineNo, bool currentStartStop)
        {
            var now = DateTime.Now; // LOCAL TIME

            bool prev = _prevStartStop[machineIndex];
            bool curr = currentStartStop;

            // START (False -> True)
            if (!prev && curr)
            {
                _stopLossStartTime[machineIndex] = now;
            }
            // END (True -> False)
            else if (prev && !curr)
            {
                var start = _stopLossStartTime[machineIndex];
                if (start.HasValue)
                {
                    int durationSec = (int)Math.Max(0, (now - start.Value).TotalSeconds);

                    bool hasGap = false;
                    int gapSec = 0;

                    if (_lastDisconnect.HasValue && _lastReconnect.HasValue)
                    {
                        var d = _lastDisconnect.Value;
                        var r = _lastReconnect.Value;

                        if (start.Value <= d && r <= now && d < r)
                        {
                            hasGap = true;
                            gapSec = (int)Math.Max(0, (r - d).TotalSeconds);
                        }
                    }

                    _stopLossQueue.Enqueue(new StopLossEventDto
                    {
                        PLC = _plcNo,
                        MachineNo = machineNo,
                        StartTime = start.Value,
                        EndTime = now,
                        TotalDuration = durationSec,
                        HasDisconnectGap = hasGap,
                        DisconnectGapSeconds = gapSec
                    });
                }

                _stopLossStartTime[machineIndex] = null;
            }

            _prevStartStop[machineIndex] = curr;
        }

        /// <summary>
        /// DTE tracker (MachineOn):
        /// - START: True -> False (downtime mulai saat mesin OFF)
        /// - END  : False -> True (enqueue DteEventDto)
        /// </summary>
        private void DetectDtePublishOnEnd(int machineIndex, int machineNo, bool currentMachineOn)
        {
            var now = DateTime.Now; // LOCAL TIME

            bool prev = _prevMachineOn[machineIndex];
            bool curr = currentMachineOn;

            // START downtime (True -> False)
            if (prev && !curr)
            {
                _dteStartTime[machineIndex] = now;
            }
            // END downtime (False -> True)
            else if (!prev && curr)
            {
                var start = _dteStartTime[machineIndex];
                if (start.HasValue)
                {
                    int durationSec = (int)Math.Max(0, (now - start.Value).TotalSeconds);

                    bool hasGap = false;
                    int gapSec = 0;

                    if (_lastDisconnect.HasValue && _lastReconnect.HasValue)
                    {
                        var d = _lastDisconnect.Value;
                        var r = _lastReconnect.Value;

                        if (start.Value <= d && r <= now && d < r)
                        {
                            hasGap = true;
                            gapSec = (int)Math.Max(0, (r - d).TotalSeconds);
                        }
                    }

                    _dteQueue.Enqueue(new DteEventDto
                    {
                        PLC = _plcNo,
                        MachineNo = machineNo,
                        StartTime = start.Value,
                        EndTime = now,
                        TotalDuration = durationSec,
                        HasDisconnectGap = hasGap,
                        DisconnectGapSeconds = gapSec
                    });
                }

                _dteStartTime[machineIndex] = null;
            }

            _prevMachineOn[machineIndex] = curr;
        }

        /// <summary>
        /// LoadingParts tracker (LoadTrigger):
        /// - publish saat rising edge (False -> True)
        /// Payload: { "LoadMachineNo": XX }
        /// </summary>
        private void DetectLoadingPartsPublishOnStart(bool currentLoadTrigger, short loadMachineNo)
        {
            bool prev = _prevLoadTrigger;
            bool curr = currentLoadTrigger;

            // Rising edge (0 -> 1)
            if (!prev && curr)
            {
                _loadingQueue.Enqueue(new LoadingPartsEventDto
                {
                    LoadMachineNo = loadMachineNo,
                    Time = DateTime.Now
                });
            }

            _prevLoadTrigger = curr;
        }

        /// <summary>
        /// UnloadingParts tracker (UnloadTrigger):
        /// - publish saat rising edge (False -> True)
        /// Payload: { "UnloadMachineNo": XX }
        /// </summary>
        private void DetectUnloadingPartsPublishOnStart(bool currentUnloadTrigger, short unloadMachineNo)
        {
            bool prev = _prevUnloadTrigger;
            bool curr = currentUnloadTrigger;

            // Rising edge (0 -> 1)
            if (!prev && curr)
            {
                _unloadingQueue.Enqueue(new UnloadingPartsEventDto
                {
                    UnloadMachineNo = unloadMachineNo,
                    Time = DateTime.Now
                });
            }

            _prevUnloadTrigger = curr;
        }
        // ==========================================================
        // Helpers
        // ==========================================================

        private bool[] ReadAlarm48(int startEvenByte)
        {
            var raw = ReadBytes($"{DB}.DBB{startEvenByte}", 6);
            if (raw.Length < 6) return new bool[48];

            var result = new bool[48];
            for (int i = 0; i < 48; i++)
            {
                int b = i / 8;
                int bit = i % 8;
                result[i] = (raw[b] & (1 << bit)) != 0;
            }
            return result;
        }

        private byte[] ReadBytes(string addr, ushort length)
        {
            var r = _plc.Read(addr, length);
            return (r.IsSuccess && r.Content != null) ? r.Content : Array.Empty<byte>();
        }

        private void ReadCreationDate(
            DetailsData m,
            int yearDbw,
            int monthDbb,
            int dayDbb,
            int weekdayDbb,
            int hourDbb,
            int minuteDbb,
            int secondDbb,
            int nanoDbd)
        {
            m.CreationYear = ReadUInt16($"{DB}.DBW{yearDbw}");
            m.CreationMonth = ReadByte($"{DB}.DBB{monthDbb}");
            m.CreationDay = ReadByte($"{DB}.DBB{dayDbb}");
            m.CreationWeekday = ReadByte($"{DB}.DBB{weekdayDbb}");
            m.CreationHour = ReadByte($"{DB}.DBB{hourDbb}");
            m.CreationMinute = ReadByte($"{DB}.DBB{minuteDbb}");
            m.CreationSecond = ReadByte($"{DB}.DBB{secondDbb}");
            m.CreationNanosecond = ReadUInt32($"{DB}.DBD{nanoDbd}");
        }

        private int[]?[] ReadTempMoldAllChannels(int baseDbw)
        {
            if (baseDbw <= 0) return new int[]?[5];

            return new int[]?[]
            {
                new int[]
                {
                    ReadInt16($"{DB}.DBW{baseDbw + 0}"),
                    ReadInt16($"{DB}.DBW{baseDbw + 2}"),
                    ReadInt16($"{DB}.DBW{baseDbw + 4}"),
                    ReadInt16($"{DB}.DBW{baseDbw + 6}")
                },
                new int[]
                {
                    ReadInt16($"{DB}.DBW{baseDbw + 8}"),
                    ReadInt16($"{DB}.DBW{baseDbw + 10}"),
                    ReadInt16($"{DB}.DBW{baseDbw + 12}"),
                    ReadInt16($"{DB}.DBW{baseDbw + 14}")
                },
                new int[]
                {
                    ReadInt16($"{DB}.DBW{baseDbw + 16}"),
                    ReadInt16($"{DB}.DBW{baseDbw + 18}"),
                    ReadInt16($"{DB}.DBW{baseDbw + 20}"),
                    ReadInt16($"{DB}.DBW{baseDbw + 22}")
                },
                new int[]
                {
                    ReadInt16($"{DB}.DBW{baseDbw + 24}"),
                    ReadInt16($"{DB}.DBW{baseDbw + 26}"),
                    ReadInt16($"{DB}.DBW{baseDbw + 28}"),
                    ReadInt16($"{DB}.DBW{baseDbw + 30}")
                },
                new int[]
                {
                    ReadInt16($"{DB}.DBW{baseDbw + 32}"),
                    ReadInt16($"{DB}.DBW{baseDbw + 34}"),
                    ReadInt16($"{DB}.DBW{baseDbw + 36}"),
                    ReadInt16($"{DB}.DBW{baseDbw + 38}")
                }
            };
        }

        private CoolingBlock ReadCoolingBlockFromBase(int baseDbw)
        {
            return new CoolingBlock
            {
                WaitingAir1 = ReadInt16($"{DB}.DBW{baseDbw + 0}"),
                WaitingAir2 = ReadInt16($"{DB}.DBW{baseDbw + 2}"),
                WaitingAir3 = ReadInt16($"{DB}.DBW{baseDbw + 4}"),
                WaitingAir4 = ReadInt16($"{DB}.DBW{baseDbw + 6}"),

                WaitingWater1 = ReadInt16($"{DB}.DBW{baseDbw + 8}"),
                WaitingWater2 = ReadInt16($"{DB}.DBW{baseDbw + 10}"),

                CoolingAir1 = ReadInt16($"{DB}.DBW{baseDbw + 12}"),
                CoolingAir2 = ReadInt16($"{DB}.DBW{baseDbw + 14}"),
                CoolingAir3 = ReadInt16($"{DB}.DBW{baseDbw + 16}"),
                CoolingAir4 = ReadInt16($"{DB}.DBW{baseDbw + 18}"),

                CoolingWater1 = ReadInt16($"{DB}.DBW{baseDbw + 20}"),
                CoolingWater2 = ReadInt16($"{DB}.DBW{baseDbw + 22}"),

                AirPressure1 = ReadInt16($"{DB}.DBW{baseDbw + 24}"),
                AirPressure2 = ReadInt16($"{DB}.DBW{baseDbw + 26}"),
                AirPressure3 = ReadInt16($"{DB}.DBW{baseDbw + 28}"),
                AirPressure4 = ReadInt16($"{DB}.DBW{baseDbw + 30}"),

                FlowRate1 = ReadInt16($"{DB}.DBW{baseDbw + 32}"),
                FlowRate2 = ReadInt16($"{DB}.DBW{baseDbw + 34}"),
                FlowRate3 = ReadInt16($"{DB}.DBW{baseDbw + 36}"),
                FlowRate4 = ReadInt16($"{DB}.DBW{baseDbw + 38}"),
                FlowRate5 = ReadInt16($"{DB}.DBW{baseDbw + 40}"),
                FlowRate6 = ReadInt16($"{DB}.DBW{baseDbw + 42}"),
            };
        }

        private int ReadInt32Sticky(string addr, int fallback)
        {
            var r = _plc.ReadInt32(addr);
            return r.IsSuccess ? r.Content : fallback;
        }

        private short ReadInt16(string addr) { var r = _plc.ReadInt16(addr); return r.IsSuccess ? r.Content : (short)0; }
        private int ReadInt32(string addr) { var r = _plc.ReadInt32(addr); return r.IsSuccess ? r.Content : 0; }
        private bool ReadBool(string addr) { var r = _plc.ReadBool(addr); return r.IsSuccess && r.Content; }
        private byte ReadByte(string addr) { var r = _plc.ReadByte(addr); return r.IsSuccess ? r.Content : (byte)0; }
        private ushort ReadUInt16(string addr) { var r = _plc.ReadUInt16(addr); return r.IsSuccess ? (ushort)r.Content : (ushort)0; }
        private uint ReadUInt32(string addr) { var r = _plc.ReadInt32(addr); return r.IsSuccess ? unchecked((uint)r.Content) : 0u; }
        private float ReadFloat(string addr) { var r = _plc.ReadFloat(addr); return r.IsSuccess ? r.Content : 0f; }
        private string ReadS7String(string addr, ushort maxLen)
        {
            var raw = _plc.Read(addr, maxLen);
            if (!raw.IsSuccess || raw.Content == null) return "";
            return Encoding.ASCII.GetString(raw.Content).TrimEnd('\0').Trim();
        }
        public void Dispose()
        {
            _timerLoop.Elapsed -= TimerLoop_Elapsed;
            _timerLoop.Dispose();
        }
    }
}