using Posit.Contracts.Artifacts;

namespace Posit.Phases;

/// <summary>
/// Abstraction over the pattern registry.
/// Only C# stub composition is needed — pattern methods removed.
/// </summary>
public interface IPatternRegistry
{
    string ComposeIoShellSkeleton(string stubName, string componentName);
    (string Name, string Responsibility)[] GetAllPatterns();
}