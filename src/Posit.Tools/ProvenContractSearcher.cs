namespace Posit.Tools;

using Npgsql;
using Posit.Data.Configuration;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

/// <summary>
/// Retrieves proven ArchitectureContracts from posit_proven_contracts by
/// semantic similarity to the incoming spec. Returns the top N matches as
/// complete JSON worked examples — few-shot prompting with the model's own
/// past successful output.
/// </summary>
public sealed class ProvenContractSearcher
{
    private readonly HttpClient _http;
    private readonly string _ollamaUrl;

    public ProvenContractSearcher(HttpClient http, string ollamaUrl = "http://127.0.0.1:11434")
    {
        _http = http;
        _ollamaUrl = ollamaUrl;
    }

    /// <summary>
    /// Search for proven contracts similar to the given spec.
    /// Returns a formatted string with up to `limit` complete JSON contracts,
    /// ready for injection into the architecture prompt as few-shot examples.
    /// </summary>
    public async Task<string> SearchAsync(string spec, int limit = 2, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return "";

        var embedding = await GetEmbeddingAsync(spec, ct);
        if (embedding is null || embedding.Length == 0)
            return "";

        var contracts = await SearchPgvectorAsync(embedding, limit, ct);
        if (contracts.Count == 0)
            return "";

        var sb = new StringBuilder();
        sb.AppendLine("Here are COMPLETE ArchitectureContract JSON examples from past SUCCESSFUL trials on similar specs.");
        sb.AppendLine("These contracts passed all Docker harness tests. Adapt their STRUCTURE (component count, method signatures, type chains, connection order) — change the names and logic to fit the new spec.");
        sb.AppendLine();
        for (var i = 0; i < contracts.Count; i++)
        {
            sb.AppendLine($"--- Proven Example {i + 1} (trial {contracts[i].TrialId}) ---");
            sb.AppendLine($"Spec was: {contracts[i].SpecText[..Math.Min(200, contracts[i].SpecText.Length)]}...");
            sb.AppendLine("Contract JSON:");
            sb.AppendLine(contracts[i].ContractJson);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private async Task<float[]?> GetEmbeddingAsync(string text, CancellationToken ct)
    {
        try
        {
            var payload = new { model = "nomic-embed-text", prompt = text };
            var response = await _http.PostAsJsonAsync($"{_ollamaUrl}/api/embeddings", payload, ct);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("embedding", out var embProp) && embProp.ValueKind == JsonValueKind.Array)
            {
                return embProp.EnumerateArray().Select(e => e.GetSingle()).ToArray();
            }
            return null;
        }
        catch { return null; }
    }

    private async Task<List<(string SpecText, string ContractJson, string TrialId)>> SearchPgvectorAsync(
        float[] embedding, int limit, CancellationToken ct)
    {
        var results = new List<(string, string, string)>();
        try
        {
            await using var dataSource = DbConnectionProvider.CreateDataSource();
            await using var conn = await dataSource.OpenConnectionAsync(ct);

            var embStr = "[" + string.Join(",", embedding) + "]";
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT spec_text, contract_json::text, trial_id
                FROM posit_proven_contracts
                ORDER BY spec_embedding <=> @embedding::vector
                LIMIT @limit
                """;
            cmd.Parameters.AddWithValue("embedding", embStr);
            cmd.Parameters.AddWithValue("limit", limit);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var spec = reader.IsDBNull(0) ? "" : reader.GetString(0);
                var json = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var trial = reader.IsDBNull(2) ? "" : reader.GetString(2);
                results.Add((spec, json, trial));
            }
        }
        catch { /* best effort — no proven contracts = no examples */ }
        return results;
    }
}