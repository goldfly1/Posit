using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Posit.AI.Models;

/// <summary>
/// Ollama-only model gateway. All model calls go through localhost:11434
/// using the /api/chat endpoint. No provider abstraction — Ollama is the
/// only backend. Cloud models are registered as Ollama tags (e.g., glm-5.2:cloud).
/// </summary>
public sealed class OllamaModelGateway : IModelGateway
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OllamaModelGateway(HttpClient httpClient, string? baseUrl = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
        _baseUrl = (baseUrl
                    ?? (httpClient.BaseAddress is not null ? httpClient.BaseAddress.ToString().TrimEnd('/') : null)
                    ?? Environment.GetEnvironmentVariable("OLLAMA_BASE_URL")
                    ?? "http://localhost:11434").TrimEnd('/');
    }

    public async Task<GenerationResult> GenerateAsync(ModelRoute route, PromptTemplate prompt, PhaseContext context, CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var model = string.IsNullOrWhiteSpace(route.ModelId) ? "llama3.1" : route.ModelId;
        var system = BuildSystemPrompt(prompt);
        var user = BuildUserPrompt(prompt, context);
        var maxTokens = route.MaxOutputTokens > 0 ? route.MaxOutputTokens : prompt.MaxOutputTokens;
        var effectiveMaxTokens = maxTokens > 0 ? (int?)maxTokens : null;

        var inputLen = (system?.Length ?? 0) + (user?.Length ?? 0);
        Console.Error.WriteLine($"[API] model={model} systemLen={system?.Length ?? 0} userLen={user?.Length ?? 0} totalInput={inputLen} maxTokens={effectiveMaxTokens} calling...");

        var request = new OllamaChatRequest
        {
            Model = model,
            Messages =
            [
                new OllamaMessage { Role = "system", Content = system ?? string.Empty },
                new OllamaMessage { Role = "user", Content = user ?? string.Empty }
            ],
            Stream = false,
            // Thinking OFF — Ollama thinking mode causes infinite loops (model repeats
            // the same paragraph 32K times, uses all output tokens on thinking, produces
            // zero response). Confirmed Aug 19: flash model loops on spec ambiguity.
            // Traces saved to .posit/staging/thinking/ when enabled for debugging.
            Think = false,
            Options = new OllamaOptions
            {
                Temperature = (float)route.Temperature,
                NumPredict = effectiveMaxTokens
            }
        };

        using var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUrl}/api/chat",
            request,
            JsonOptions,
            ct).ConfigureAwait(false);

        var httpStatus = (int)response.StatusCode;
        var elapsed = DateTimeOffset.UtcNow - startedAt;

        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            Console.Error.WriteLine($"[API] HTTP {httpStatus} after {elapsed.TotalSeconds:F1}s: {errBody[..Math.Min(200, errBody.Length)]}");
            response.EnsureSuccessStatusCode();
        }

        var body = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(JsonOptions, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Ollama returned an empty response body.");

        var text = body.Message?.Content ?? string.Empty;
        text = StripReasoningTags(text);
        // Only extract JSON for JSON-format prompts — plain text output must not be mangled
        if (prompt.OutputFormat == OutputFormat.Json)
            text = ExtractJson(text);

        // Save thinking trace to file (separate from the prompt/response)
        var thinking = body.Message?.Thinking;
        if (!string.IsNullOrWhiteSpace(thinking))
        {
            var thinkingDir = Path.Combine(Directory.GetCurrentDirectory(), ".posit", "staging", "thinking");
            Directory.CreateDirectory(thinkingDir);
            var thinkingFile = Path.Combine(thinkingDir, $"thinking-{context.PhaseId.Value}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(thinkingFile, thinking);
            Console.Error.WriteLine($"[API] Thinking trace saved: {thinkingFile} ({thinking.Length} chars)");
        }

        var inputTokens = body.PromptEvalCount ?? EstimateTokens(system + "\n" + user);
        var outputTokens = body.EvalCount ?? EstimateTokens(text);

        Console.Error.WriteLine($"[API] HTTP {httpStatus} OK after {elapsed.TotalSeconds:F1}s: inputTokens={inputTokens} outputTokens={outputTokens} responseLen={text.Length}");

        return new GenerationResult
        {
            Text = text,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CostUsd = 0m,
            Latency = DateTimeOffset.UtcNow - startedAt
        };
    }

    private static string BuildSystemPrompt(PromptTemplate prompt)
    {
        var sb = new StringBuilder();
        sb.AppendLine(prompt.SystemPrompt);
        if (prompt.OutputFormat == OutputFormat.Json && !string.IsNullOrWhiteSpace(prompt.OutputFormatSpec))
        {
            sb.AppendLine();
            sb.AppendLine("You must respond with a single JSON object matching the schema reference above. Do not include Markdown prose, explanations, or code fences outside the JSON. Ensure the JSON is valid and complete.");
        }
        return sb.ToString();
    }

    private static string BuildUserPrompt(PromptTemplate prompt, PhaseContext context)
    {
        // If the phase has built its own user prompt (replaced UserRequest with its own content),
        // send it as-is. The phase knows what it needs — don't add boilerplate, artifacts,
        // or corrections on top. This is the case for C#Impl, WireFixer.
        // We detect this by checking if UserRequest contains phase-specific markers.
        if (!string.IsNullOrWhiteSpace(context.UserRequest)
            && (context.UserRequest.Contains("═══", StringComparison.Ordinal)
                || context.UserRequest.Contains("INTERFACE DEFINITION", StringComparison.OrdinalIgnoreCase)
                || context.UserRequest.Contains("WIRE.CS", StringComparison.OrdinalIgnoreCase)
                || context.UserRequest.Contains("DAFNY SOURCE", StringComparison.OrdinalIgnoreCase)))
        {
            return context.UserRequest;
        }

        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(context.UserRequest))
        {
            sb.AppendLine(context.UserRequest);
            sb.AppendLine();
        }

        // CorrectionSignal — only for phases that don't build their own correction prompts.
        // Architecture relies on this for retry feedback.
        if (context.CorrectionSignal is { Length: > 0 })
        {
            sb.AppendLine("═══ CORRECTION SIGNAL — your previous output had these errors ═══");
            sb.AppendLine("Fix ALL of the following before resubmitting:");
            sb.AppendLine();
            foreach (var signal in context.CorrectionSignal)
            {
                sb.AppendLine($"• {signal}");
            }
            sb.AppendLine();
            sb.AppendLine("═══ END CORRECTION SIGNAL ═══");
            sb.AppendLine();
        }

        // Input artifacts — only for phases that need them (Architecture is first phase, has none;
        // QA needs test cases; other phases extract what they need themselves).
        foreach (var artifact in context.InputArtifacts)
        {
            var payloadJson = Encoding.UTF8.GetString(artifact.PayloadJson);
            sb.AppendLine($"Input artifact from {artifact.SourcePhase.Value} ({artifact.Kind}):");
            sb.AppendLine(payloadJson);
            sb.AppendLine();
        }

        if (sb.Length == 0)
            sb.AppendLine("Respond according to the system instructions.");

        if (prompt.OutputFormat == OutputFormat.Json)
            sb.AppendLine("Respond with valid JSON only.");
        else
            sb.AppendLine("Respond with raw code only — no JSON wrapping, no markdown fences, no explanations.");

        return sb.ToString();
    }

    /// <summary>
    /// Strip reasoning/thinking tags from models that emit them (Qwen, DeepSeek, GLM).
    /// Handles both closed and unclosed tags.
    /// </summary>
    public static string StripReasoningTags(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        text = Regex.Replace(text, "<think>.*?</think>", "", RegexOptions.Singleline);
        text = Regex.Replace(text, "<reasoning>.*?</reasoning>", "", RegexOptions.Singleline);
        text = Regex.Replace(text, "<thinking>.*?</thinking>", "", RegexOptions.Singleline);

        // Strip unclosed opening tag (model started thinking but never closed it)
        var unclosedStart = text.IndexOf("<think>", StringComparison.Ordinal);
        if (unclosedStart >= 0 && text.IndexOf("</think>", unclosedStart, StringComparison.Ordinal) < 0)
            text = text[..unclosedStart];
        unclosedStart = text.IndexOf("<reasoning>", StringComparison.Ordinal);
        if (unclosedStart >= 0 && text.IndexOf("</reasoning>", unclosedStart, StringComparison.Ordinal) < 0)
            text = text[..unclosedStart];
        unclosedStart = text.IndexOf("<thinking>", StringComparison.Ordinal);
        if (unclosedStart >= 0 && text.IndexOf("</thinking>", unclosedStart, StringComparison.Ordinal) < 0)
            text = text[..unclosedStart];

        return text.Trim();
    }

    /// <summary>
    /// Extract JSON from model output. Handles markdown code fences,
    /// prose-wrapped JSON, and balanced-brace scanning.
    /// </summary>
    public static string ExtractJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        text = text.Trim();

        if (text.StartsWith('{') || text.StartsWith('['))
            return text;

        // Strip markdown code fences
        var fenceMatch = Regex.Match(text, @"```(?:json)?\s*\n?(.*?)\n?```", RegexOptions.Singleline);
        if (fenceMatch.Success)
            return fenceMatch.Groups[1].Value.Trim();

        // Balanced-brace scan
        var start = text.IndexOf('{');
        if (start < 0)
        {
            start = text.IndexOf('[');
            if (start < 0)
                return text;
        }

        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (escape) { escape = false; continue; }
            if (c == '\\' && inString) { escape = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (c == '{' || c == '[') depth++;
            else if (c == '}' || c == ']')
            {
                depth--;
                if (depth == 0)
                    return text[start..(i + 1)].Trim();
            }
        }

        return text[start..].Trim();
    }

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        return Math.Max(1, Encoding.UTF8.GetByteCount(text) / 4);
    }

    private sealed class OllamaChatRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; set; }

        [JsonPropertyName("messages")]
        public required OllamaMessage[] Messages { get; set; }

        [JsonPropertyName("stream")]
        public required bool Stream { get; set; }

        [JsonPropertyName("options")]
        public OllamaOptions Options { get; set; } = new();

        [JsonPropertyName("think")]
        public bool Think { get; set; } = true;
    }

    private sealed class OllamaMessage
    {
        [JsonPropertyName("role")]
        public required string Role { get; set; }

        [JsonPropertyName("content")]
        public required string Content { get; set; }

        [JsonPropertyName("thinking")]
        public string? Thinking { get; set; }
    }

    private sealed class OllamaOptions
    {
        [JsonPropertyName("temperature")]
        public float Temperature { get; set; }

        [JsonPropertyName("num_predict")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? NumPredict { get; set; }
    }

    private sealed class OllamaChatResponse
    {
        [JsonPropertyName("message")]
        public OllamaMessage? Message { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }

        [JsonPropertyName("prompt_eval_count")]
        public int? PromptEvalCount { get; set; }

        [JsonPropertyName("eval_count")]
        public int? EvalCount { get; set; }
    }
}