namespace Posit.Tools;

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using Posit.Data.Configuration;

/// <summary>
/// Semantic search over the wiki knowledge base (wiki.wiki_chunks table).
/// Uses Ollama nomic-embed-text for embeddings and pgvector cosine similarity.
///
/// When a phase hits an error (Z3 rejection, C# compile failure, etc.),
/// search the wiki for relevant Dafny stdlib examples, patterns, and
/// reference material. Inject the results into the correction prompt so
/// the model sees how the Dafny standard library solves similar problems.
///
/// This is the "fix finder" — it doesn't store errors, it finds examples.
/// The wiki knowledge base grows as we index more material (stdlib,
/// examples, tutorials, runtime source). Every phase benefits.
/// </summary>
public class WikiSearcher
{
    private readonly HttpClient _httpClient;
    private readonly string _ollamaUrl;
    private const string EmbedModel = "nomic-embed-text";
    private const int DefaultLimit = 3;
    private const int MaxContentLength = 500; // cap each result to keep prompts manageable

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public WikiSearcher(HttpClient httpClient, string? ollamaUrl = null)
    {
        _httpClient = httpClient;
        _ollamaUrl = (ollamaUrl
                      ?? Environment.GetEnvironmentVariable("OLLAMA_BASE_URL")
                      ?? "http://localhost:11434").TrimEnd('/');
    }

    /// <summary>
    /// Search the wiki for chunks semantically similar to the query.
    /// Returns formatted text ready to inject into a prompt.
    /// </summary>
    public async Task<string> SearchAsync(string query, int limit = DefaultLimit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "";

        var embedding = await GetEmbeddingAsync(query, ct);
        if (embedding == null || embedding.Length == 0)
            return "";

        var results = await SearchPgvectorAsync(embedding, limit, ct);
        if (results.Count == 0)
            return "";

        var sb = new StringBuilder();
        sb.AppendLine("═══ REFERENCE EXAMPLES FROM DAFNY STANDARD LIBRARY ═══");
        sb.AppendLine("The following examples from the Dafny standard library and test suite are");
        sb.AppendLine("semantically related to your error. Study how they solve the problem.");
        sb.AppendLine("Use these patterns — they are proven, verified Dafny code.");
        sb.AppendLine();

        foreach (var (file, title, content) in results)
        {
            sb.AppendLine($"── {title} (from {file}) ──");
            var truncated = content.Length > MaxContentLength
                ? content[..MaxContentLength] + "..."
                : content;
            sb.AppendLine(truncated);
            sb.AppendLine();
        }

        sb.AppendLine("═══ END REFERENCE EXAMPLES ═══");
        return sb.ToString();
    }

    /// <summary>
    /// Search and return raw results (for programmatic use, not prompt injection).
    /// </summary>
    public async Task<List<(string File, string Title, string Content)>> SearchRawAsync(
        string query, int limit = DefaultLimit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var embedding = await GetEmbeddingAsync(query, ct);
        if (embedding == null || embedding.Length == 0)
            return [];

        return await SearchPgvectorAsync(embedding, limit, ct);
    }

    private async Task<float[]?> GetEmbeddingAsync(string text, CancellationToken ct)
    {
        try
        {
            var payload = new { model = EmbedModel, prompt = text };
            var response = await _httpClient.PostAsJsonAsync($"{_ollamaUrl}/api/embeddings", payload, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var data = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (data.TryGetProperty("embedding", out var embProp) && embProp.ValueKind == JsonValueKind.Array)
            {
                return embProp.EnumerateArray().Select(e => e.GetSingle()).ToArray();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<List<(string File, string Title, string Content)>> SearchPgvectorAsync(
        float[] embedding, int limit, CancellationToken ct)
    {
        var results = new List<(string, string, string)>();
        try
        {
            await using var dataSource = DbConnectionProvider.CreateDataSource();
            await using var conn = await dataSource.OpenConnectionAsync(ct);

            // Convert float[] to pgvector format: '[0.1,0.2,...]'
            var embStr = "[" + string.Join(",", embedding) + "]";

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT file, title, content
                FROM wiki.wiki_chunks
                WHERE file LIKE '%dafny%' OR file LIKE '%Dafny%' OR file LIKE '%stdlib%' OR file LIKE '%examples%'
                ORDER BY embedding <=> @embedding::vector
                LIMIT @limit
                """;
            cmd.Parameters.AddWithValue("embedding", embStr);
            cmd.Parameters.AddWithValue("limit", limit);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var file = reader.GetString(0);
                var title = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var content = reader.IsDBNull(2) ? "" : reader.GetString(2);
                results.Add((file, title, content));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[wiki-search] Search failed: {ex.Message}");
        }

        return results;
    }
}