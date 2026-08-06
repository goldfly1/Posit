namespace Posit.Contracts.Artifacts;

/// <summary>
/// Result of the Dafny Contracts phase for a single module. Contains the
/// Dafny skeleton source with formal contracts (requires/ensures), the
/// verification status from Z3, and (on success) the translated C# code.
///
/// In Posit, this is produced BEFORE Implementation — the architect writes
/// the skeleton, Z3 verifies the spec is sound, and only then does Imp fill
/// in the bodies. This is the exoskeleton pattern: contracts first, code second.
/// </summary>
public record DafnyContractResult
{
    public string ModuleName { get; init; } = "";
    public string DafnySource { get; init; } = "";
    public string[] VerifiedTypes { get; init; } = [];
    public string ContractSummary { get; init; } = "";

    /// <summary>
    /// True when `dafny verify` succeeded with 0 errors on the skeleton
    /// (contracts without bodies). Set by the Dafny Contracts phase after
    /// running the verifier — NOT by the model.
    /// </summary>
    public bool IsVerified { get; init; }

    /// <summary>
    /// Output from `dafny verify` (stdout+stderr). Populated when verification
    /// fails so the correction signal can include the proof failure details.
    /// </summary>
    public string? VerificationOutput { get; init; }
}

/// <summary>
/// Result of the Implementation phase's Dafny verification for a single module.
/// Contains the complete Dafny source (skeleton + bodies), verification status,
/// and translated C# code. This is the final verification gate before QA.
/// </summary>
public record DafnyVerificationResult
{
    public string ModuleName { get; init; } = "";
    public string DafnySource { get; init; } = "";
    public string[] VerifiedTypes { get; init; } = [];
    public string ContractSummary { get; init; } = "";

    /// <summary>
    /// True when `dafny verify` succeeded with 0 errors on the complete program
    /// (skeleton + bodies). Set by the Implementation phase after Z3 verification.
    /// </summary>
    public bool IsVerified { get; init; }

    public string? VerificationOutput { get; init; }

    /// <summary>
    /// C# code translated from Dafny via `dafny translate cs`.
    /// Only populated when IsVerified is true.
    /// </summary>
    public string? TranslatedCSharp { get; init; }
}