using Microsoft.Data.Sqlite;
using System;
using System.Data;
using System.IO;
using System.Text;

namespace SqliteConfigStore
{
    public class SQLiteDatabase : IDisposable
    {
        public SqliteConnection DBConnection { get; private set; }
        public string DBLocation { get; private set; }
        public string LastError { get; private set; } = string.Empty;

        protected string[] requiredTableList = Array.Empty<string>();
        protected const string appSettingsTableName = "appconfig";

        // ================= OPEN / CREATE =================

        public bool Open(string filename)
        {
            try
            {
                DBLocation = filename;

                // SQLite auto-create file
                DBConnection = new SqliteConnection($"Data Source={filename}");
                DBConnection.Open();

                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        public void Close()
        {
            try
            {
                DBConnection?.Close();
                DBConnection?.Dispose();
                DBConnection = null;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        // ================= QUERY =================

        public DataTable DoQuery(string query)
        {
            LastError = string.Empty;

            if (DBConnection == null)
                return null;

            try
            {
                using var cmd = DBConnection.CreateCommand();
                cmd.CommandText = query;

                using var reader = cmd.ExecuteReader();
                var dt = new DataTable();
                dt.Load(reader);
                return dt;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return null;
            }
        }

        public int DoNonQuery(string query)
        {
            LastError = string.Empty;

            if (DBConnection == null)
                return -1;

            try
            {
                using var cmd = DBConnection.CreateCommand();
                cmd.CommandText = query;
                return cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return -1;
            }
        }

        public string DoQuerySingle(string query)
        {
            var dt = DoQuery(query);
            if (dt == null || dt.Rows.Count == 0)
                return string.Empty;

            return dt.Rows[0][0]?.ToString() ?? string.Empty;
        }

        public int DoQueryCount(string query)
        {
            var dt = DoQuery(query);
            return dt?.Rows.Count ?? 0;
        }

        // ================= TABLE CHECK =================

        public bool HasTable(string tableName)
        {
            string sql =
                $"SELECT 1 FROM sqlite_master WHERE type='table' AND name='{Escape(tableName)}'";
            return DoQueryCount(sql) > 0;
        }

        public bool HasRequiredTables()
        {
            foreach (var tbl in requiredTableList)
            {
                if (!string.IsNullOrWhiteSpace(tbl) && !HasTable(tbl))
                    return false;
            }
            return true;
        }

        // ================= APP CONFIG =================

        protected bool PrepareAppSettings()
        {
            if (HasTable(appSettingsTableName))
                return true;

            return DoNonQuery(
                $"CREATE TABLE {appSettingsTableName} (cfg_name TEXT PRIMARY KEY, cfg_value TEXT)"
            ) > 0;
        }

        public bool SetConfig(string key, string value)
        {
            PrepareAppSettings();

            string sql =
                $"INSERT INTO {appSettingsTableName} (cfg_name, cfg_value) VALUES ('{Escape(key)}','{Escape(value)}') " +
                $"ON CONFLICT(cfg_name) DO UPDATE SET cfg_value=excluded.cfg_value";

            return DoNonQuery(sql) > 0;
        }

        public string GetConfig(string key, string defaultValue = "")
        {
            PrepareAppSettings();

            string sql =
                $"SELECT cfg_value FROM {appSettingsTableName} WHERE cfg_name='{Escape(key)}'";
            var val = DoQuerySingle(sql);

            return string.IsNullOrEmpty(val) ? defaultValue : val;
        }

        // ================= HELPERS =================

        protected string Escape(string v) => v.Replace("'", "''");

        public void Dispose()
        {
            Close();
        }
    }
}
