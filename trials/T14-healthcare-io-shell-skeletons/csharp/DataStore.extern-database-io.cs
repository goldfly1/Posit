// DataStore — {:extern} database I/O portal implementations
// Auto-bound to Dafny stub: database-io
// DO NOT invent new structure. This file only inlays function behind pre-cut portals.

using System;
using System.Data;
using Microsoft.Data.SqlClient;

namespace _module_DataStore
{
    public partial class DataStore
    {
        public static string QueryDb(string sql)
        {
            using var conn = new SqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = new SqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            var rows = new System.Collections.Generic.List<string>();
            while (reader.Read())
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                rows.Add(System.Text.Json.JsonSerializer.Serialize(values));
            }
            return "[" + string.Join(",", rows) + "]";
        }

        public static int ExecuteDb(string sql)
        {
            using var conn = new SqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = new SqlCommand(sql, conn);
            return cmd.ExecuteNonQuery();
        }

        public static int OpenConnection(string connectionString)
        {
            var conn = new SqlConnection(connectionString);
            conn.Open();
            return conn.GetHashCode();
        }

        public static void CloseConnection(int connId)
        {
            // Simplification: real implementation would track connection pool by id.
        }

        public static int BeginTransaction(int connId)
        {
            // Simplification: real implementation would track transactions per connection.
            return connId;
        }

        public static void CommitTransaction(int txId)
        {
            // Simplification: real implementation would look up the active transaction.
        }

        public static void RollbackTransaction(int txId)
        {
            // Simplification: real implementation would look up the active transaction.
        }

        private static string GetConnectionString()
        {
            var cs = Environment.GetEnvironmentVariable("DataStore__ConnectionString");
            return string.IsNullOrWhiteSpace(cs)
                ? "Server=localhost;Database=WorkflowDb;Trusted_Connection=True;TrustServerCertificate=True"
                : cs;
        }
    }
}
