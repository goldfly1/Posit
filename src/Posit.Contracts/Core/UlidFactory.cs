namespace Posit.Contracts.Core;

public static class UlidFactory
{
    public static string Generate()
    {
        var id = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        return id.Length >= 26 ? id[..26] : id.PadRight(26, '0');
    }
}