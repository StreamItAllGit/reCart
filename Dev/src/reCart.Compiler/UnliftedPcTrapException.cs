// @module: reCart.Compiler.UnliftedPcTrapException
// @purpose: Deterministic fail-closed exception raised when staticAotOnly dispatches an unlifted 6502 PC.
// @depends-on: System, reCart.Contracts
// @exports: UnliftedPcTrapException

using reCart.Contracts;

namespace reCart.Compiler;

/// <summary>
/// Fail-closed exception thrown under <see cref="CartExecutionTierMode.StaticAotOnly"/> when execution
/// reaches a bank/address that was not statically lifted, preventing silent fallback to <c>interpreterOracle</c>.
/// </summary>
public sealed class UnliftedPcTrapException : InvalidOperationException
{
    /// <summary>Gets the active switchable PRG bank index at the time of the trap.</summary>
    public uint PrgBank { get; }

    /// <summary>Gets the unlifted 16-bit 6502 program counter address.</summary>
    public ushort Address { get; }

    /// <summary>Gets the module identifier raising the fail-closed trap.</summary>
    public string ModuleId { get; }

    /// <summary>Gets the active execution tier enforcing fail-closed execution.</summary>
    public CartExecutionTierMode ExecutionTier { get; }

    /// <summary>Initializes a new <see cref="UnliftedPcTrapException"/> for an unlifted PC dispatch.</summary>
    public UnliftedPcTrapException(
        uint prgBank,
        ushort address,
        string moduleId,
        CartExecutionTierMode executionTier = CartExecutionTierMode.StaticAotOnly)
        : base($"Static AOT fail-closed trap in '{moduleId}': unlifted PC ${address:X4} in PRG bank {prgBank} under '{CartExecutionTierModeCodec.ToCanonicalId(executionTier)}' (silent interpreterOracle fallback is prohibited).")
    {
        PrgBank = prgBank;
        Address = address;
        ModuleId = moduleId;
        ExecutionTier = executionTier;
    }

    /// <summary>Initializes a new <see cref="UnliftedPcTrapException"/> with an explicit diagnostic message.</summary>
    public UnliftedPcTrapException(
        uint prgBank,
        ushort address,
        string moduleId,
        CartExecutionTierMode executionTier,
        string message)
        : base(message)
    {
        PrgBank = prgBank;
        Address = address;
        ModuleId = moduleId;
        ExecutionTier = executionTier;
    }
}
