using HslCommunication.Profinet.Melsec;
using PakoSeiryuka.Singletone;
using System;
using System.Drawing;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PakoSeiryuka.PLC.MITSUBISHI.Service;
using Newtonsoft.Json;
using PakoSeiryuka.MQTT;
using PakoSeiryuka.Model.MITSUBISHI;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace PakoSeiryuka.PLC.MITSUBISHI
{
    public class FX5U : BackgroundService
    {
        private readonly ILogger<FX5U> _logger;
        private readonly string Ip;
        private readonly int Port;
        private MelsecMcNet plc;
        private System.Timers.Timer _timerConnectPLC;
        private Thread? thread;
        private bool firstConnect;
        private bool isThreadRun = false;
        private readonly int timerLoop = 3000; // 1 second delay between reads
        private bool isThreadRunning = false;
        private System.Timers.Timer _timerPublishMQTT;
        private bool isConnectedPLC = false;
        private string PLCMessageStatus;
        DetailsData detailsData = new DetailsData();
        public string DeviceName { get; private set; } = "MitsubishiPLC";
        public FX5U(ILogger<FX5U> logger, IConfiguration config)
        {
            plc = new MelsecMcNet();
            _logger = logger;
            Ip = Config.Instance.Read("IP", DeviceName) ?? throw new ArgumentNullException("IP", "IP address cannot be null");
            if (!int.TryParse(Config.Instance.Read("Port", DeviceName)?.Trim(), out Port))
            {
                throw new ArgumentException($"Invalid Port configuration for {DeviceName}");
            }

            _timerConnectPLC = new System.Timers.Timer
            {
                Interval = 5000 // 2 seconds
            };
            _timerConnectPLC.Elapsed += _timerConnectPLC_Elapsed;
            _timerConnectPLC.Enabled = true;

            _timerPublishMQTT = new System.Timers.Timer();
            _timerPublishMQTT.Interval = 1000;
            _timerPublishMQTT.Elapsed += _timerPublishMQTT_Elapsed;
            _timerPublishMQTT.Enabled = true;

        }
        private async void _timerPublishMQTT_Elapsed(object? sender, ElapsedEventArgs e)
        {
            try
            {
                var data = new PLCStatus()
                {
                    PLC_Ip = this.Ip,
                    PLC_Port = this.Port,
                    IsConnected = isConnectedPLC,
                    MessageStatus = PLCMessageStatus,
                    Data = new List<DetailsData>
                    {
                        new DetailsData 
                        { 
                            isMachineRunning = DetailsData.Instance.isMachineRunning,
                            isMachineStop = DetailsData.Instance.isMachineStop,
                        },
                    }
                };
                var objStr = JsonConvert.SerializeObject(data);
                var Topic = Config.Instance.Read("MitsubishiTopic", "MQTT");
                //await MQTTClient.Instance.MqttClientPublish(Topic, objStr);
            }
            catch { }
            finally
            {
                _timerPublishMQTT.Enabled = true;
            }
        }
        private async void _timerConnectPLC_Elapsed(object? sender, ElapsedEventArgs e)
        {
            firstConnect = await ConnectToPlc();
            _timerConnectPLC.Enabled = false;
            if (firstConnect)
            {
                var testRead = plc.ReadInt16("D0");
                if (testRead.IsSuccess)
                {
                    PLCMessageStatus = testRead.Message;
                    _timerConnectPLC.Enabled = false;
                    if (!this.isThreadRunning)
                    {
                        new Thread(() => new ReadDataService(plc)).Start();
                        this.isThreadRunning = true;
                    }
                   
                }
                StartReading();
            }
            else
            {
                _timerConnectPLC.Enabled = true;
            }
        }
        private async Task<bool> ConnectToPlc()
        {
            using Ping ping = new();
            try
            {
                _logger.LogInformation($"Pinging {Ip}...");
                PingReply reply = await ping.SendPingAsync(Ip, 2000); // 2-second timeout
                if (reply.Status == IPStatus.Success)
                {
                    plc = new MelsecMcNet(Ip, Port);
                    var connect = await plc.ConnectServerAsync();
                    Console.WriteLine("Mitsubishi" + connect.Message);
                    if (connect.IsSuccess)
                    {
                        SetConnected();
                        _logger.LogInformation($"Ping to {Ip} PLC successful. Response time: {reply.RoundtripTime}ms");
                        return true;
                    }
                    else
                    {
                        SetReconnecting();
                        _logger.LogWarning($"Ping to {Ip} successful, but PLC connection failed. {connect.Message}");
                        return false;
                    }
                }
                else
                {
                    SetReconnecting();
                    _logger.LogWarning($"Ping to {Ip} PLC failed. Status: {reply.Status}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                SetReconnecting();
                _logger.LogError(ex, $"An error occurred while pinging {Ip}");
                return false;
            }
        }
        private void SetReconnecting()
        {
            UpdateConnectionStatus($"{DeviceName} Reconnecting", Color.Red);
        }
        private void SetConnected()
        {
            UpdateConnectionStatus($"{DeviceName} Connected", Color.Green);
        }
        private void UpdateConnectionStatus(string statusText, Color color)
        {
            _logger.LogInformation($"Status Update: {statusText}");
        }
        public void StartReading()
        {
            if (!isThreadRun)
            {
                isThreadRun = true;
                thread = new Thread(ThreadReadServer)
                {
                    IsBackground = true
                };
                thread.Start();
            }
        }
        private async void ThreadReadServer()
        {
            try
            {
                while (isThreadRun)
                {
                    await Task.Delay(timerLoop);
                    if (firstConnect)
                    {
                        var systemUsed = plc.ReadBool("X1");
                        PLCMessageStatus = systemUsed.Message;
                        if (!systemUsed.IsSuccess)
                        {
                            isConnectedPLC = false;  
                            //_logger.LogError($"Connection to PLC Failed: {systemUsed.Message}");
                            _timerConnectPLC.Enabled = true; 
                            isThreadRun = false;
                        }
                        else
                        {
                            isConnectedPLC = true;
                            //_logger.LogInformation($"PLC System is {(systemUsed.Content ? "Active" : "Inactive")}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during PLC reading");
                isThreadRun = false;
            }
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting MitsubishiPLC1 service...");
            _timerConnectPLC.Start();

            // Keep the service running until stopped
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }

            _logger.LogInformation("Stopping MitsubishiPLC1 service...");
            isThreadRun = false;
            _timerConnectPLC.Stop();
        }
        public override void Dispose()
        {
            _timerConnectPLC?.Dispose();
            base.Dispose();
        }
    }
}
