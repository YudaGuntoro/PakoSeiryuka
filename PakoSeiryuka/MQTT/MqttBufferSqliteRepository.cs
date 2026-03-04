using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace PakoSeiryuka.MQTT
{
    public class MqttBufferSqliteRepository
    {
        private readonly string _dbPath;

        public MqttBufferSqliteRepository(string dbPath = "pako_seiryuka.db")
        {
            _dbPath = dbPath;
            Init().GetAwaiter().GetResult();
        }

        private async Task Init()
        {
            await using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            var sql = @"
CREATE TABLE IF NOT EXISTS mqtt_buffer (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  topic TEXT NOT NULL,
  payloadJson TEXT NOT NULL,
  qos INTEGER NOT NULL DEFAULT 1,
  retain INTEGER NOT NULL DEFAULT 0,
  status TEXT NOT NULL DEFAULT 'PENDING',
  retryCount INTEGER NOT NULL DEFAULT 0,
  lastError TEXT NULL,
  createdAt TEXT NOT NULL DEFAULT (datetime('now')),
  sentAt TEXT NULL
);

CREATE INDEX IF NOT EXISTS idx_mqtt_status_created
ON mqtt_buffer(status, createdAt);
";
            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task InsertAsync(string topic, string payloadJson, int qos = 1, bool retain = false)
        {
            await using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO mqtt_buffer (topic, payloadJson, qos, retain, status, retryCount, lastError)
VALUES ($topic, $payloadJson, $qos, $retain, 'PENDING', 0, NULL);
";
            cmd.Parameters.AddWithValue("$topic", topic);
            cmd.Parameters.AddWithValue("$payloadJson", payloadJson);
            cmd.Parameters.AddWithValue("$qos", qos);
            cmd.Parameters.AddWithValue("$retain", retain ? 1 : 0);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<MqttBufferRow>> GetPendingAsync(int limit = 50)
        {
            var list = new List<MqttBufferRow>();

            await using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT id, topic, payloadJson, qos, retain, retryCount
FROM mqtt_buffer
WHERE status = 'PENDING'
ORDER BY createdAt ASC
LIMIT $limit;
";
            cmd.Parameters.AddWithValue("$limit", limit);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new MqttBufferRow
                {
                    Id = reader.GetInt64(0),
                    Topic = reader.GetString(1),
                    PayloadJson = reader.GetString(2),
                    Qos = reader.GetInt32(3),
                    Retain = reader.GetInt32(4) == 1,
                    RetryCount = reader.GetInt32(5)
                });
            }

            return list;
        }

        public async Task MarkSentAsync(long id)
        {
            await using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE mqtt_buffer
SET status='SENT', sentAt=datetime('now'), lastError=NULL
WHERE id=$id;
";
            cmd.Parameters.AddWithValue("$id", id);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task MarkFailedAsync(long id, string err, int deadAfter = 50)
        {
            await using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE mqtt_buffer
SET retryCount = retryCount + 1,
    lastError = $err,
    status = CASE WHEN retryCount + 1 >= $deadAfter THEN 'DEAD' ELSE 'PENDING' END
WHERE id=$id;
";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$err", err);
            cmd.Parameters.AddWithValue("$deadAfter", deadAfter);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteManyAsync(IEnumerable<long> ids)
        {
            var idList = ids?.ToList() ?? new List<long>();
            if (idList.Count == 0) return;

            await using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            // bikin IN ($p0,$p1,...)
            var ps = idList.Select((_, i) => $"$p{i}").ToArray();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DELETE FROM mqtt_buffer WHERE id IN ({string.Join(",", ps)});";

            for (int i = 0; i < idList.Count; i++)
                cmd.Parameters.AddWithValue(ps[i], idList[i]);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<MqttBufferRow>> GetBatchAsync(int limit)
        {
            // sama persis dengan GetPendingAsync
            return await GetPendingAsync(limit);
        }

       

    }



    public class MqttBufferRow
    {
        public long Id { get; set; }
        public string Topic { get; set; } = "";
        public string PayloadJson { get; set; } = "";
        public int Qos { get; set; }
        public bool Retain { get; set; }
        public int RetryCount { get; set; }
    }
}
