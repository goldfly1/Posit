using Npgsql;

namespace Posit.Dt.Data;

/// <summary>
/// Read-only prompt repository over posit_qa.prompts_log.
/// </summary>
public sealed class PositPromptRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PositPromptRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<List<PositPromptEntry>> GetPromptsAsync(string? sessionId = null, string? phaseId = null, int limit = 100, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        var sql = @"SELECT id, session_id, phase_id, phase_attempt, module_name,
                           attempt_kind, model_provider, model_id,
                           length(system_prompt), length(user_prompt), length(response_text),
                           input_tokens, output_tokens, cost_usd, latency_ms,
                           parse_status, parse_error, created_at
                    FROM posit_qa.prompts_log";
        var conditions = new List<string>();
        if (!string.IsNullOrEmpty(sessionId))
            conditions.Add("session_id = @sessionId");
        if (!string.IsNullOrEmpty(phaseId))
            conditions.Add("phase_id = @phaseId");
        if (conditions.Count > 0)
            sql += " WHERE " + string.Join(" AND ", conditions);
        sql += " ORDER BY created_at DESC LIMIT @limit";
        cmd.CommandText = sql;
        if (!string.IsNullOrEmpty(sessionId))
            cmd.Parameters.AddWithValue("sessionId", sessionId);
        if (!string.IsNullOrEmpty(phaseId))
            cmd.Parameters.AddWithValue("phaseId", phaseId);
        cmd.Parameters.AddWithValue("limit", limit);

        var results = new List<PositPromptEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadPrompt(reader, detail: false));
        }
        return results;
    }

    public async Task<PositPromptEntry?> GetPromptDetailAsync(long id, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, session_id, phase_id, phase_attempt, module_name,
                   attempt_kind, model_provider, model_id,
                   system_prompt, user_prompt, response_text,
                   input_tokens, output_tokens, cost_usd, latency_ms,
                   parse_status, parse_error, created_at
            FROM posit_qa.prompts_log WHERE id = @id";
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return ReadPrompt(reader, detail: true);
    }

    private static PositPromptEntry ReadPrompt(NpgsqlDataReader reader, bool detail)
    {
        return new PositPromptEntry
        {
            Id = reader.GetInt64(0),
            SessionId = reader.GetString(1),
            PhaseId = reader.GetString(2),
            PhaseAttempt = reader.GetInt32(3),
            ModuleName = reader.IsDBNull(4) ? null : reader.GetString(4),
            AttemptKind = reader.GetString(5),
            ModelProvider = reader.IsDBNull(6) ? null : reader.GetString(6),
            ModelId = reader.IsDBNull(7) ? null : reader.GetString(7),
            SystemPromptLen = detail ? (reader.IsDBNull(8) ? 0 : reader.GetString(8)?.Length ?? 0) : (reader.IsDBNull(8) ? 0 : reader.GetInt32(8)),
            UserPromptLen = detail ? (reader.IsDBNull(9) ? 0 : reader.GetString(9)?.Length ?? 0) : (reader.IsDBNull(9) ? 0 : reader.GetInt32(9)),
            ResponseLen = detail ? (reader.IsDBNull(10) ? 0 : reader.GetString(10)?.Length ?? 0) : (reader.IsDBNull(10) ? 0 : reader.GetInt32(10)),
            InputTokens = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
            OutputTokens = reader.IsDBNull(12) ? 0 : reader.GetInt32(12),
            CostUsd = reader.IsDBNull(13) ? 0m : reader.GetDecimal(13),
            LatencyMs = reader.IsDBNull(14) ? 0 : reader.GetInt64(14),
            ParseStatus = reader.IsDBNull(15) ? null : reader.GetString(15),
            ParseError = reader.IsDBNull(16) ? null : reader.GetString(16),
            CreatedAt = reader.IsDBNull(17) ? DateTime.MinValue : DateTime.SpecifyKind(reader.GetDateTime(17), DateTimeKind.Utc).ToLocalTime(),
            SystemPrompt = detail ? (reader.IsDBNull(8) ? null : reader.GetString(8)) : null,
            UserPrompt = detail ? (reader.IsDBNull(9) ? null : reader.GetString(9)) : null,
            ResponseText = detail ? (reader.IsDBNull(10) ? null : reader.GetString(10)) : null
        };
    }
}
