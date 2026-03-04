using Dapper;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using PakoSeiryuka.Dtos;
using PakoSeiryuka.Helper;
using PakoSeiryuka.Singletone;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PakoSeiryuka.SQL
{
    public class SQLParameterSettings
    {
        private readonly ILogger<SQLParameterSettings> _logger;

        public SQLParameterSettings(
            ILogger<SQLParameterSettings> logger)
        {
            _logger = logger;
        }

        public async Task<IntervalTaskDto?> GetIntervalTaskAsync(
            CancellationToken ct = default)
        {
            const string op = nameof(GetIntervalTaskAsync);

            try
            {
                await using var connection =
                    new MySqlConnection(dbConfig.MysqlConnString);

                await DbRetry.OpenWithRetryAsync(
                    connection, _logger, op, ct);

                const string sql = @"
                    SELECT
                        id,
                        mqtt_interval AS Mqtt_Interval,
                        plc_interval AS Plc_Interval
                    FROM interval_tasks
                    WHERE id = 1
                    LIMIT 1;
                ";

                var row = await connection
                    .QuerySingleOrDefaultAsync<IntervalTaskDto>(
                        new CommandDefinition(
                            sql,
                            cancellationToken: ct));

                if (row != null)
                {
                    _logger.LogInformation(
                        "{Operation} | Interval loaded | Mqtt={MqttInterval} | Plc={PlcInterval}",
                        op,
                        row.Mqtt_Interval,
                        row.Plc_Interval);
                }
                else
                {
                    _logger.LogWarning(
                        "{Operation} | Interval config not found",
                        op);
                }

                return row;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "{Operation} | Error getting interval config",
                    op);

                return null;
            }
        }
    }
}
