using Posit.Contracts.Artifacts;
using Posit.Contracts.Core;

namespace Posit.Phases;

/// <summary>
/// Abstraction over the pattern registry (Posit.Tools.PatternRegistry).
/// Defines the methods the phases need. The real PatternRegistry implements this.
/// </summary>
public interface IPatternRegistry
{
    string PatternsDirectory { get; }
    Dictionary<string, string> CSharpStubs { get; }
    bool HasPattern(string name);
    string GetPattern(string name);
    string[] GetPatternSignatures(string patternName);
    MethodSignature[] ExtractMethodSignatures(string patternName);
    string[] SelectCSharpStubs(string[] stubNames);
    string ComposeSkeleton(string patternName, string[] stubNames, string? parametersJson);
    string ComposeIoShellSkeleton(string stubName, string componentName);
    string[] MaterializeDependencies(string patternName, string stagingDir);
}

/// <summary>
/// Abstraction over Z3Runner (Posit.Tools.Z3Runner).
/// Verifies Dafny files and translates them to C#.
/// Uses DafnyVerificationResult from Posit.Contracts.Artifacts.
/// </summary>
public interface IZ3Runner
{
    Task<DafnyVerificationResult> VerifyAsync(string dafnyPath, string moduleName, CancellationToken ct = default);
    Task<DafnyVerificationResult> TranslateToCSharpAsync(string dafnyPath, string moduleName, CancellationToken ct = default);
    string GetDafnyStagingPath(string sessionId, string moduleName);
}