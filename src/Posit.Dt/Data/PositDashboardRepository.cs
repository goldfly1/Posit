using System.Text.Json;
using Npgsql;
using Posit.Contracts.Serialization;
using Posit.Core.State;
using static Posit.Contracts.Serialization.PositJson;

namespace Posit.Dt.Data;

/// <summary>
/// Read-only dashboard repository over posit_state.sessions.
/// Deserializes the full SessionState JSON to project session fields.
/// </summary>
public sealed class PositDashboardRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private static readonly JsonSerializerOptions JsonOptions = Options;

    public PositDashboardRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<List<PositSessionSummary>> GetSessionsAsync(CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT session_id, state_json FROM posit_state.sessions ORDER BY saved_at DESC LIMIT 500",
            conn);

        var results = new List<PositSessionSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var sessionId = reader.GetString(0);
            var json = reader.GetString(1);
            var state = DeserializeOrDefault(json);
            results.Add(ProjectSummary(sessionId, state));
        }
        return results;
    }

    public async Task<PositSessionSummary?> GetSessionSummaryAsync(string sessionId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT state_json FROM posit_state.sessions WHERE session_id = @sid",
            conn);
        cmd.Parameters.AddWithValue("sid", sessionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        var json = reader.GetString(0);
        var state = DeserializeOrDefault(json);
        return ProjectSummary(sessionId, state);
    }

    private static SessionState? DeserializeOrDefault(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SessionState>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Posit.Dt] Failed to deserialize session state: {ex.Message}");
            return null;
        }
    }

    private static PositSessionSummary ProjectSummary(string sessionId, SessionState? state)
    {
        if (state is null)
        {
            return new PositSessionSummary
            {
                SessionId = sessionId,
                Status = "Unknown",
                StartedAt = DateTime.MinValue
            };
        }

        return new PositSessionSummary
        {
            SessionId = sessionId,
            Status = state.Status.ToString(),
            CurrentPhaseId = state.CurrentPhaseId?.Value,
            CurrentPhaseStatus = state.CurrentPhaseStatus?.ToString(),
            CurrentAttempt = state.CurrentAttempt,
            CompletedPhases = state.CompletedPhases.Select(p => p.Value).ToArray(),
            InputTokens = state.RunningCosts.InputTokens,
            OutputTokens = state.RunningCosts.OutputTokens,
            CostUsd = state.RunningCosts.AmountUsd,
            StartedAt = state.StartedAt.LocalDateTime,
            LastAdvancedAt = state.LastAdvancedAt?.LocalDateTime,
            Description = state.InitialRequest?.Prompt
        };
    }
}
