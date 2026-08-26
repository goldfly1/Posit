using System.Text.Json.Serialization;

namespace Posit.Contracts.Core;

public enum ArtifactKind
{
    RequirementsDocument,
    ArchitectureContract,
    APIDefinition,
    PseudocodeModule,
    DesignReview,
    DafnyContract,
    SourceCodeFile,
    SourceCodeBundle,
    DafnyVerification,
    TestSuite,
    DeploymentManifest,
    ObservabilityConfig,
    DocumentationSet
}

public enum Priority { Must, Should, Could, Wont }
public enum LogLevel { Trace, Debug, Information, Warning, Error, Critical }
public enum Audience { User, Dev, Ops, Executive }
public enum DeployArtifactKind { Image, Binary, Package, Container }
public enum RiskSeverity { Low, Medium, High, Critical }
public enum AlertSeverity { Low, Medium, High, Critical }
public enum ConstraintKind { Technical, Regulatory, Time, Budget, Operational }
public enum DataStoreKind { Relational, Document, Vector, Cache, Queue, Object }
public enum PersistenceKind { Ephemeral, Persistent, Replicated }
public enum GenerationKind { AI, Human, Mixed }
public enum TestStatus { Passed, Failed, Skipped, Flaky }
public enum DeployStrategy { BlueGreen, Rolling, Canary, Recreate, Manual }
public enum SecretSource { UserSecrets, Env, Vault }
public enum GateTimeoutPolicy { AutoReject, AutoApprove, BlockForever }
public enum RoutingStrategy { Static, ComplexityBased, Cascade }
public enum ModelTier { Frontier, Standard, Fast }
public enum SandboxKind { Fake, Docker, MicroVM }
public enum PromptStatus { Active, Superseded, Deprecated }
public enum OutputFormat { Json, Markdown, Yaml, Xml, PlainText }

/// <summary>
/// How the architect classifies a module for the C#-direct pipeline.
/// logic = pure logic, no I/O. io-shell = side effects, I/O handling.
/// </summary>
[JsonConverter(typeof(ModuleClassificationConverter))]
public enum ModuleClassification
{
    Logic,
    IoShell,
    Mixed
}