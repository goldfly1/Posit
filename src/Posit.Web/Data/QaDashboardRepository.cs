using Npgsql;
using System.Text.Json;
using Posit.Data.Configuration;

namespace Posit.Web.Data;

/// <summary>
/// Reads Posit sessions and artifacts from Postgres for the QA dashboard.
/// </summary>
public sealed class QaDashboardRepository
{
    private static readonly string ConnString = DbConnectionProvider.GetConnectionString();

    public async Task<List<SessionSummary>> GetSessionsAsync()
    {
        var sessions = new List<SessionSummary>();
        await using var conn = new NpgsqlConnection(ConnString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT session_id, state_json->>'status' as status,
                   state_json->>'currentPhaseId' as phase,
                   state_json->>'initialRequest' as request,
                   saved_at
            FROM posit_state.sessions
            ORDER BY saved_at DESC";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var requestId = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var request = "";
            try
            {
                using var doc = JsonDocument.Parse(requestId);
                request = doc.RootElement.TryGetProperty("prompt", out var p) ? p.GetString() ?? "" : requestId;
            }
            catch { request = requestId; }

            sessions.Add(new SessionSummary
            {
                SessionId = reader.GetString(0),
                Status = reader.IsDBNull(1) ? "unknown" : reader.GetString(1),
                Phase = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Request = request,
                SavedAt = reader.GetDateTime(4)
            });
        }
        return sessions;
    }

    public async Task<List<ArtifactSummary>> GetArtifactsAsync(string sessionId)
    {
        var artifacts = new List<ArtifactSummary>();
        await using var conn = new NpgsqlConnection(ConnString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, source_phase, kind, payload_json::text, produced_at
            FROM posit_artifacts.artifacts
            WHERE session_id = @sid
            ORDER BY produced_at";

        cmd.Parameters.AddWithValue("sid", sessionId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            artifacts.Add(new ArtifactSummary
            {
                Id = reader.GetString(0),
                SourcePhase = reader.GetString(1),
                Kind = reader.GetString(2),
                PayloadJson = reader.GetString(3),
                ProducedAt = reader.GetDateTime(4)
            });
        }
        return artifacts;
    }

    public async Task<TestSuiteData?> GetTestSuiteAsync(string sessionId)
    {
        var artifacts = await GetArtifactsAsync(sessionId);
        var testArtifact = artifacts.FirstOrDefault(a => a.Kind == "TestSuite");
        if (testArtifact == null) return null;

        try
        {
            using var doc = JsonDocument.Parse(testArtifact.PayloadJson);
            var root = doc.RootElement;

            var testFiles = new List<TestFileData>();
            if (root.TryGetProperty("testFiles", out var filesEl))
            {
                foreach (var f in filesEl.EnumerateArray())
                {
                    testFiles.Add(new TestFileData
                    {
                        Path = f.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "",
                        Content = f.TryGetProperty("content", out var c) ? c.GetString() ?? "" : ""
                    });
                }
            }

            var expectedOutputs = new Dictionary<string, string>();
            if (root.TryGetProperty("expectedOutputs", out var eoEl))
            {
                foreach (var prop in eoEl.EnumerateObject())
                    expectedOutputs[prop.Name] = prop.Value.GetString() ?? "";
            }

            var expectedExitCodes = new Dictionary<string, int>();
            if (root.TryGetProperty("expectedExitCodes", out var eeEl))
            {
                foreach (var prop in eeEl.EnumerateObject())
                    expectedExitCodes[prop.Name] = prop.Value.GetInt32();
            }

            return new TestSuiteData
            {
                Summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "",
                TestFiles = testFiles,
                ExpectedOutputs = expectedOutputs,
                ExpectedExitCodes = expectedExitCodes,
                ProducedAt = testArtifact.ProducedAt
            };
        }
        catch { return null; }
    }
}

public sealed class SessionSummary
{
    public string SessionId { get; set; } = "";
    public string Status { get; set; } = "";
    public string Phase { get; set; } = "";
    public string Request { get; set; } = "";
    public DateTime SavedAt { get; set; }

    public string ShortId => SessionId.Length > 8 ? SessionId[..8] : SessionId;
    public string ShortRequest => Request.Length > 60 ? Request[..60] + "…" : Request;
}

public sealed class ArtifactSummary
{
    public string Id { get; set; } = "";
    public string SourcePhase { get; set; } = "";
    public string Kind { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public DateTime ProducedAt { get; set; }
}

public sealed class TestSuiteData
{
    public string Summary { get; set; } = "";
    public List<TestFileData> TestFiles { get; set; } = [];
    public Dictionary<string, string> ExpectedOutputs { get; set; } = [];
    public Dictionary<string, int> ExpectedExitCodes { get; set; } = [];
    public DateTime ProducedAt { get; set; }
}

public sealed class TestFileData
{
    public string Path { get; set; } = "";
    public string Content { get; set; } = "";
}