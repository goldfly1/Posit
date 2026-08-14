import re
with open('src/Posit.Phases/ArchitecturePhase.cs', 'r') as f:
    content = f.read()
old = '''    private ArchitectureContract? ParseContract(string text)
    {
        try
        {
            // Strip reasoning tags and extract JSON from markdown fences
            var cleaned = OllamaModelGateway.StripReasoningTags(text);
            cleaned = OllamaModelGateway.ExtractJson(cleaned);
            return JsonSerializer.Deserialize<ArchitectureContract>(cleaned, PositJson.Options);
        }
        catch { return null; }
    }'''
new = '''    private ArchitectureContract? ParseContract(string text)
    {
        try
        {
            var cleaned = OllamaModelGateway.StripReasoningTags(text);
            cleaned = OllamaModelGateway.ExtractJson(cleaned);
            Console.Error.WriteLine($"[DEBUG] ParseContract: cleaned.Length={cleaned.Length} first200={cleaned[..Math.Min(200,cleaned.Length)]}");
            return JsonSerializer.Deserialize<ArchitectureContract>(cleaned, PositJson.Options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DEBUG] ParseContract FAILED: {ex.Message}");
            Console.Error.WriteLine($"[DEBUG] Raw first500: {text[..Math.Min(500,text.Length)]}");
            return null;
        }
    }'''
content = content.replace(old, new)
with open('src/Posit.Phases/ArchitecturePhase.cs', 'w') as f:
    f.write(content)
print("patched")