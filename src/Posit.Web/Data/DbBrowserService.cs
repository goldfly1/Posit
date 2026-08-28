using Npgsql;
using System.Text.Json;

namespace Posit.Web.Data;

/// <summary>
/// Real database browser service — reads table schemas, rows, and individual records
/// from the Posit Postgres database. No raw SQL accepted from clients.
/// </summary>
public class DbBrowserService
{
    private readonly string _connStr;

    public DbBrowserService(string connStr)
    {
        _connStr = connStr;
    }

    /// <summary>
    /// Returns the known schema tree: schemas → tables → columns.
    /// </summary>
    public async Task<List<SchemaInfo>> GetSchemaAsync()
    {
        var schemas = new List<SchemaInfo>
        {
            new("posit_state", new List<TableInfo>
            {
                new("posit_state", "sessions", new List<ColumnInfo>
                {
                    new("session_id", "text", false),
                    new("state_json", "jsonb", false),
                    new("saved_at", "timestamp with time zone", false)
                }),
                new("posit_state", "session_contexts", new List<ColumnInfo>
                {
                    new("session_id", "text", false),
                    new("context_json", "jsonb", false),
                    new("saved_at", "timestamp with time zone", false)
                })
            }),
            new("posit_artifacts", new List<TableInfo>
            {
                new("posit_artifacts", "artifacts", new List<ColumnInfo>
                {
                    new("id", "text", false),
                    new("session_id", "text", false),
                    new("source_phase", "text", false),
                    new("schema_version", "text", false),
                    new("kind", "text", false),
                    new("payload_json", "jsonb", false),
                    new("produced_at", "timestamp with time zone", false),
                    new("sealed_at", "timestamp with time zone", true),
                    new("checksum", "text", true)
                }),
                new("posit_artifacts", "artifact_lineage", new List<ColumnInfo>
                {
                    new("artifact_id", "text", false),
                    new("parent_artifact_id", "text", false)
                })
            })
        };

        return await Task.FromResult(schemas);
    }

    /// <summary>
    /// Returns rows from a table as a list of dictionaries. Safe: only allows
    /// the known tables, parameterized by page/pageSize.
    /// </summary>
    public async Task<TableData> GetTableRowsAsync(string schema, string table, int page = 1, int pageSize = 50, string? filter = null)
    {
        // Validate table name against whitelist
        var validTables = new HashSet<string>
        {
            "posit_state.sessions",
            "posit_state.session_contexts",
            "posit_artifacts.artifacts",
            "posit_artifacts.artifact_lineage"
        };
        var full = $"{schema}.{table}";
        if (!validTables.Contains(full))
            throw new ArgumentException($"Unknown table: {full}");

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync();

        // Get total count
        var countSql = $"SELECT COUNT(*) FROM {schema}.{table}";
        var whereClause = BuildWhereClause(schema, table, filter ?? "");
        var hasFilter = !string.IsNullOrEmpty(filter) && !string.IsNullOrEmpty(whereClause);
        if (hasFilter) countSql += whereClause;
        await using var countCmd = new NpgsqlCommand(countSql, conn);
        if (hasFilter) countCmd.Parameters.AddWithValue("@pfilter", NpgsqlTypes.NpgsqlDbType.Text, $"%{filter}%");
        var totalRows = (long)(await countCmd.ExecuteScalarAsync() ?? 0);

        // Get rows for this page
        var offset = (page - 1) * pageSize;
        var dataSql = $"SELECT * FROM {schema}.{table}";
        if (hasFilter) dataSql += whereClause;
        var orderBy = GetDefaultOrderBy(schema, table);
        dataSql += $" ORDER BY {orderBy} DESC LIMIT @limit OFFSET @offset";

        await using var dataCmd = new NpgsqlCommand(dataSql, conn);
        dataCmd.Parameters.AddWithValue("limit", pageSize);
        dataCmd.Parameters.AddWithValue("offset", offset);
        if (hasFilter) dataCmd.Parameters.AddWithValue("@pfilter", NpgsqlTypes.NpgsqlDbType.Text, $"%{filter}%");

        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await dataCmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var colName = reader.GetName(i);
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                // Convert JSON types to string for JSON serialization
                if (value is System.Text.Json.JsonElement je)
                {
                    value = je.GetRawText();
                }
                row[colName] = value;
            }
            rows.Add(row);
        }

        return new TableData(
            Schema: schema,
            Table: table,
            TotalRows: totalRows,
            Page: page,
            PageSize: pageSize,
            Rows: rows
        );
    }

    private static string GetDefaultOrderBy(string schema, string table)
    {
        return (schema, table) switch
        {
            ("posit_state", "sessions") => "saved_at",
            ("posit_state", "session_contexts") => "saved_at",
            ("posit_artifacts", "artifacts") => "produced_at",
            ("posit_artifacts", "artifact_lineage") => "artifact_id",
            _ => "1"
        };
    }

    private static string BuildWhereClause(string schema, string table, string filter)
    {
        return (schema, table) switch
        {
            ("posit_state", "sessions") => " WHERE session_id ILIKE @pfilter OR state_json::text ILIKE @pfilter",
            ("posit_artifacts", "artifacts") => " WHERE id ILIKE @pfilter OR session_id ILIKE @pfilter OR kind ILIKE @pfilter OR source_phase ILIKE @pfilter",
            ("posit_artifacts", "artifact_lineage") => " WHERE artifact_id ILIKE @pfilter OR parent_artifact_id ILIKE @pfilter",
            ("posit_state", "session_contexts") => " WHERE session_id ILIKE @pfilter OR context_json::text ILIKE @pfilter",
            _ => ""
        };
    }
}

public record SchemaInfo(string Name, List<TableInfo> Tables);
public record TableInfo(string Schema, string Name, List<ColumnInfo> Columns);
public record ColumnInfo(string Name, string DataType, bool Nullable);
public record TableData(string Schema, string Table, long TotalRows, int Page, int PageSize, List<Dictionary<string, object?>> Rows);