using Npgsql;

namespace Posit.Dt.Data;

/// <summary>
/// Read-only implementation trace repository.
/// Tries posit_qa.dafny_results first; if the table is missing, falls back to
/// prompts_log rows from the Dafny and C# implementation phases.
/// </summary>
public sealed class PositImplTraceRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PositImplTraceRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<List<PositImplTraceEntry>> GetImplTracesAsync(string? sessionId = null, CancellationToken ct = default)
    {
        var results = new List<PositImplTraceEntry>();

        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            var sql = @"SELECT attempt_number, phase_id, module_name, is_verified,
                               verification_output, length(dafny_source), created_at
                        FROM posit_qa.dafny_results";
            if (!string.IsNullOrEmpty(sessionId))
            {
                sql += " WHERE session_id = @sessionId";
                cmd.Parameters.AddWithValue("sessionId", sessionId);
            }
            sql += " ORDER BY attempt_number, created_at";
            cmd.CommandText = sql;

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var dafnyLen = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
                results.Add(new PositImplTraceEntry
                {
                    PhaseAttempt = reader.GetInt32(0),
                    PhaseId = reader.GetString(1),
                    ModuleName = reader.GetString(2),
                    IsVerified = reader.GetBoolean(3),
                    VerificationOutput = reader.IsDBNull(4) ? null : reader.GetString(4),
                    PromptLength = dafnyLen,
                    ResponseLength = 0,
                    CompilerErrors = null,
                    CreatedAt = reader.IsDBNull(5) ? DateTime.MinValue : DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc).ToLocalTime(),
                    IsDafny = true
                });
            }
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P01")
        {
            Console.WriteLine("[Posit.Dt] posit_qa.dafny_results missing; falling back to prompts_log.");
        }

        // Fallback: enrich with prompts_log rows from implementation-ish phases if no Dafny rows.
        if (results.Count == 0)
        {
            try
            {
                await using var conn = await _dataSource.OpenConnectionAsync(ct);
                await using var cmd = conn.CreateCommand();
                var sql = @"SELECT phase_attempt, phase_id, module_name, length(user_prompt), length(response_text),
                                   parse_status, parse_error, created_at
                            FROM posit_qa.prompts_log
                            WHERE phase_id IN ('dafny-contracts','dafny-implementation','csharp-implementation')";
                if (!string.IsNullOrEmpty(sessionId))
                {
                    sql += " AND session_id = @sessionId";
                    cmd.Parameters.AddWithValue("sessionId", sessionId);
                }
                sql += " ORDER BY phase_attempt, created_at";
                cmd.CommandText = sql;

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    results.Add(new PositImplTraceEntry
                    {
                        PhaseAttempt = reader.GetInt32(0),
                        PhaseId = reader.GetString(1),
                        ModuleName = reader.IsDBNull(2) ? null : reader.GetString(2),
                        IsVerified = reader.IsDBNull(5) ? false : reader.GetString(5) == "success",
                        VerificationOutput = reader.IsDBNull(6) ? null : reader.GetString(6),
                        PromptLength = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                        ResponseLength = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                        CompilerErrors = null,
                        CreatedAt = reader.IsDBNull(7) ? DateTime.MinValue : DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc).ToLocalTime(),
                        IsDafny = reader.GetString(1).Contains("dafny", StringComparison.OrdinalIgnoreCase)
                    });
                }
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P01")
            {
                Console.WriteLine("[Posit.Dt] posit_qa.prompts_log missing; no impl trace data available.");
            }
        }

        return results;
    }
}
