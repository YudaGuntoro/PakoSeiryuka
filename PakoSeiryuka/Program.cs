using HslCommunication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PakoSeiryuka.MQTT;
using PakoSeiryuka.PLC.SIEMENS;
using PakoSeiryuka.Singletone;
using Serilog;
using Serilog.Events;
using System;

namespace PakoSeiryuka
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // ===============================
            // SERILOG CONFIG (WORKER/HOST)
            // ===============================
            Log.Logger = new LoggerConfiguration()
                // Atur level global
                .MinimumLevel.Information()

                // Kurangi noise dari framework (opsional)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)

                // Enrichment biar log enak ditrace
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentName()
                .Enrich.WithThreadId()

                // Console sink
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}"
                )

                // Rolling file sink (harian)
                .WriteTo.File(
                    path: "logs/pakoseiryuka-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    fileSizeLimitBytes: 20_000_000,     // 20MB per file
                    rollOnFileSizeLimit: true,
                    shared: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}"
                )
                .CreateLogger();

            try
            {
                // ===============================
                // LICENSE CHECK
                // ===============================
                if (!Authorization.SetAuthorizationCode("9acc2770-cadf-459a-8b16-f0a50f8f7368"))
                {
                    Log.Error("License not active in 8 hours. Application will be closed.");
                    Console.WriteLine("License Not Active in 8 Hours. App will be closed.");
                    Environment.Exit(1);
                }

                var builder = Host.CreateApplicationBuilder(args);
                dbConfig.Init(builder.Configuration);
                
                
                // Pakai Serilog untuk ILogger<T>
                builder.Logging.ClearProviders();
                builder.Logging.AddSerilog(Log.Logger, dispose: true);

                builder.Configuration
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddYamlFile("appsettings.yaml", optional: false, reloadOnChange: true)
                    .AddEnvironmentVariables();

                // ===============================
                // SERVICES / HOSTED SERVICES
                // ===============================
                // SQLite buffer (1 file DB)
                builder.Services.AddSingleton(
                    new MqttBufferSqliteRepository("pako_seiryuka.db")
                );

                // MQTTClient: DAFTARKAN SEBAGAI SINGLETON
                builder.Services.AddSingleton<MQTTClient>();

                // MQTTClient juga dijalankan sebagai HostedService
                builder.Services.AddHostedService(sp => sp.GetRequiredService<MQTTClient>());

                // Dispatcher (pakai MQTTClient + SQLite)
                builder.Services.AddHostedService<MqttBufferDispatcher>();

                // PLC Worker
                builder.Services.AddHostedService<S7_1500>();


                var host = builder.Build();

                Log.Information("Application starting up...");
                await host.RunAsync();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
