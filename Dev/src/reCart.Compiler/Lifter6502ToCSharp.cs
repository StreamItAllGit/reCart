// @module: reCart.Compiler.Lifter6502ToCSharp
// @purpose: Bank-aware static 6502 control-flow lifter translating PRG-ROM vectors, branches, calls, and jump tables into C# + dispatch metadata.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Security.Cryptography, System.Text.Json, reCart.Contracts
// @exports: LiftedBlockDescriptor, LiftedProgramResult, Lifter6502ToCSharp

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using reCart.Contracts;

namespace reCart.Compiler;

internal sealed record LiftedBasicBlock(
    uint PrgBank,
    ushort StartAddress,
    ushort EndAddress,
    IReadOnlyList<DecodedInstruction6502> Instructions,
    bool IsFixedWindow,
    bool IsEntryVector);

/// <summary>
/// Clean-room metadata descriptor for one statically lifted <c>6502</c> basic block.
/// </summary>
public sealed record LiftedBlockDescriptor(
    [property: JsonPropertyName("prgBank")] uint PrgBank,
    [property: JsonPropertyName("startAddress")] string StartAddress,
    [property: JsonPropertyName("endAddress")] string EndAddress,
    [property: JsonPropertyName("instructionCount")] int InstructionCount,
    [property: JsonPropertyName("isFixedWindow")] bool IsFixedWindow,
    [property: JsonPropertyName("isEntryVector")] bool IsEntryVector);

/// <summary>
/// Result of statically lifting a cartridge ROM into clean-room C# source and dispatch metadata.
/// </summary>
public sealed record LiftedProgramResult(
    CartRomHeader Header,
    string ModuleId,
    string ModuleTypeName,
    string RomSha256Hex,
    int PrgBankCount,
    uint FixedBankIndex,
    ushort ResetVector,
    ushort NmiVector,
    ushort IrqVector,
    IReadOnlyList<LiftedBlockDescriptor> Blocks,
    IReadOnlyList<ushort> JumpTableTargets,
    string CSharpSource,
    string DispatchMetaJson);

/// <summary>
/// Bank-aware static <c>6502 -&gt; C#</c> control-flow lifter rooted at <c>RESET</c>, <c>NMI</c>, and <c>IRQ</c>.
/// </summary>
public sealed class Lifter6502ToCSharp
{
    private static readonly JsonSerializerOptions MetaJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Parses <paramref name="romBytes"/> via <see cref="CartRomHeaderParser"/> and statically lifts reachable <c>6502</c> blocks to C#.
    /// </summary>
    public LiftedProgramResult Lift(
        ReadOnlySpan<byte> romBytes,
        CartPackageManifest manifest,
        IReadOnlyList<(uint Bank, ushort Address)>? additionalEntrySeeds = null) =>
        Lift(romBytes, manifest, additionalEntrySeeds, inlineJumpTableDispatchers: null);

    /// <summary>
    /// Parses <paramref name="romBytes"/> via <see cref="CartRomHeaderParser"/> and statically lifts reachable <c>6502</c> blocks and inline jump-table targets to C#.
    /// </summary>
    public LiftedProgramResult Lift(
        ReadOnlySpan<byte> romBytes,
        CartPackageManifest manifest,
        IReadOnlyList<(uint Bank, ushort Address)>? additionalEntrySeeds,
        IReadOnlySet<ushort>? inlineJumpTableDispatchers)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        CartRomHeader header = CartRomHeaderParser.Parse(romBytes);
        int prgOffset = CartRomHeaderParser.HeaderSizeBytes + (header.HasTrainer ? CartRomHeaderParser.TrainerSizeBytes : 0);
        if (header.PrgRomSizeBytes <= 0 || romBytes.Length < prgOffset + header.PrgRomSizeBytes)
        {
            throw new InvalidDataException($"ROM image truncated: expected {prgOffset + header.PrgRomSizeBytes} bytes, found {romBytes.Length}.");
        }

        ReadOnlySpan<byte> prgRom = romBytes.Slice(prgOffset, header.PrgRomSizeBytes);
        string romSha256 = Convert.ToHexString(SHA256.HashData(romBytes)).ToLowerInvariant();
        var layout = new PrgBankLayout(header);

        ushort nmiVec = layout.ReadRom16(prgRom, layout.FixedBankIndex, 0xFFFA);
        ushort resetVec = layout.ReadRom16(prgRom, layout.FixedBankIndex, 0xFFFC);
        ushort irqVec = layout.ReadRom16(prgRom, layout.FixedBankIndex, 0xFFFE);

        CartPackageManifest normalized = CloneWithDefaults(manifest, header);
        var entryPoints = new HashSet<(uint Bank, ushort Pc)>();
        var decoded = new Dictionary<(uint Bank, ushort Pc), DecodedInstruction6502>();
        var jumpTableTargets = new HashSet<ushort>();
        var worklist = new Queue<(uint Bank, ushort Pc)>();
        var vectorTargets = new HashSet<ushort> { resetVec, nmiVec, irqVec };

        void Enqueue(uint bank, ushort pc)
        {
            if (pc < PrgBankLayout.PrgBaseAddress) return;
            uint normBank = layout.NormalizeBank(bank, pc);
            if (entryPoints.Add((normBank, pc)))
            {
                worklist.Enqueue((normBank, pc));
            }
        }

        Enqueue(layout.FixedBankIndex, resetVec);
        Enqueue(layout.FixedBankIndex, nmiVec);
        Enqueue(layout.FixedBankIndex, irqVec);
        if (additionalEntrySeeds is not null)
        {
            foreach ((uint b, ushort addr) in additionalEntrySeeds) Enqueue(b, addr);
        }

        TraceControlFlow(prgRom, layout, worklist, decoded, jumpTableTargets, entryPoints, inlineJumpTableDispatchers, Enqueue);
        List<LiftedBasicBlock> blocks = BuildBasicBlocks(layout, entryPoints, decoded, vectorTargets);

        string csharp = LifterOpcodeEmitter.EmitModuleSource(normalized, layout, resetVec, nmiVec, irqVec, blocks);
        List<LiftedBlockDescriptor> descriptors = blocks
            .Select(b => new LiftedBlockDescriptor(
                b.PrgBank, $"0x{b.StartAddress:X4}", $"0x{b.EndAddress:X4}",
                b.Instructions.Count, b.IsFixedWindow, b.IsEntryVector))
            .ToList();

        ushort[] sortedJumpTargets = jumpTableTargets.OrderBy(a => a).ToArray();
        string dispatchMetaJson = SerializeDispatchMeta(normalized, layout, romSha256, resetVec, nmiVec, irqVec, descriptors, sortedJumpTargets);

        return new LiftedProgramResult(
            header, normalized.ModuleId, normalized.ModuleTypeName, romSha256,
            layout.BankCount, layout.FixedBankIndex, resetVec, nmiVec, irqVec,
            descriptors, sortedJumpTargets, csharp, dispatchMetaJson);
    }

    private static void TraceControlFlow(
        ReadOnlySpan<byte> prgRom,
        PrgBankLayout layout,
        Queue<(uint Bank, ushort Pc)> worklist,
        Dictionary<(uint Bank, ushort Pc), DecodedInstruction6502> decoded,
        HashSet<ushort> jumpTableTargets,
        IReadOnlySet<(uint Bank, ushort Pc)> entryPoints,
        IReadOnlySet<ushort>? inlineJumpTableDispatchers,
        Action<uint, ushort> enqueue)
    {
        while (worklist.Count > 0)
        {
            (uint bank, ushort pc) = worklist.Dequeue();
            uint activeSwitchBank = bank;
            byte? knownA = null;
            var trace = new List<DecodedInstruction6502>();

            while (pc >= PrgBankLayout.PrgBaseAddress)
            {
                uint curBank = layout.NormalizeBank(bank, pc);
                if (decoded.ContainsKey((curBank, pc))) break;

                int offset = layout.MapAddressToOffset(curBank, pc);
                if (!Opcode6502Catalog.TryDecode(prgRom, offset, pc, out DecodedInstruction6502 insn)) break;

                decoded[(curBank, pc)] = insn;
                trace.Add(insn);

                if (insn.Spec.Mnemonic == Mnemonic6502.LDA && insn.Spec.Mode == AddressingMode6502.Immediate)
                {
                    knownA = insn.OperandLo;
                }
                else if (insn.Spec.Mnemonic == Mnemonic6502.STA && insn.Spec.Mode == AddressingMode6502.Absolute &&
                         insn.Operand16 >= PrgBankLayout.PrgBaseAddress && layout.UsesSwitchableLowWindow && knownA is byte bVal)
                {
                    activeSwitchBank = (uint)(bVal % layout.BankCount);
                }
                else if (insn.Spec.Mnemonic is Mnemonic6502.LDA or Mnemonic6502.PLA or Mnemonic6502.TXA or Mnemonic6502.TYA or Mnemonic6502.ADC or Mnemonic6502.SBC)
                {
                    knownA = null;
                }

                Mnemonic6502 m = insn.Spec.Mnemonic;
                if (m == Mnemonic6502.JMP && insn.Spec.Mode == AddressingMode6502.Absolute)
                {
                    EnqueueLowOrFixed(layout, insn.Operand16, curBank, activeSwitchBank, knownA, prgRom, enqueue);
                    break;
                }
                if (m == Mnemonic6502.JMP && insn.Spec.Mode == AddressingMode6502.Indirect)
                {
                    foreach (ushort target in LifterJumpTableScanner.DiscoverIndirectJumpTargets(prgRom, layout, curBank, trace, insn.Operand16))
                    {
                        jumpTableTargets.Add(target);
                        EnqueueLowOrFixed(layout, target, curBank, activeSwitchBank, knownA, prgRom, enqueue);
                    }
                    break;
                }
                if (m == Mnemonic6502.JSR)
                {
                    EnqueueLowOrFixed(layout, insn.Operand16, curBank, activeSwitchBank, knownA, prgRom, enqueue);
                    if (inlineJumpTableDispatchers is not null && inlineJumpTableDispatchers.Contains(insn.Operand16))
                    {
                        ushort tableStart = (ushort)(pc + insn.Spec.Length);
                        foreach (ushort jt in LifterJumpTableScanner.DiscoverInlineJumpTableTargets(prgRom, layout, curBank, tableStart, entryPoints))
                        {
                            jumpTableTargets.Add(jt);
                            EnqueueLowOrFixed(layout, jt, curBank, activeSwitchBank, knownA: null, prgRom, enqueue);
                        }
                        break;
                    }
                    ushort fallthrough = (ushort)(pc + insn.Spec.Length);
                    if (fallthrough >= PrgBankLayout.PrgBaseAddress) enqueue(curBank, fallthrough);
                    break;
                }
                if (insn.Spec.Mode == AddressingMode6502.Relative)
                {
                    ushort target = (ushort)(pc + 2 + (sbyte)insn.OperandLo);
                    ushort fallthrough = (ushort)(pc + 2);
                    enqueue(target < 0xC000 ? activeSwitchBank : curBank, target);
                    if (fallthrough >= PrgBankLayout.PrgBaseAddress) enqueue(curBank, fallthrough);
                    break;
                }
                if (m == Mnemonic6502.RTS)
                {
                    foreach (ushort target in LifterJumpTableScanner.DiscoverRtsTrampolineTargets(prgRom, layout, curBank, trace))
                    {
                        jumpTableTargets.Add(target);
                        enqueue(target < 0xC000 ? activeSwitchBank : curBank, target);
                    }
                    break;
                }
                if (m is Mnemonic6502.RTI or Mnemonic6502.BRK) break;

                ushort nextPc = (ushort)(pc + insn.Spec.Length);
                if (nextPc < pc) break;
                pc = nextPc;
            }
        }
    }

    private static List<LiftedBasicBlock> BuildBasicBlocks(
        PrgBankLayout layout,
        HashSet<(uint Bank, ushort Pc)> entryPoints,
        Dictionary<(uint Bank, ushort Pc), DecodedInstruction6502> decoded,
        HashSet<ushort> vectorTargets)
    {
        var blocks = new List<LiftedBasicBlock>();
        foreach ((uint bank, ushort startPc) in entryPoints.OrderBy(k => k.Bank).ThenBy(k => k.Pc))
        {
            if (!decoded.ContainsKey((bank, startPc))) continue;
            var insns = new List<DecodedInstruction6502>();
            ushort curPc = startPc;

            while (decoded.TryGetValue((bank, curPc), out DecodedInstruction6502 insn))
            {
                insns.Add(insn);
                if (Opcode6502Catalog.IsControlFlowTerminator(insn.Spec.Mnemonic)) break;
                ushort nextPc = (ushort)(curPc + insn.Spec.Length);
                if (nextPc < curPc || entryPoints.Contains((bank, nextPc))) break;
                curPc = nextPc;
            }

            if (insns.Count == 0) continue;
            DecodedInstruction6502 last = insns[^1];
            ushort endPc = (ushort)(last.Address + last.Spec.Length - 1);
            blocks.Add(new LiftedBasicBlock(
                bank, startPc, endPc, insns,
                layout.IsFixedAddress(startPc),
                vectorTargets.Contains(startPc)));
        }
        return blocks;
    }

    private static CartPackageManifest CloneWithDefaults(CartPackageManifest m, CartRomHeader header) => new()
    {
        SchemaVersion = m.SchemaVersion < 1 ? 1 : m.SchemaVersion,
        ModuleId = string.IsNullOrWhiteSpace(m.ModuleId) ? "untitled-cart-module" : m.ModuleId.Trim(),
        Title = string.IsNullOrWhiteSpace(m.Title) ? m.ModuleId : m.Title.Trim(),
        PlatformId = CartEngineConstants.PlatformId,
        Version = string.IsNullOrWhiteSpace(m.Version) ? CartEngineConstants.Version : m.Version.Trim(),
        EngineId = string.IsNullOrWhiteSpace(m.EngineId) ? CartEngineConstants.EngineId : m.EngineId.Trim(),
        EngineVersion = string.IsNullOrWhiteSpace(m.EngineVersion) ? CartEngineConstants.Version : m.EngineVersion.Trim(),
        CompatibleEngineTrack = string.IsNullOrWhiteSpace(m.CompatibleEngineTrack) ? CartEngineConstants.CompatibleEngineTrack : m.CompatibleEngineTrack.Trim(),
        SupportedEngineTracks = m.SupportedEngineTracks is { Count: > 0 } ? [.. m.SupportedEngineTracks] : [.. CartEngineConstants.SupportedEngineTracks],
        PreloadRomDir = string.IsNullOrWhiteSpace(m.PreloadRomDir) ? "data/rom" : m.PreloadRomDir.Trim(),
        ModuleTypeName = string.IsNullOrWhiteSpace(m.ModuleTypeName) ? "reCart.Lifted.CartLiftedModule" : m.ModuleTypeName.Trim(),
        SerialNumber = m.SerialNumber,
        EntryAssembly = m.EntryAssembly,
        BackendId = string.IsNullOrWhiteSpace(m.BackendId) ? CartRenderBackendIds.OpenGl : m.BackendId,
        AspectRatioMode = string.IsNullOrWhiteSpace(m.AspectRatioMode) ? "nativePar" : m.AspectRatioMode,
        ExecutionTierMode = string.IsNullOrWhiteSpace(m.ExecutionTierMode) ? "staticAotOnly" : m.ExecutionTierMode,
        VideoRegionStandardName = string.IsNullOrWhiteSpace(m.VideoRegionStandardName) ? CartVideoRegionCodec.ToCanonicalId(header.VideoRegionStandard) : m.VideoRegionStandardName,
        MapperId = header.MapperId,
        SubmapperId = header.SubmapperId,
        DefaultFramerateMode = m.DefaultFramerateMode,
        RequiredDiscChecksums = [.. m.RequiredDiscChecksums]
    };

    private static string SerializeDispatchMeta(
        CartPackageManifest m,
        PrgBankLayout layout,
        string romSha256,
        ushort resetVec,
        ushort nmiVec,
        ushort irqVec,
        IReadOnlyList<LiftedBlockDescriptor> blocks,
        IReadOnlyList<ushort> jumpTargets)
    {
        var payload = new
        {
            schemaVersion = 1,
            moduleId = m.ModuleId,
            engineId = m.EngineId,
            engineVersion = m.EngineVersion,
            mapperId = m.MapperId,
            submapperId = m.SubmapperId,
            prgBankCount = layout.BankCount,
            fixedBankIndex = layout.FixedBankIndex,
            romSha256Hex = romSha256,
            vectors = new
            {
                reset = $"0x{resetVec:X4}",
                nmi = $"0x{nmiVec:X4}",
                irq = $"0x{irqVec:X4}"
            },
            jumpTableTargets = jumpTargets.Select(t => $"0x{t:X4}").ToArray(),
            liftedBlocks = blocks
        };
        return JsonSerializer.Serialize(payload, MetaJsonOptions);
    }

    private static void EnqueueLowOrFixed(
        PrgBankLayout layout,
        ushort target,
        uint curBank,
        uint activeSwitchBank,
        byte? knownA,
        ReadOnlySpan<byte> prgRom,
        Action<uint, ushort> enqueue)
    {
        if (!layout.UsesSwitchableLowWindow || target >= 0xC000)
        {
            enqueue(curBank, target);
            return;
        }
        enqueue(activeSwitchBank, target);
        if (knownA is null && layout.BankCount <= 16)
        {
            for (uint b = 0; b < (uint)layout.BankCount; b++)
            {
                if (b != activeSwitchBank && LifterJumpTableScanner.IsLikelyValidCodeEntry(prgRom, layout, b, target))
                {
                    enqueue(b, target);
                }
            }
        }
    }
}
