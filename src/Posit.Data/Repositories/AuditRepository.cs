using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Posit.Data.Configuration;

namespace Posit.Data.Repositories;

/// <summary>
/// Append-only audit event log. Every phase transition, correction signal,
/// model call, and Z3 verification is recorded to posit_audit.events.
/// </summary>
public static class AuditRepository
{
    private static NpgsqlDataSource? _dataSource;

    public static void Initialize(NpgsqlDataSource? dataSource) => _dataSource = dataSource;

    private static NpgsqlConnection CreateConnection()
    {
        if (_dataSource is not null)
            return _dataSource.OpenConnectionAsync().GetAwaiter().GetResult();
        return new NpgsqlConnection(DbConnectionProvider.GetConnectionString());
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task LogEventAsync(
        string sessionId,
        string eventType,
        string? phaseId,
        string severity = "info",
        object? payload = null,
        CancellationToken ct = default)
    {
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(ct);

            var payloadJson = payload is not null
                ? JsonSerializer.Serialize(payload, JsonOptions)
                : null;

            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO posit_audit.events
                    (session_id, event_type, phase_id, severity, payload)
                VALUES (@sid, @type, @phase, @sev, @payload)",
                conn);

            cmd.Parameters.AddWithValue("sid", sessionId);
            cmd.Parameters.AddWithValue("type", eventType);
            cmd.Parameters.AddWithValue("phase", (object?)phaseId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("sev", severity);
            cmd.Parameters.AddWithValue("payload", (object?)payloadJson ?? DBNull.Value).NpgsqlDbType = NpgsqlDbType.Jsonb;

            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Posit] Audit logging failed (ignored): {ex.Message}");
        }
    }

    public static async Task<List<(string EventType, string? PhaseId, string Severity, string? Payload, DateTimeOffset CreatedAt)>> GetEventsAsync(
        string sessionId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(DbConnectionProvider.GetConnectionString());
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "SELECT event_type, phase_id, severity, payload::text, created_at FROM posit_audit.events WHERE session_id = @sid ORDER BY created_at",
            conn);
        cmd.Parameters.AddWithValue("sid", sessionId);

        var results = new List<(string, string?, string, string?, DateTimeOffset)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add((
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetDateTime(4)));
        }

        return results;
    }
}