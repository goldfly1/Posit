using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Posit.Data.Configuration;
using Posit.Core.State;

namespace Posit.Data.Repositories;

/// <summary>
/// Persists session state to posit_state.sessions so sessions survive
/// process exit and can be resumed with --session=<id>.
/// </summary>
public sealed class StateStore
{
    private readonly NpgsqlDataSource _dataSource;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public StateStore(NpgsqlDataSource? dataSource = null)
    {
        _dataSource = dataSource ?? DbConnectionProvider.CreateDataSource();
    }

    public async Task SaveSessionAsync(SessionId sessionId, SessionState state, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO posit_state.sessions (session_id, state_json, saved_at)
            VALUES (@sid, @json, now())
            ON CONFLICT (session_id) DO UPDATE SET state_json = EXCLUDED.state_json, saved_at = now()",
            conn);

        cmd.Parameters.AddWithValue("sid", sessionId.Value);
        cmd.Parameters.AddWithValue("json", json).NpgsqlDbType = NpgsqlDbType.Jsonb;

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<SessionState?> LoadSessionAsync(SessionId sessionId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT state_json FROM posit_state.sessions WHERE session_id = @sid",
            conn);
        cmd.Parameters.AddWithValue("sid", sessionId.Value);

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            var json = reader.GetString(0);
            return JsonSerializer.Deserialize<SessionState>(json, JsonOptions);
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P01")
        {
            return null;
        }
    }

    public async Task<SessionState[]> ListAllSessionsAsync(CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT state_json FROM posit_state.sessions ORDER BY saved_at DESC",
            conn);

        try
        {
            var results = new List<SessionState>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var json = reader.GetString(0);
                var state = JsonSerializer.Deserialize<SessionState>(json, JsonOptions);
                if (state is not null)
                    results.Add(state);
            }
            return [.. results];
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P01")
        {
            return [];
        }
    }
}