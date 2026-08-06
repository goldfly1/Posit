namespace Posit.Contracts.Artifacts;

/// <summary>
/// A single source code file produced by the implementation phases.
/// </summary>
public record SourceCodeFile(string Path, string Content);

/// <summary>
/// Bundle of source code files produced by the C# Implementation phase (Pass 2).
/// Contains both extern portal implementations and io-shell module classes.
/// </summary>
public record SourceCodeBundle
{
    public SourceCodeFile[] Files { get; init; } = [];
    public required string ProjectPath { get; init; }
    public required string TargetFramework { get; init; }
}