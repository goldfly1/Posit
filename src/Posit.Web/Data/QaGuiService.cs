using Npgsql;
using Posit.Contracts.Artifacts;
using Posit.Contracts.Core;
using Posit.Contracts.Serialization;
using Posit.Data.Repositories;
using Posit.Tools;
using System.Text;

namespace Posit.Web.Data;

/// <summary>
/// Service for the QA GUI. Loads trial data from Postgres, runs test cases
/// via the BotHarness (Docker clean room), and provides a generic form model
/// that works across all trials.
/// </summary>
public sealed class QaGuiService
{
    private readonly ArtifactRepository _repo;
    private readonly string _connStr;

    public QaGuiService(ArtifactRepository? repo = null)
    {
        _repo = repo ?? new ArtifactRepository();
        _connStr = "Host=localhost;Port=5434;Database=shepherd;Username=shepherd;Password=shepherd";
    }

    // ── Trial listing ──────────────────────────────────────────────

    public async Task<List<TrialSummary>> ListTrialsAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT s.session_id, s.state_json->>'status' as status, s.saved_at,
                   (SELECT COUNT(*) FROM posit_artifacts.artifacts a WHERE a.session_id = s.session_id) as artifact_count,
                   (SELECT string_agg(DISTINCT a.kind, ', ' ORDER BY a.kind)
                    FROM posit_artifacts.artifacts a WHERE a.session_id = s.session_id) as kinds
            FROM posit_state.sessions s
            WHERE EXISTS (SELECT 1 FROM posit_artifacts.artifacts a
                          WHERE a.session_id = s.session_id AND a.kind = 'ArchitectureContract')
            ORDER BY s.saved_at DESC
            """, conn);
        var results = new List<TrialSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new TrialSummary(
                reader.GetString(0),
                reader.IsDBNull(1) ? "unknown" : reader.GetString(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                (int)reader.GetInt64(3),
                reader.IsDBNull(4) ? "" : reader.GetString(4)
            ));
        }
        return results;
    }

    // ── Trial detail ───────────────────────────────────────────────

    public async Task<TrialDetail?> LoadTrialAsync(string sessionId, CancellationToken ct = default)
    {
        var sid = new SessionId(sessionId);
        var artifacts = await _repo.ListBySessionAsync(sid, ct);

        var contractBundle = artifacts.FirstOrDefault(a => a.Kind == ArtifactKind.ArchitectureContract);
        if (contractBundle == null) return null;
        var contract = Deserialize<ArchitectureContract>(contractBundle.PayloadJson);
        if (contract == null) return null;

        var sourceBundle = artifacts.FirstOrDefault(a => a.Kind == ArtifactKind.SourceCodeBundle);
        var sourceCode = sourceBundle != null ? Deserialize<SourceCodeBundle>(sourceBundle.PayloadJson) : null;

        var testBundle = artifacts.FirstOrDefault(a => a.Kind == ArtifactKind.TestSuite);
        var testSuite = testBundle != null ? Deserialize<TestSuite>(testBundle.PayloadJson) : null;

        // Build pages from all components with method signatures
        var pages = new List<GuiPage>();
        foreach (var comp in contract.Components)
        {
            if (comp.MethodSignatures.Length == 0) continue;
            foreach (var method in comp.MethodSignatures)
            {
                var fields = new List<GuiField>();
                foreach (var p in method.Params)
                {
                    fields.Add(new GuiField(
                        p.Name, p.Name, MapType(p.Type), fields.Count, false
                    ));
                }
                pages.Add(new GuiPage(
                    $"{comp.Name}.{method.Name}",
                    comp.Name,
                    method.Name,
                    method.ReturnType ?? "void",
                    [.. fields]
                ));
            }
        }

        // Build test cases from the CLI component
        var cliComp = contract.Components.FirstOrDefault(c => c.Connections.Length > 0)
                   ?? contract.Components.FirstOrDefault();
        var testCases = new List<GuiTestCase>();
        if (cliComp?.TestCases.Length > 0)
        {
            for (int i = 0; i < cliComp.TestCases.Length; i++)
            {
                var tc = cliComp.TestCases[i];
                var key = $"tc{i + 1}";
                var expectedOutput = testSuite?.ExpectedOutputs?.TryGetValue(key, out var eo) == true ? eo : "";
                var expectedExit = testSuite?.ExpectedExitCodes?.TryGetValue(key, out var ee) == true ? ee : 0;
                var inputData = testSuite?.TestFiles.Length > i
                    ? testSuite.TestFiles[i].Content
                    : tc.Description;
                testCases.Add(new GuiTestCase(
                    tc.Id, tc.Name, inputData, expectedOutput, expectedExit,
                    tc.ExpectedBehavior ?? tc.Description
                ));
            }
        }

        // Universal fields (generic — present on every trial)
        var universalFields = new[]
        {
            new GuiField("name", "Name", "string", 0, false),
            new GuiField("address", "Address", "string", 1, false),
            new GuiField("age", "Age", "int", 2, false),
            new GuiField("email", "Email", "string", 3, false),
            new GuiField("phone", "Phone", "string", 4, false),
            new GuiField("date", "Date", "date", 5, false),
            new GuiField("notes", "Notes", "string", 6, false),
        };

        return new TrialDetail(
            sessionId,
            contract.SystemContext ?? "",
            [.. pages],
            [.. testCases],
            universalFields,
            cliComp?.Name ?? "Program",
            string.Equals(cliComp?.EntryType, "stdin", StringComparison.OrdinalIgnoreCase),
            sourceCode != null,
            [.. (sourceCode?.Files ?? []).Select(f => new SourceFile(f.Path, f.Content))]
        );
    }

    // ── Run a single test case ──────────────────────────────────────

    public async Task<GuiRunResult> RunTestAsync(string sessionId, string input, string expectedOutput, int expectedExitCode, string expectedBehavior, string systemContext, bool isStdin, CancellationToken ct = default)
    {
        var sid = new SessionId(sessionId);
        var artifacts = await _repo.ListBySessionAsync(sid, ct);

        var contractBundle = artifacts.First(a => a.Kind == ArtifactKind.ArchitectureContract);
        var contract = Deserialize<ArchitectureContract>(contractBundle.PayloadJson)!;
        var sourceBundle = artifacts.First(a => a.Kind == ArtifactKind.SourceCodeBundle);
        var sourceCode = Deserialize<SourceCodeBundle>(sourceBundle.PayloadJson)!;

        var cliComp = contract.Components.FirstOrDefault(c => c.Connections.Length > 0)
                   ?? contract.Components.FirstOrDefault();
        var cliName = cliComp?.Name ?? "Program";

        // Materialize source into temp dir
        var tempDir = Path.Combine(Path.GetTempPath(), "posit-qa-gui", sessionId, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in sourceCode.Files)
        {
            if (!seenPaths.Add(file.Path)) continue;
            var fullPath = Path.Combine(tempDir, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, file.Content);
        }

        // Generate project files
        var projectNames = new List<string>();
        foreach (var comp in contract.Components)
        {
            var isExe = comp.Id == cliComp?.Id;
            var projName = comp.Name;
            var projDir = Path.Combine(tempDir, projName);
            Directory.CreateDirectory(projDir);
            var deps = comp.Connections
                .Where(c => c.ToComponent != comp.Name)
                .Select(c => c.ToComponent)
                .Distinct()
                .ToList();
            File.WriteAllText(Path.Combine(projDir, $"{projName}.csproj"),
                BotHarnessProjects.GenerateCsproj(projName, isExe, deps));
            projectNames.Add(projName);
        }

        File.WriteAllText(Path.Combine(tempDir, "PositGenerated.sln"),
            BotHarnessProjects.GenerateSln("PositGenerated", projectNames));
        File.WriteAllText(Path.Combine(tempDir, "Dockerfile.run"),
            BotHarnessDocker.GenerateDockerfileRun(cliName));

        // Prepare test input
        string cliArg = "";
        string? stdinInput = null;
        if (isStdin)
        {
            stdinInput = input;
        }
        else
        {
            var ext = input.StartsWith("[") || input.StartsWith("{") ? ".json"
                : (input.Contains(",") && input.Contains("\n") ? ".csv" : ".txt");
            File.WriteAllText(Path.Combine(tempDir, $"testdata_gui{ext}"), input);
            cliArg = $"testdata_gui{ext}";
        }

        // Build + run in Docker
        var buildResult = await BotHarnessDocker.BuildAsync("docker", tempDir, sessionId, ct);
        if (!buildResult.Success)
        {
            return new GuiRunResult(false, "", $"Docker build failed:\n{buildResult.Output}", -1,
                "Fail", "Build", "Build failed — source does not compile");
        }

        var runResult = await BotHarnessDocker.RunContainerAsync("docker", sessionId, cliName, cliArg, ct, stdinInput);

        // Judge
        var judge = new QaJudge();
        var run = new TestCaseRun(runResult.Output, "", runResult.ExitCode);
        var verdict = await judge.JudgeAsync(run, expectedOutput, expectedExitCode, expectedBehavior, systemContext, ct);

        return new GuiRunResult(
            runResult.Success, runResult.Output, "", runResult.ExitCode,
            verdict.Result.ToString(), verdict.Layer.ToString(), verdict.Reason
        );
    }

    // ── Search ──────────────────────────────────────────────────────

    public async Task<List<SearchHit>> SearchAsync(string query, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);

        var hits = new List<SearchHit>();

        // Search sessions
        await using (var cmd = new NpgsqlCommand("""
            SELECT session_id, state_json->>'status', saved_at
            FROM posit_state.sessions
            WHERE session_id ILIKE @q OR state_json::text ILIKE @q
            LIMIT 20
            """, conn))
        {
            cmd.Parameters.AddWithValue("@q", NpgsqlTypes.NpgsqlDbType.Text, $"%{query}%");
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                hits.Add(new SearchHit(reader.GetString(0), "session",
                    reader.IsDBNull(1) ? "" : reader.GetString(1),
                    reader.GetFieldValue<DateTimeOffset>(2).ToString("yyyy-MM-dd HH:mm")));
            }
        }

        // Search artifacts
        await using (var cmd = new NpgsqlCommand("""
            SELECT id, session_id, kind, source_phase, produced_at
            FROM posit_artifacts.artifacts
            WHERE id ILIKE @q OR session_id ILIKE @q OR kind ILIKE @q OR payload_json::text ILIKE @q
            LIMIT 20
            """, conn))
        {
            cmd.Parameters.AddWithValue("@q", NpgsqlTypes.NpgsqlDbType.Text, $"%{query}%");
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                hits.Add(new SearchHit(reader.GetString(0), reader.GetString(3),
                    reader.GetString(2),
                    reader.GetFieldValue<DateTimeOffset>(4).ToString("yyyy-MM-dd HH:mm")));
            }
        }

        return hits;
    }

    // ── DB browser ──────────────────────────────────────────────────

    public async Task<DbTableData> GetTableAsync(string schema, string table, int page, int pageSize, string? filter, CancellationToken ct = default)
    {
        var valid = new HashSet<string> {
            "posit_state.sessions", "posit_state.session_contexts",
            "posit_artifacts.artifacts", "posit_artifacts.artifact_lineage"
        };
        if (!valid.Contains($"{schema}.{table}"))
            throw new ArgumentException($"Unknown table: {schema}.{table}");

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);

        var where = BuildWhere(schema, table, filter);
        var hasFilter = !string.IsNullOrEmpty(filter) && !string.IsNullOrEmpty(where);

        // Count
        var countSql = $"SELECT COUNT(*) FROM {schema}.{table}";
        if (hasFilter) countSql += where;
        await using var countCmd = new NpgsqlCommand(countSql, conn);
        if (hasFilter) countCmd.Parameters.AddWithValue("@q", NpgsqlTypes.NpgsqlDbType.Text, $"%{filter}%");
        var total = (long)(await countCmd.ExecuteScalarAsync(ct) ?? 0);

        // Data
        var offset = (page - 1) * pageSize;
        var orderBy = (schema, table) switch
        {
            ("posit_state", "sessions") => "saved_at",
            ("posit_artifacts", "artifacts") => "produced_at",
            _ => "1"
        };
        var dataSql = $"SELECT * FROM {schema}.{table}";
        if (hasFilter) dataSql += where;
        dataSql += $" ORDER BY {orderBy} DESC LIMIT @limit OFFSET @offset";

        await using var dataCmd = new NpgsqlCommand(dataSql, conn);
        dataCmd.Parameters.AddWithValue("limit", pageSize);
        dataCmd.Parameters.AddWithValue("offset", offset);
        if (hasFilter) dataCmd.Parameters.AddWithValue("@q", NpgsqlTypes.NpgsqlDbType.Text, $"%{filter}%");

        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await dataCmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var val = reader.IsDBNull(i) ? null : reader.GetValue(i);
                if (val is System.Text.Json.JsonElement je) val = je.GetRawText();
                row[reader.GetName(i)] = val;
            }
            rows.Add(row);
        }

        return new DbTableData(schema, table, total, page, pageSize, [.. rows]);
    }

    private static string BuildWhere(string schema, string table, string? filter) => (schema, table) switch
    {
        ("posit_state", "sessions") => " WHERE session_id ILIKE @q OR state_json::text ILIKE @q",
        ("posit_artifacts", "artifacts") => " WHERE id ILIKE @q OR session_id ILIKE @q OR kind ILIKE @q OR source_phase ILIKE @q",
        ("posit_artifacts", "artifact_lineage") => " WHERE artifact_id ILIKE @q OR parent_artifact_id ILIKE @q",
        ("posit_state", "session_contexts") => " WHERE session_id ILIKE @q OR context_json::text ILIKE @q",
        _ => ""
    };

    private static string MapType(string dafnyType) => dafnyType switch
    {
        "string" => "string",
        "int" => "int",
        "double" or "real" => "double",
        "bool" => "bool",
        _ when dafnyType.StartsWith("seq<") => dafnyType.Trim(['s', 'e', 'q', '<', '>']) + "[]",
        _ => dafnyType
    };

    private static T? Deserialize<T>(byte[] payloadJson) where T : class
    {
        var json = System.Text.Encoding.UTF8.GetString(payloadJson);
        return System.Text.Json.JsonSerializer.Deserialize<T>(json, PositJson.Options);
    }
}

// ── DTOs ────────────────────────────────────────────────────────────

public sealed record TrialSummary(string SessionId, string Status, DateTimeOffset SavedAt, int ArtifactCount, string Kinds);
public sealed record TrialDetail(string SessionId, string SystemContext, GuiPage[] Pages, GuiTestCase[] TestCases, GuiField[] UniversalFields, string CliComponentName, bool IsStdinEntry, bool HasSourceCode, SourceFile[] SourceFiles);
public sealed record GuiPage(string FullName, string Component, string Method, string ReturnType, GuiField[] Fields);
public sealed record GuiField(string Name, string Label, string Type, int TabOrder, bool IsPrimary);
public sealed record GuiTestCase(string Id, string Name, string Input, string ExpectedOutput, int ExpectedExitCode, string ExpectedBehavior);
public sealed record SourceFile(string Path, string Content);
public sealed record GuiRunResult(bool Success, string Stdout, string Stderr, int ExitCode, string Verdict, string JudgeLayer, string Reason);
public sealed record SearchHit(string Id, string Kind, string Label, string Date);
public sealed record DbTableData(string Schema, string Table, long TotalRows, int Page, int PageSize, Dictionary<string, object?>[] Rows);