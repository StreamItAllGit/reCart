// @module: reCart.Core.Oracle.InterpreterOracle
// @purpose: Quarantined Tier 4 6502 interpreter oracle for explicit differential lockstep verification only.
// @depends-on: System, reCart.Contracts, reCart.Core.Cpu
// @exports: InterpreterOracle

using reCart.Contracts;
using reCart.Core.Cpu;

namespace reCart.Core.Oracle;

/// <summary>
/// Strictly quarantined Tier 4 reference interpreter path for differential <c>D4b</c> lockstep verification.
/// Throws <see cref="InvalidOperationException"/> unless <see cref="CartExecutionTierMode.InterpreterOracle"/> is active.
/// </summary>
public sealed class InterpreterOracle
{
    private readonly Cpu2A03Core _cpu;
    private readonly CartExecutionTierMode _executionTier;

    /// <summary>Initializes a quarantined Tier 4 oracle wrapping <paramref name="cpu"/>.</summary>
    public InterpreterOracle(CartExecutionTierMode executionTier, Cpu2A03Core cpu)
    {
        ArgumentNullException.ThrowIfNull(cpu);
        _executionTier = executionTier;
        _cpu = cpu;
    }

    /// <summary>Initializes a quarantined Tier 4 oracle wrapping <paramref name="cpu"/>.</summary>
    public InterpreterOracle(Cpu2A03Core cpu, CartExecutionTierMode executionTier)
        : this(executionTier, cpu)
    {
    }

    /// <summary>Configured execution tier governing quarantine enforcement.</summary>
    public CartExecutionTierMode ExecutionTier => _executionTier;

    /// <summary>Underlying <c>2A03</c> CPU instance.</summary>
    public Cpu2A03Core Cpu => _cpu;

    /// <summary>Total instructions stepped through the quarantined oracle.</summary>
    public long ExecutedSteps { get; private set; }

    /// <summary>Total CPU cycles stepped through the quarantined oracle.</summary>
    public long ExecutedCycles { get; private set; }

    /// <summary>Verifies that <paramref name="tier"/> explicitly permits quarantined Tier 4 execution.</summary>
    public static void EnsureAuthorized(CartExecutionTierMode tier)
    {
        if (tier != CartExecutionTierMode.InterpreterOracle)
        {
            throw new InvalidOperationException(
                $"Tier 4 InterpreterOracle is strictly quarantined and cannot execute under '{CartExecutionTierModeCodec.ToCanonicalId(tier)}'. " +
                "Pass '--execution-tier interpreterOracle' explicitly for differential verification.");
        }
    }

    /// <summary>Creates an <see cref="InterpreterOracle"/> and fails immediately if <paramref name="executionTier"/> is not <see cref="CartExecutionTierMode.InterpreterOracle"/>.</summary>
    public static InterpreterOracle CreateAuthorized(CartExecutionTierMode executionTier, Cpu2A03Core cpu)
    {
        EnsureAuthorized(executionTier);
        return new InterpreterOracle(executionTier, cpu);
    }

    /// <summary>Executes one single 6502 instruction under explicit <see cref="CartExecutionTierMode.InterpreterOracle"/> quarantine.</summary>
    public int StepInstruction() => StepInstruction(_executionTier);

    /// <summary>Executes one single 6502 instruction, validating <paramref name="activeTier"/> is <see cref="CartExecutionTierMode.InterpreterOracle"/>.</summary>
    public int StepInstruction(CartExecutionTierMode activeTier)
    {
        EnsureAuthorized(activeTier);
        int cycles = _cpu.StepInstruction();
        ExecutedSteps++;
        ExecutedCycles += cycles;
        return cycles;
    }

    /// <summary>Executes at least <paramref name="cycleBudget"/> CPU cycles in quarantined Tier 4 mode.</summary>
    public int ExecuteCycles(int cycleBudget)
    {
        EnsureAuthorized(_executionTier);
        if (cycleBudget <= 0)
        {
            return 0;
        }

        int consumed = 0;
        while (consumed < cycleBudget)
        {
            int cycles = _cpu.StepInstruction();
            ExecutedSteps++;
            ExecutedCycles += cycles;
            consumed += cycles;
        }

        return consumed;
    }
}
