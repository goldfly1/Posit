using System.Text.Json;
using Npgsql;
using Posit.Contracts.Artifacts;
using Posit.Contracts.Serialization;
using static Posit.Contracts.Serialization.PositJson;

namespace Posit.Dt.Data;

/// <summary>
/// DTO for trial data — test results, generated code, components, QA analysis.
/// </summary>
public sealed class TrialData
{
    public TestCaseResultDto[] TestResults { get; set; } = [];
    public string WireCs { get; set; } = "";
    public ComponentDto[] Components { get; set; } = [];
    public string QaAnalysis { get; set; } = "";
}

public sealed record TestCaseResultDto(string Id, string Name, bool Matches, string Output, string Expected);
public sealed record ComponentDto(string Name, string? PatternName, string Classification, int Connections, bool IsVerified);

/// <summary>
/// Reads trial artifacts from posit_artifacts.artifacts for a session.
/// Extracts Wire.cs, test results, architecture contract components, and QA data.
/// </summary>
public sealed class PositTrialRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private static readonly JsonSerializerOptions JsonOpts = Options;

    public PositTrialRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<TrialData> GetTrialDataAsync(string sessionId, CancellationToken ct = default)
    {
        var data = new TrialData();
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // Get architecture contract for components
        var contract = await GetArtifactAsync(conn, sessionId, "architecture", ct);
        if (contract != null)
        {
            try
            {
                var arch = JsonSerializer.Deserialize<ArchitectureContract>(contract, JsonOpts);
                if (arch != null)
                    data.Components = arch.Components.Select(c => new ComponentDto(
                        c.Name, c.PatternName, c.Classification.ToString(), c.Connections.Length, c.IsVerified)).ToArray();
            }
            catch { }
        }

        // Get source code bundle for Wire.cs
        var sourceCode = await GetArtifactAsync(conn, sessionId, "csharp-implementation", ct);
        if (sourceCode != null)
        {
            try
            {
                var bundle = JsonSerializer.Deserialize<SourceCodeBundle>(sourceCode, JsonOpts);
                if (bundle != null)
                {
                    var wireFile = bundle.Files.FirstOrDefault(f => f.Path.Contains("Wire.cs"));
                    if (wireFile != null)
                        data.WireCs = wireFile.Content;
                }
            }
            catch { }
        }

        // Get QA test suite for test results and analysis
        var qaData = await GetArtifactAsync(conn, sessionId, "qa", ct);
        if (qaData != null)
        {
            try
            {
                var suite = JsonSerializer.Deserialize<TestSuite>(qaData, JsonOpts);
                if (suite != null)
                {
                    data.QaAnalysis = suite.Summary;
                    // Test results would come from the bot harness — stored separately
                }
            }
            catch { }
        }

        // Get bot harness test results from posit_audit.events
        data.TestResults = await GetTestResultsAsync(conn, sessionId, ct);

        return data;
    }

    private static async Task<string?> GetArtifactAsync(NpgsqlConnection conn, string sessionId, string phase, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT payload_json FROM posit_artifacts.artifacts WHERE session_id = @sid AND source_phase = @phase ORDER BY produced_at DESC LIMIT 1",
            conn);
        cmd.Parameters.AddWithValue("sid", sessionId);
        cmd.Parameters.AddWithValue("phase", phase);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var raw = reader.GetValue(0);
        return raw switch
        {
            string s => s,
            byte[] b => System.Text.Encoding.UTF8.GetString(b),
            _ => raw?.ToString()
        };
    }

    private static async Task<TestCaseResultDto[]> GetTestResultsAsync(NpgsqlConnection conn, string sessionId, CancellationToken ct)
    {
        // Bot harness results are stored in posit_audit.events as JSON
        await using var cmd = new NpgsqlCommand(
            "SELECT payload FROM posit_audit.events WHERE session_id = @sid AND event_type = 'harness_test' ORDER BY created_at DESC LIMIT 1",
            conn);
        cmd.Parameters.AddWithValue("sid", sessionId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return [];
        var raw = reader.GetValue(0);
        var json = raw switch
        {
            string s => s,
            byte[] b => System.Text.Encoding.UTF8.GetString(b),
            _ => raw?.ToString() ?? "[]"
        };
        try { return JsonSerializer.Deserialize<TestCaseResultDto[]>(json, JsonOpts) ?? []; }
        catch { return []; }
    }
}