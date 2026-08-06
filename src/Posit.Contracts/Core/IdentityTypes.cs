namespace Posit.Contracts.Core;

public readonly record struct SessionId(string Value)
{
    public static SessionId Empty { get; } = new(string.Empty);
    public static SessionId New() => new(UlidFactory.Generate());
    public override string ToString() => Value;
}

public readonly record struct PhaseId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct PhaseName(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ArtifactId(string Value)
{
    public static ArtifactId New() => new(UlidFactory.Generate());
    public override string ToString() => Value;
}

public readonly record struct ProjectId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct EventId(string Value)
{
    public static EventId New() => new(UlidFactory.Generate());
    public override string ToString() => Value;
}

public readonly record struct Checksum(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct PromptVersion(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct CheckpointId(string Value)
{
    public static CheckpointId New() => new(UlidFactory.Generate());
    public override string ToString() => Value;
}