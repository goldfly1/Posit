using System.Text.Json;
using Npgsql;
using Posit.Contracts.Core;
using Posit.Contracts.Serialization;
using Posit.Core.State;
using static Posit.Contracts.Serialization.PositJson;

namespace Posit.Dt.Data;

/// <summary>
/// Lightweight DTO for a session row on the dashboard.
/// Projected from posit_state.sessions.state_json.
/// </summary>
public sealed class PositSessionSummary
{
    public string SessionId { get; set; } = "";
    public string Status { get; set; } = "";
    public string? CurrentPhaseId { get; set; }
    public string? CurrentPhaseStatus { get; set; }
    public int CurrentAttempt { get; set; }
    public string[] CompletedPhases { get; set; } = [];
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? LastAdvancedAt { get; set; }
    public string? Description { get; set; }
}
