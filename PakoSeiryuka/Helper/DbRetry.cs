using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PakoSeiryuka.Helper
{
    public static class DbRetry
    {
        public static bool IsTransient(MySqlException ex)
        {
            // 0: cannot connect / network
            // 1042: can't get hostname / can't connect
            // 1047: unknown command (kadang saat startup)
            // 1152/1153/1158/1159/1160/1161: connection issues
            // 1205/1213: lock wait / deadlock
            // 2002/2003: connection refused
            return ex.Number is 0 or 1042 or 1047 or 1152 or 1153 or 1158 or 1159 or 1160 or 1161 or 1205 or 1213 or 2002 or 2003;
        }

        public static async Task OpenWithRetryAsync(
            MySqlConnection conn,
            ILogger logger,
            string operationName,
            CancellationToken ct,
            int maxRetry = 10,
            int baseDelayMs = 500)
        {
            for (int attempt = 1; attempt <= maxRetry; attempt++)
            {
                try
                {
                    await conn.OpenAsync(ct);
                    return;
                }
                catch (MySqlException ex) when (IsTransient(ex))
                {
                    int delay = Math.Min(10_000, baseDelayMs * attempt); // linear backoff capped
                    logger.LogWarning(ex,
                        "[DB] {Op} attempt {Attempt}/{Max} failed. Retry in {Delay}ms. MySqlErr={ErrNo}",
                        operationName, attempt, maxRetry, delay, ex.Number);

                    await Task.Delay(delay, ct);
                }
            }

            // last try (biar exception asli keluar)
            await conn.OpenAsync(ct);
        }
    }
}
