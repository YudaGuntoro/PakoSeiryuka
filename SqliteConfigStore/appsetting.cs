using System;
using System.IO;

namespace SqliteConfigStore
{
    public class appsetting : IDisposable
    {
        private readonly SQLiteDatabase _db;

        /// <summary>
        /// Create / open config database (filename WITHOUT .db)
        /// </summary>
        public appsetting(string filename)
        {
            _db = new SQLiteDatabase();

            var dbPath = Path.ChangeExtension(filename, ".db");

            if (!_db.Open(dbPath))
            {
                throw new Exception($"Failed to open config DB: {_db.LastError}");
            }
        }

        // ================= READ =================

        public string ReadKey(string section, string key, string defaultValue = "")
        {
            return _db.GetConfig($"{section}_{key}", defaultValue);
        }

        public int ReadInt(string section, string key, int defaultValue = 0)
        {
            var v = _db.GetConfig($"{section}_{key}", defaultValue.ToString());
            return int.TryParse(v, out var r) ? r : defaultValue;
        }

        public bool ReadBool(string section, string key, bool defaultValue = false)
        {
            var v = _db.GetConfig($"{section}_{key}", defaultValue ? "1" : "0");
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        // ================= WRITE =================

        public void WriteKey(string section, string key, string value)
        {
            _db.SetConfig($"{section}_{key}", value);
        }

        public void WriteKey(string section, string key, int value)
        {
            _db.SetConfig($"{section}_{key}", value.ToString());
        }

        public void WriteKey(string section, string key, bool value)
        {
            _db.SetConfig($"{section}_{key}", value ? "1" : "0");
        }

        // ================= CLEANUP =================

        public void Dispose()
        {
            _db?.Dispose();
        }
    }
}
