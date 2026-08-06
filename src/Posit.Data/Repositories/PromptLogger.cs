using Npgsql;
using Posit.Data.Configuration;

namespace Posit.Data.Repositories;

/// <summary>
/// Captures every prompt→response pair to posit_qa.prompts_log.
/// This is the data harvest — every model call is a training sample.
/// Never throws — logging must not break the pipeline.
/// </summary>
public static class PromptLogger
{
    public static async Task LogPromptAsync(
        string sessionId,
        string phaseId,
        int phaseAttempt,
        string? moduleName,
        string attemptKind,
        string? modelProvider,
        string? modelId,
        string? systemPrompt,
        string? userPrompt,
        string? responseText,
        int? inputTokens,
        int? outputTokens,
        decimal? costUsd,
        long? latencyMs,
        string? parseStatus,
        string? parseError,
        CancellationToken ct = default)
    {
        try
        {
            await using var conn = new NpgsqlConnection(DbConnectionProvider.GetConnectionString());
            await conn.OpenAsync(ct);

            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO posit_qa.prompts_log
                    (session_id, phase_id, phase_attempt, module_name,
                     attempt_kind, model_provider, model_id,
                     system_prompt, user_prompt, response_text,
                     input_tokens, output_tokens, cost_usd, latency_ms,
                     parse_status, parse_error)
                VALUES (@sid, @phase, @attempt, @mod,
                        @kind, @provider, @modelId,
                        @sysPrompt, @userPrompt, @response,
                        @inTokens, @outTokens, @cost, @latency,
                        @parseStatus, @parseErr)",
                conn);

            cmd.Parameters.AddWithValue("sid", sessionId);
            cmd.Parameters.AddWithValue("phase", phaseId);
            cmd.Parameters.AddWithValue("attempt", phaseAttempt);
            cmd.Parameters.AddWithValue("mod", (object?)moduleName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("kind", attemptKind);
            cmd.Parameters.AddWithValue("provider", (object?)modelProvider ?? DBNull.Value);
            cmd.Parameters.AddWithValue("modelId", (object?)modelId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("sysPrompt", (object?)systemPrompt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("userPrompt", (object?)userPrompt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("response", (object?)responseText ?? DBNull.Value);
            cmd.Parameters.AddWithValue("inTokens", (object?)inputTokens ?? DBNull.Value);
            cmd.Parameters.AddWithValue("outTokens", (object?)outputTokens ?? DBNull.Value);
            cmd.Parameters.AddWithValue("cost", (object?)costUsd ?? DBNull.Value);
            cmd.Parameters.AddWithValue("latency", (object?)latencyMs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("parseStatus", (object?)parseStatus ?? DBNull.Value);
            cmd.Parameters.AddWithValue("parseErr", (object?)parseError ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Posit] Prompt logging failed (ignored): {ex.Message}");
        }
    }

    /// <summary>
    /// Log a Dafny verification result to posit_qa.dafny_results.
    /// Captures the Dafny source, Z3 output, and translated C# for every
    /// verification attempt (both skeleton and body phases).
    /// </summary>
    public static async Task LogDafnyResultAsync(
        string sessionId,
        string phaseId,
        string moduleName,
        string dafnySource,
        bool isVerified,
        string? verificationOutput,
        string? translatedCsharp,
        string? contractSummary,
        int attemptNumber = 1,
        CancellationToken ct = default)
    {
        try
        {
            await using var conn = new NpgsqlConnection(DbConnectionProvider.GetConnectionString());
            await conn.OpenAsync(ct);

            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO posit_qa.dafny_results
                    (session_id, phase_id, module_name, dafny_source,
                     is_verified, verification_output, translated_csharp,
                     contract_summary, attempt_number)
                VALUES (@sid, @phase, @mod, @source,
                        @verified, @vOutput, @cs,
                        @summary, @attempt)",
                conn);

            cmd.Parameters.AddWithValue("sid", sessionId);
            cmd.Parameters.AddWithValue("phase", phaseId);
            cmd.Parameters.AddWithValue("mod", moduleName);
            cmd.Parameters.AddWithValue("source", dafnySource);
            cmd.Parameters.AddWithValue("verified", isVerified);
            cmd.Parameters.AddWithValue("vOutput", (object?)verificationOutput ?? DBNull.Value);
            cmd.Parameters.AddWithValue("cs", (object?)translatedCsharp ?? DBNull.Value);
            cmd.Parameters.AddWithValue("summary", (object?)contractSummary ?? DBNull.Value);
            cmd.Parameters.AddWithValue("attempt", attemptNumber);

            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Posit] Dafny result logging failed (ignored): {ex.Message}");
        }
    }
}