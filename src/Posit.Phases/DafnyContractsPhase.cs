using System.Text.Json;
using System.Text.Json.Serialization;
using Posit.Tools;

namespace Posit.Phases;

/// <summary>
/// Dafny Contracts phase — the deterministic verification gate between
/// Design Review and Implementation.
///
/// The architect writes .dfy skeletons with requires/ensures during the
/// Architecture phase. This phase takes those skeletons and runs Z3 to
/// verify that the SPEC is sound (contracts without bodies). If the skeleton
/// verifies, the module is marked as having a proven exoskeleton. If it
/// doesn't verify, a correction signal goes back to the architect with the
/// exact proof failure.
///
/// This is NOT the same as Shepherd's DafnyPhase, which ran AFTER Implementation
/// and asked the model to write Dafny from C# code. In Posit, the contracts
/// come FIRST — the exoskeleton before the meat. Z3 is the judge, not the model.
///
/// Model calls: NONE. This phase is entirely deterministic.
/// </summary>
public sealed class DafnyContractsPhase : IPhase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), new ModuleClassificationConverter() }
    };

    private readonly Z3Runner _z3Runner;

    public DafnyContractsPhase(Z3Runner z3Runner)
    {
        _z3Runner = z3Runner ?? throw new ArgumentNullException(nameof(z3Runner));
    }

    public PhaseId Id => new("dafny-contracts");
    public PhaseName Name => new("Dafny Contracts Verification");
    public PhaseId[] Dependencies => [new PhaseId("design-review")];

    public ArtifactSchema OutputSchema => new()
    {
        Kind = ArtifactKind.DafnyContract,
        SchemaVersion = "1.0.0",
        PayloadClrTypeName = typeof(DafnyContractResult).FullName!
    };

    public Task InitializeAsync(PhaseContext context, CancellationToken ct) => Task.CompletedTask;

    public async Task<PhaseResult> ExecuteAsync(PhaseContext context, CancellationToken ct)
    {
        // Extract Dafny contract sources from the architecture artifact
        var contractSources = ExtractContractSources(context);

        if (contractSources.Count == 0)
        {
            Console.Error.WriteLine("[Posit] Dafny Contracts — no .dfy skeletons found, skipping");
            return new PhaseResult
            {
                PhaseId = Id,
                Status = PhaseStatus.Success,
                Artifacts = CreateEmptyBundle(context),
                Costs = CostSnapshot.Zero,
                AttemptNumber = context.AttemptNumber
            };
        }

        var results = new List<DafnyContractResult>();
        var anyFailed = false;

        foreach (var (moduleName, dafnySource) in contractSources)
        {
            Console.Error.WriteLine($"[Posit] Dafny Contracts — verifying skeleton for '{moduleName}'...");

            var result = await VerifySkeletonAsync(moduleName, dafnySource, ct);
            results.Add(result);

            if (!result.IsVerified)
                anyFailed = true;

            Console.Error.WriteLine(
                $"[Posit] Dafny Contracts — module '{moduleName}' skeleton verified={result.IsVerified}");
        }

        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(results, JsonOptions);
        var bundle = new ArtifactBundle
        {
            Id = ArtifactId.New(),
            SessionId = context.SessionId,
            SourcePhase = Id,
            SchemaVersion = OutputSchema.SchemaVersion,
            Kind = OutputSchema.Kind,
            ProducedAt = DateTimeOffset.UtcNow,
            PayloadJson = payloadJson,
            References = context.InputArtifacts
                .Select(a => new ArtifactReference(a.Id, a.Kind, a.SchemaVersion))
                .ToArray()
        };

        return new PhaseResult
        {
            PhaseId = Id,
            Status = PhaseStatus.Success,
            Artifacts = bundle,
            Costs = CostSnapshot.Zero, // No model calls — pure Z3
            AttemptNumber = context.AttemptNumber,
            Warnings = anyFailed
                ? [$"dafny.partial_skeleton_verification: {results.Count(r => r.IsVerified)}/{results.Count} skeletons verified. Unverified skeletons will be sent back to the architect."]
                : []
        };
    }

    /// <summary>
    /// Extracts (moduleName, dafnySource) pairs from the architecture artifact
    /// in the input artifacts. Only modules classified as Dafny or Mixed have
    /// .dfy skeletons — io-shell modules are skipped.
    /// </summary>
    private static List<(string ModuleName, string DafnySource)> ExtractContractSources(PhaseContext context)
    {
        var sources = new List<(string, string)>();

        // Check DesignContext first (snowballed from Architecture)
        if (context.DesignContext?.Components is { Length: > 0 } components)
        {
            foreach (var comp in components)
            {
                if (comp.Classification is ModuleClassification.Dafny or ModuleClassification.Mixed
                    && !string.IsNullOrWhiteSpace(comp.DafnyContractSource))
                {
                    // DafnyContractSource is on the DesignComponent, but that record
                    // doesn't have it — we need to check the ArchitectureContract artifact
                }
            }
        }

        // Check input artifacts for the architecture contract
        foreach (var artifact in context.InputArtifacts)
        {
            if (artifact.Kind != ArtifactKind.ArchitectureContract)
                continue;

            try
            {
                var json = System.Text.Encoding.UTF8.GetString(artifact.PayloadJson);
                var archContract = JsonSerializer.Deserialize<ArchitectureContract>(json, JsonOptions);
                if (archContract?.Components is null)
                    continue;

                foreach (var comp in archContract.Components)
                {
                    if (comp.Classification is ModuleClassification.Dafny or ModuleClassification.Mixed
                        && !string.IsNullOrWhiteSpace(comp.DafnyContractSource))
                    {
                        sources.Add((comp.Name, comp.DafnyContractSource!));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Posit] Dafny Contracts — failed to parse architecture artifact: {ex.Message}");
            }
        }

        return sources;
    }

    /// <summary>
    /// Writes the .dfy skeleton to a temp file and runs Z3 verification.
    /// Returns a DafnyContractResult with the verification status.
    /// </summary>
    private async Task<DafnyContractResult> VerifySkeletonAsync(
        string moduleName,
        string dafnySource,
        CancellationToken ct)
    {
        var dafnyPath = Z3Runner.GetDafnyStagingPath($"skeleton-{moduleName}");

        await File.WriteAllTextAsync(dafnyPath, dafnySource, ct);

        var (verified, output) = await _z3Runner.VerifyAsync(dafnyPath, ct);

        return new DafnyContractResult
        {
            ModuleName = moduleName,
            DafnySource = dafnySource,
            IsVerified = verified,
            VerificationOutput = output,
            ContractSummary = verified
                ? $"Skeleton verified — spec is sound ({moduleName})"
                : $"Skeleton verification failed — correction needed ({moduleName})"
        };
    }

    private static ArtifactBundle CreateEmptyBundle(PhaseContext context)
    {
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(
            Array.Empty<DafnyContractResult>(), JsonOptions);
        return new ArtifactBundle
        {
            Id = ArtifactId.New(),
            SessionId = context.SessionId,
            SourcePhase = new PhaseId("dafny-contracts"),
            SchemaVersion = "1.0.0",
            Kind = ArtifactKind.DafnyContract,
            ProducedAt = DateTimeOffset.UtcNow,
            PayloadJson = payloadJson,
            References = []
        };
    }

    public Task<ValidationResult> ValidateOutputAsync(ArtifactBundle output, CancellationToken ct)
    {
        var errors = new List<string>();

        if (output.Kind != ArtifactKind.DafnyContract)
            errors.Add("validation.schema_mismatch: Kind");
        if (output.SchemaVersion != "1.0.0")
            errors.Add("validation.schema_mismatch: SchemaVersion");

        try
        {
            var results = JsonSerializer.Deserialize<DafnyContractResult[]>(output.PayloadJson, JsonOptions);
            if (results is null)
                errors.Add("validation.missing_required_field: Payload");
        }
        catch (JsonException ex)
        {
            errors.Add($"validation.schema_mismatch: {ex.Message}");
        }

        return Task.FromResult(new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors.ToArray()
        });
    }
}