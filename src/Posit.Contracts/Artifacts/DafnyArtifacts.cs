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
    /// Path to the .dfy skeleton file on disk. The file is the authority.
    /// </summary>
    public string DafnyPath { get; init; } = "";

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
    public string DafnySource { get; init; } = "";  // kept for DB logging
    public string DafnyPath { get; init; } = "";  // file on disk — the authority
    public string[] VerifiedTypes { get; init; } = [];
    public string ContractSummary { get; init; } = "";
    public bool IsVerified { get; init; }
    public string? VerificationOutput { get; init; }
    public string? TranslatedCSharpPath { get; init; }  // translated C# file path on disk
}