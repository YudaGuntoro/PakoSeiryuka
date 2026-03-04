using Microsoft.Extensions.Configuration;
using System;

namespace PakoSeiryuka.Singletone
{
    public static class dbConfig
    {
        private static IConfiguration? _config;

        // WAJIB dipanggil sekali di Program.cs
        public static void Init(IConfiguration config)
        {
            _config = config;
        }
        public static string MysqlConnString
        {
            get
            {
                if (_config == null)
                    throw new InvalidOperationException(
                        "dbConfig not initialized. Call dbConfig.Init(configuration) first.");

                return BuildMysqlConnString(_config);
            }
        }
        private static string BuildMysqlConnString(IConfiguration config)
        {
            string server = config["Connections:Server"] ?? "127.0.0.1";
            string port = config["Connections:Port"] ?? "3306";
            string userId = config["Connections:UserID"] ?? "root_native";
            string password = config["Connections:Password"] ?? "";
            string database = config["Connections:Db"] ?? "pakoakuina";

            string connTimeout = config["Connections:TimeOut"] ?? "5";
            string cmdTimeout = config["Connections:CommandTimeOut"] ?? "30";

            return
                $"Server={server};" +
                $"Port={port};" +
                $"User ID={userId};" +
                $"Password={password};" +
                $"Database={database};" +
                $"Connection Timeout={connTimeout};" +
                $"Default Command Timeout={cmdTimeout};" +
                $"Pooling=true;" +
                $"Minimum Pool Size=0;" +
                $"Maximum Pool Size=100;" +
                $"SslMode=None;" +
                $"Allow User Variables=True;";
        }
    }
}
