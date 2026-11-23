// @module: reCart.Player.ReCartLockstepRunner
// @purpose: Executes bounded differential AOT vs InterpreterOracle lockstep verification across registers and 2 KB WRAM.
// @depends-on: System, System.IO, System.Threading, System.Threading.Tasks, reCart.Compiler, reCart.Contracts, reCart.Core.Cpu, reCart.Core.Oracle, reCart.Core.Runtime
// @exports: ReCartLockstepRunner

using Foundation.Core;
using reCart.Compiler;
using reCart.Contracts;
using reCart.Core.Cpu;
using reCart.Core.Oracle;
using reCart.Core.Runtime;
using reEngine.Core.Bootstrap;

namespace reCart.Player;

internal static class ReCartLockstepRunner
{
    private const int MaxInstructionsPerBlock = 4096;

    public static async Task<int> RunAsync(
        PlayerCliOptions options,
        ICartCompilerPipeline pipeline,
        TextWriter output,
        TextWriter error,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        int targetSteps = options.LockstepSteps ?? 0;
        if (targetSteps <= 0)
        {
            error.WriteLine("Lockstep error: --lockstep-steps must be a positive integer.");
            return 3;
        }

        ReCartSessionHost.LoadedCartSessionInput loaded = ReCartSessionHost.LoadCartInputs(options);
        if (loaded.RomBytes is null || loaded.RomSha256Hex is null || loaded.RomHeader is null)
        {
            error.WriteLine($"Lockstep failed: no verified BYO ROM image found for module '{loaded.ModuleId}'.");
            return 4;
        }

        if (string.IsNullOrWhiteSpace(loaded.RecompPackagePath) || !File.Exists(loaded.RecompPackagePath))
        {
            error.WriteLine($"Lockstep failed: .recomp package not found for '{loaded.ModuleId}'.");
            return 4;
        }

        var logSink = new CappedSharedLogSink(Path.Combine(options.Environment.LogDirectory, $"{CartEngineConstants.AppId}.lockstep.log"));
        void Log(string tag, string msg) =>
            logSink.AppendLine(ReEngineTelemetryFormatter.FormatLogLine(tag, ReEngineTelemetryFormatter.StripHeartbeatPrefix(msg)));

        ICartRecompGameModule aotModule = pipeline is ColdAotCacheCompiler coldCompiler
            ? await coldCompiler.CompileAndLoadAsync(loaded.RecompPackagePath, loaded.RomSha256Hex, options.Environment.DataDirectory, loaded.RomBytes, m => Log("AOT", m), ct).ConfigureAwait(false)
            : await pipeline.CompileAndLoadAsync(loaded.RecompPackagePath, loaded.RomSha256Hex, options.Environment.DataDirectory, m => Log("AOT", m), ct).ConfigureAwait(false);

        CartVideoRegionStandard region = loaded.RomHeader.VideoRegionStandard == CartVideoRegionStandard.Auto
            ? CartVideoRegionStandard.Ntsc
            : loaded.RomHeader.VideoRegionStandard;

        using CartMachineHost aot = CartMachineFactory.CreateFromRomBytes(
            loaded.RomBytes, region, CartExecutionTierMode.StaticAotOnly, loaded.Manifest, loaded.PreloadRomDirectory, options.SaveDirectory);
        using CartMachineHost oracleHost = CartMachineFactory.CreateFromRomBytes(
            loaded.RomBytes, region, CartExecutionTierMode.InterpreterOracle, loaded.Manifest, loaded.PreloadRomDirectory, options.SaveDirectory);

        if (loaded.Manifest is not null)
        {
            aot.MountStaticModule(aotModule, loaded.Manifest, loaded.PreloadRomDirectory, options.SaveDirectory);
        }
        else
        {
            aotModule.BindHost(aot, aot.Bus, aot.Cpu, aot.Ppu, aot.Apu);
        }

        var oracle = InterpreterOracle.CreateAuthorized(CartExecutionTierMode.InterpreterOracle, (Cpu2A03Core)oracleHost.Cpu);
        int dotsDebt = 0;

        while (oracle.ExecutedSteps < targetSteps)
        {
            ct.ThrowIfCancellationRequested();

            bool aotNmi = aot.Ppu.TakeNmiPending();
            bool oracleNmi = oracleHost.Ppu.TakeNmiPending();
            if (aotNmi || oracleNmi)
            {
                if (aotNmi != oracleNmi)
                {
                    string msg = $"Lockstep NMI mismatch at step {oracle.ExecutedSteps}: aotNmi={aotNmi}, oracleNmi={oracleNmi}.";
                    Log("ERROR", msg);
                    error.WriteLine(msg);
                    return 1;
                }

                TriggerNmiVector(aot);
                TriggerNmiVector(oracleHost);
            }

            if (aot.Cpu.PC != oracleHost.Cpu.PC || aot.Bus.ActivePrgBank != oracleHost.Bus.ActivePrgBank)
            {
                string msg = $"Lockstep entry mismatch at step {oracle.ExecutedSteps}: " +
                             $"AOT=B{aot.Bus.ActivePrgBank:X2}:${aot.Cpu.PC:X4}, Oracle=B{oracleHost.Bus.ActivePrgBank:X2}:${oracleHost.Cpu.PC:X4}.";
                Log("ERROR", msg);
                error.WriteLine(msg);
                return 1;
            }

            ushort blockPc = aot.Cpu.PC;
            uint blockBank = aot.Bus.ActivePrgBank;
            ulong oracleCyclesBefore = oracleHost.Cpu.CycleCount;

            if (!aotModule.TryDispatch(blockBank, blockPc))
            {
                string msg = $"Lockstep trapped at unlifted B{blockBank:X2}:${blockPc:X4} after {oracle.ExecutedSteps} instructions.";
                Log("ERROR", msg);
                error.WriteLine(msg);
                return 1;
            }

            int blockInsns = 0;
            do
            {
                oracle.StepInstruction();
                blockInsns++;
            }
            while ((oracleHost.Cpu.PC != aot.Cpu.PC || oracleHost.Bus.ActivePrgBank != aot.Bus.ActivePrgBank)
                   && blockInsns < MaxInstructionsPerBlock);

            if (oracleHost.Cpu.PC != aot.Cpu.PC || oracleHost.Bus.ActivePrgBank != aot.Bus.ActivePrgBank)
            {
                string msg = $"Lockstep PC divergence from B{blockBank:X2}:${blockPc:X4} after {oracle.ExecutedSteps} steps: " +
                             $"AOT=B{aot.Bus.ActivePrgBank:X2}:${aot.Cpu.PC:X4}, Oracle=B{oracleHost.Bus.ActivePrgBank:X2}:${oracleHost.Cpu.PC:X4}.";
                Log("ERROR", msg);
                error.WriteLine(msg);
                return 1;
            }

            string? stateMismatch = CompareMachineState(aot, oracleHost, oracle.ExecutedSteps, blockPc, blockBank);
            if (stateMismatch is not null)
            {
                Log("ERROR", stateMismatch);
                error.WriteLine(stateMismatch);
                return 1;
            }

            int cpuDelta = Math.Max(1, (int)(oracleHost.Cpu.CycleCount - oracleCyclesBefore));
            StepSyncedPeripherals(aot, oracleHost, cpuDelta, region, ref dotsDebt);
        }

        string okMsg = $"Lockstep OK: module={loaded.ModuleId}, executedSteps={oracle.ExecutedSteps}, mismatchedSteps=0, AOT=B{aot.Bus.ActivePrgBank:X2}:${aot.Cpu.PC:X4}.";
        Log("INFO", okMsg);
        output.WriteLine(okMsg);
        return 0;
    }

    private static string? CompareMachineState(
        CartMachineHost aot,
        CartMachineHost oracle,
        long stepCount,
        ushort blockPc,
        uint blockBank)
    {
        if (aot.Cpu.A != oracle.Cpu.A || aot.Cpu.X != oracle.Cpu.X || aot.Cpu.Y != oracle.Cpu.Y ||
            aot.Cpu.SP != oracle.Cpu.SP || aot.Cpu.P != oracle.Cpu.P || aot.Cpu.PC != oracle.Cpu.PC ||
            aot.Bus.ActivePrgBank != oracle.Bus.ActivePrgBank)
        {
            return $"Lockstep register mismatch at step {stepCount} (block B{blockBank:X2}:${blockPc:X4}): " +
                   $"AOT[A={aot.Cpu.A:X2},X={aot.Cpu.X:X2},Y={aot.Cpu.Y:X2},SP={aot.Cpu.SP:X2},P={aot.Cpu.P:X2},PC={aot.Cpu.PC:X4},B={aot.Bus.ActivePrgBank}] vs " +
                   $"Oracle[A={oracle.Cpu.A:X2},X={oracle.Cpu.X:X2},Y={oracle.Cpu.Y:X2},SP={oracle.Cpu.SP:X2},P={oracle.Cpu.P:X2},PC={oracle.Cpu.PC:X4},B={oracle.Bus.ActivePrgBank}].";
        }

        for (ushort addr = 0; addr < 2048; addr++)
        {
            byte ab = aot.Bus.Read(addr);
            byte ob = oracle.Bus.Read(addr);
            if (ab != ob)
            {
                return $"Lockstep WRAM mismatch at step {stepCount} (block B{blockBank:X2}:${blockPc:X4}) addr=${addr:X4}: AOT={ab:X2}, Oracle={ob:X2}.";
            }
        }

        return null;
    }

    private static void TriggerNmiVector(CartMachineHost host)
    {
        ushort pc = host.Cpu.PC;
        host.Bus.Write((ushort)(0x0100 + host.Cpu.SP), (byte)(pc >> 8));
        host.Cpu.SP--;
        host.Bus.Write((ushort)(0x0100 + host.Cpu.SP), (byte)(pc & 0xFF));
        host.Cpu.SP--;
        host.Bus.Write((ushort)(0x0100 + host.Cpu.SP), (byte)((host.Cpu.P & 0xEF) | 0x20));
        host.Cpu.SP--;
        host.Cpu.P |= 0x04;
        ushort lo = host.Bus.Read(0xFFFA);
        ushort hi = host.Bus.Read(0xFFFB);
        host.Cpu.PC = (ushort)(lo | (hi << 8));
        host.Cpu.CycleCount += 7;
    }

    private static void StepSyncedPeripherals(
        CartMachineHost aot,
        CartMachineHost oracle,
        int cpuCycles,
        CartVideoRegionStandard region,
        ref int dotsDebt)
    {
        int dotsPerBatch = region == CartVideoRegionStandard.Pal ? 16 : 3;
        int cpuPerBatch = region == CartVideoRegionStandard.Pal ? 5 : 1;

        for (int c = 0; c < cpuCycles; c++)
        {
            aot.Apu.ClockCycle();
            oracle.Apu.ClockCycle();

            dotsDebt += dotsPerBatch;
            while (dotsDebt >= cpuPerBatch)
            {
                dotsDebt -= cpuPerBatch;
                aot.Ppu.StepDot();
                oracle.Ppu.StepDot();
            }
        }
    }
}
