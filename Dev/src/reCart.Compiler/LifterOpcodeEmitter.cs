// @module: reCart.Compiler.LifterOpcodeEmitter
// @purpose: Emits clean, warning-free C# ICartRecompGameModule source for statically lifted 6502 basic blocks.
// @depends-on: System, System.Collections.Generic, System.Globalization, System.Text, reCart.Contracts
// @exports: LifterOpcodeEmitter

using System.Globalization;
using System.Text;
using reCart.Contracts;

namespace reCart.Compiler;

/// <summary>
/// Translates lifted <c>6502</c> basic blocks into clean C# source implementing <see cref="ICartRecompGameModule"/>.
/// </summary>
public static class LifterOpcodeEmitter
{
    internal static string EmitModuleSource(
        CartPackageManifest manifest,
        PrgBankLayout layout,
        ushort resetVector,
        ushort nmiVector,
        ushort irqVector,
        IReadOnlyList<LiftedBasicBlock> blocks)
    {
        (string ns, string cls) = SplitTypeName(manifest.ModuleTypeName);
        var sb = new StringBuilder(32768);
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Foundation.Retro;");
        sb.AppendLine("using reCart.Contracts;");
        sb.AppendLine();
        sb.Append("namespace ").Append(ns).AppendLine(";");
        sb.AppendLine();
        sb.Append("public sealed class ").Append(cls).AppendLine(" : ICartRecompGameModule");
        sb.AppendLine("{");
        EmitManifestAndFields(sb, manifest, ns, cls);
        EmitLifecycleMethods(sb, blocks);
        EmitDispatcher(sb, layout, blocks);
        EmitRuntimeHelpers(sb);
        foreach (LiftedBasicBlock block in blocks)
        {
            EmitBasicBlock(sb, block);
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitManifestAndFields(StringBuilder sb, CartPackageManifest m, string ns, string cls)
    {
        sb.AppendLine("    private readonly CartPackageManifest _manifest = new()");
        sb.AppendLine("    {");
        sb.Append("        SchemaVersion = ").Append(m.SchemaVersion).AppendLine(",");
        sb.Append("        ModuleId = \"").Append(Esc(m.ModuleId)).AppendLine("\",");
        sb.Append("        Title = \"").Append(Esc(m.Title)).AppendLine("\",");
        sb.Append("        PlatformId = \"").Append(Esc(m.PlatformId)).AppendLine("\",");
        sb.Append("        Version = \"").Append(Esc(m.Version)).AppendLine("\",");
        sb.Append("        EngineId = \"").Append(Esc(m.EngineId)).AppendLine("\",");
        sb.Append("        EngineVersion = \"").Append(Esc(m.EngineVersion)).AppendLine("\",");
        sb.Append("        CompatibleEngineTrack = \"").Append(Esc(m.CompatibleEngineTrack)).AppendLine("\",");
        sb.Append("        PreloadRomDir = \"").Append(Esc(m.PreloadRomDir)).AppendLine("\",");
        sb.Append("        ModuleTypeName = \"").Append(Esc($"{ns}.{cls}")).AppendLine("\",");
        sb.Append("        BackendId = \"").Append(Esc(m.BackendId ?? CartRenderBackendIds.OpenGl)).AppendLine("\",");
        sb.Append("        AspectRatioMode = \"").Append(Esc(m.AspectRatioMode ?? "nativePar")).AppendLine("\",");
        sb.Append("        ExecutionTierMode = \"").Append(Esc(m.ExecutionTierMode ?? "staticAotOnly")).AppendLine("\",");
        sb.Append("        VideoRegionStandardName = \"").Append(Esc(m.VideoRegionStandardName)).AppendLine("\",");
        sb.Append("        MapperId = ").Append(m.MapperId).AppendLine(",");
        sb.Append("        SubmapperId = ").Append(m.SubmapperId).AppendLine();
        sb.AppendLine("    };");
        sb.AppendLine("    private IReCartHost? _host;");
        sb.AppendLine("    private ICartMemoryBus? _bus;");
        sb.AppendLine("    private ICartCpuContext? _cpu;");
        sb.AppendLine("    private ICartPpuController? _ppu;");
        sb.AppendLine("    private ICartApuSink? _apu;");
        sb.AppendLine("    private bool _hitRti;");
        sb.AppendLine("    public string ModuleId => _manifest.ModuleId;");
        sb.AppendLine("    public ushort MapperId => _manifest.MapperId;");
        sb.AppendLine("    public byte SubmapperId => _manifest.SubmapperId;");
        sb.AppendLine("    public bool SupportsLockstepOracle => true;");
        sb.AppendLine("    public IRecompModuleManifest Manifest => _manifest;");
        sb.AppendLine("    public IReadOnlyList<RecompPatchDescriptor> TimingPatches => Array.Empty<RecompPatchDescriptor>();");
    }

    private static void EmitLifecycleMethods(StringBuilder sb, IReadOnlyList<LiftedBasicBlock> blocks)
    {
        var idleLoopAddrs = blocks
            .Where(b => b.Instructions.Count == 1 &&
                        b.Instructions[0].Spec.Mnemonic == Mnemonic6502.JMP &&
                        b.Instructions[0].Spec.Mode == AddressingMode6502.Absolute &&
                        b.Instructions[0].Operand16 == b.StartAddress)
            .Select(b => b.StartAddress)
            .Distinct()
            .ToArray();

        sb.AppendLine("    public void BindHost(IReCartHost host, ICartMemoryBus bus, ICartCpuContext cpu, ICartPpuController ppu, ICartApuSink apu)");
        sb.AppendLine("    {");
        sb.AppendLine("        _host = host ?? throw new ArgumentNullException(nameof(host));");
        sb.AppendLine("        _bus = bus ?? throw new ArgumentNullException(nameof(bus));");
        sb.AppendLine("        _cpu = cpu ?? throw new ArgumentNullException(nameof(cpu));");
        sb.AppendLine("        _ppu = ppu ?? throw new ArgumentNullException(nameof(ppu));");
        sb.AppendLine("        _apu = apu ?? throw new ArgumentNullException(nameof(apu));");
        sb.AppendLine("    }");
        sb.AppendLine("    public Task InitializeAsync(IRecompHost host, CancellationToken ct = default)");
        sb.AppendLine("    {");
        sb.AppendLine("        ct.ThrowIfCancellationRequested();");
        sb.AppendLine("        if (host is IReCartHost h) { BindHost(h, h.Bus, h.Cpu, h.Ppu, h.Apu); ExecuteResetVector(); }");
        sb.AppendLine("        return Task.CompletedTask;");
        sb.AppendLine("    }");
        sb.AppendLine("    public Task StepFrameAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); HandleNmi(); return Task.CompletedTask; }");
        sb.AppendLine("    public Task LoadSaveStateAsync(string saveStatePath, CancellationToken ct = default) => Task.CompletedTask;");
        sb.Append("    private static bool IsIdleSelfLoop(ushort addr) => addr is ");
        if (idleLoopAddrs.Length == 0) sb.AppendLine("0xFFFF && false;");
        else sb.Append(string.Join(" or ", idleLoopAddrs.Select(a => $"0x{a:X4}"))).AppendLine(";");
        sb.AppendLine("    public void ExecuteResetVector()");
        sb.AppendLine("    {");
        sb.AppendLine("        var bus = _bus ?? throw new InvalidOperationException(\"Host not bound.\");");
        sb.AppendLine("        var cpu = _cpu ?? throw new InvalidOperationException(\"Host not bound.\");");
        sb.AppendLine("        if (cpu.SP == 0 && cpu.CycleCount == 0) { cpu.SP = 0xFD; cpu.P = 0x24; }");
        sb.AppendLine("        cpu.PC = Read16(bus, 0xFFFC);");
        sb.AppendLine("        int idleSpins = 0;");
        sb.AppendLine("        for (int step = 0; step < 32768; step++)");
        sb.AppendLine("        {");
        sb.AppendLine("            ushort beforePc = cpu.PC;");
        sb.AppendLine("            ulong beforeCyc = cpu.CycleCount;");
        sb.AppendLine("            if (!TryDispatch(bus.ActivePrgBank, beforePc)) break;");
        sb.AppendLine("            if (cpu.PC == beforePc && IsIdleSelfLoop(beforePc)) break;");
        sb.AppendLine("            if (cpu.PC <= beforePc && (beforePc - cpu.PC) <= 8)");
        sb.AppendLine("            {");
        sb.AppendLine("                idleSpins++;");
        sb.AppendLine("                ulong dc = cpu.CycleCount > beforeCyc ? cpu.CycleCount - beforeCyc : 1;");
        sb.AppendLine("                for (ulong c = 0; c < dc; c++) { _ppu?.StepDot(); _ppu?.StepDot(); _ppu?.StepDot(); _apu?.ClockCycle(); }");
        sb.AppendLine("                if (idleSpins > 16384) break;");
        sb.AppendLine("            }");
        sb.AppendLine("            else { idleSpins = 0; }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("    public void HandleNmi() => ExecuteInterrupt(0xFFFA);");
        sb.AppendLine("    public void HandleIrq() => ExecuteInterrupt(0xFFFE);");
        sb.AppendLine("    private void ExecuteInterrupt(ushort vectorAddr)");
        sb.AppendLine("    {");
        sb.AppendLine("        var bus = _bus ?? throw new InvalidOperationException(\"Host not bound.\");");
        sb.AppendLine("        var cpu = _cpu ?? throw new InvalidOperationException(\"Host not bound.\");");
        sb.AppendLine("        byte preSp = cpu.SP;");
        sb.AppendLine("        Push(bus, cpu, (byte)(cpu.PC >> 8));");
        sb.AppendLine("        Push(bus, cpu, (byte)(cpu.PC & 0xFF));");
        sb.AppendLine("        Push(bus, cpu, (byte)((cpu.P & 0xEF) | 0x20));");
        sb.AppendLine("        cpu.P |= 0x04;");
        sb.AppendLine("        cpu.PC = Read16(bus, vectorAddr);");
        sb.AppendLine("        cpu.CycleCount += 7;");
        sb.AppendLine("        for (int step = 0; step < 16384; step++)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!TryDispatch(bus.ActivePrgBank, cpu.PC)) break;");
        sb.AppendLine("            if (_hitRti && cpu.SP == preSp) break;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    private static void EmitDispatcher(StringBuilder sb, PrgBankLayout layout, IReadOnlyList<LiftedBasicBlock> blocks)
    {
        sb.AppendLine("    public bool TryDispatch(uint prgBank, ushort address)");
        sb.AppendLine("    {");
        sb.AppendLine("        var bus = _bus ?? throw new InvalidOperationException(\"Host not bound.\");");
        sb.AppendLine("        var cpu = _cpu ?? throw new InvalidOperationException(\"Host not bound.\");");
        sb.AppendLine("        _hitRti = false;");
        if (layout.UsesSwitchableLowWindow)
        {
            sb.Append("        uint keyBank = address >= 0xC000 ? ").Append(layout.FixedBankIndex.ToString(CultureInfo.InvariantCulture))
              .Append("u : (prgBank % ").Append(layout.BankCount.ToString(CultureInfo.InvariantCulture)).AppendLine("u);");
            sb.AppendLine("        switch ((keyBank << 16) | address)");
            sb.AppendLine("        {");
            foreach (LiftedBasicBlock b in blocks)
            {
                uint packedKey = (b.PrgBank << 16) | b.StartAddress;
                sb.Append("            case 0x").Append(packedKey.ToString("X8", CultureInfo.InvariantCulture))
                  .Append("u: Block_B").Append(b.PrgBank.ToString("X2")).Append('_').Append(b.StartAddress.ToString("X4"))
                  .AppendLine("(bus, cpu); return true;");
            }
        }
        else
        {
            sb.AppendLine("        switch (address)");
            sb.AppendLine("        {");
            foreach (LiftedBasicBlock b in blocks)
            {
                sb.Append("            case 0x").Append(b.StartAddress.ToString("X4"))
                  .Append(": Block_B").Append(b.PrgBank.ToString("X2")).Append('_').Append(b.StartAddress.ToString("X4"))
                  .AppendLine("(bus, cpu); return true;");
            }
        }
        sb.AppendLine("            default:");
        sb.AppendLine("                CartExecutionTierMode tier = (_host as ICartMachineRuntime)?.ExecutionTier ?? CartExecutionTierModeCodec.Parse(_manifest.ExecutionTierMode);");
        sb.AppendLine("                if (tier == CartExecutionTierMode.StaticAotOnly)");
        sb.AppendLine("                {");
        sb.AppendLine("                    if (_host is ICartMachineRuntime rt) rt.Diagnostics.UnliftedTrapCount++;");
        sb.AppendLine("                    throw new global::reCart.Compiler.UnliftedPcTrapException(prgBank, address, ModuleId, tier);");
        sb.AppendLine("                }");
        sb.AppendLine("                return false;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    private static void EmitRuntimeHelpers(StringBuilder sb)
    {
        sb.AppendLine("    private static ushort Read16(ICartMemoryBus bus, ushort a) => (ushort)(bus.Read(a) | (bus.Read((ushort)(a + 1)) << 8));");
        sb.AppendLine("    private static ushort Read16Zp(ICartMemoryBus bus, byte zp) => (ushort)(bus.Read(zp) | (bus.Read((byte)(zp + 1)) << 8));");
        sb.AppendLine("    private static ushort Read16IndWrap(ICartMemoryBus bus, ushort a) => (ushort)(bus.Read(a) | (bus.Read((ushort)((a & 0xFF00) | ((a + 1) & 0xFF))) << 8));");
        sb.AppendLine("    private static ushort ReadAddrIdx(ICartCpuContext cpu, ushort b, byte i, bool px) { ushort t = (ushort)(b + i); if (px && (b & 0xFF00) != (t & 0xFF00)) cpu.CycleCount++; return t; }");
        sb.AppendLine("    private static ushort ReadIndY(ICartMemoryBus bus, ICartCpuContext cpu, byte zp, bool px) { ushort b = Read16Zp(bus, zp); ushort t = (ushort)(b + cpu.Y); if (px && (b & 0xFF00) != (t & 0xFF00)) cpu.CycleCount++; return t; }");
        sb.AppendLine("    private static void Push(ICartMemoryBus bus, ICartCpuContext cpu, byte v) { bus.Write((ushort)(0x0100 | cpu.SP), v); cpu.SP--; }");
        sb.AppendLine("    private static byte Pop(ICartMemoryBus bus, ICartCpuContext cpu) { cpu.SP++; return bus.Read((ushort)(0x0100 | cpu.SP)); }");
        sb.AppendLine("    private static void SetFlag(ICartCpuContext cpu, byte m, bool on) { if (on) cpu.P |= m; else cpu.P = (byte)(cpu.P & ~m); }");
        sb.AppendLine("    private static byte SetNZ(ICartCpuContext cpu, byte v) { cpu.P = (byte)((cpu.P & 0x7D) | (v == 0 ? 2 : 0) | (v & 0x80)); return v; }");
        sb.AppendLine("    private static void Adc(ICartCpuContext cpu, byte v) { int s = cpu.A + v + (cpu.P & 1); bool ov = (~(cpu.A ^ v) & (cpu.A ^ s) & 0x80) != 0; cpu.A = SetNZ(cpu, (byte)s); SetFlag(cpu, 1, s > 0xFF); SetFlag(cpu, 0x40, ov); }");
        sb.AppendLine("    private static void Cmp(ICartCpuContext cpu, byte r, byte v) { SetFlag(cpu, 1, r >= v); _ = SetNZ(cpu, (byte)(r - v)); }");
        sb.AppendLine("    private static void Bit(ICartCpuContext cpu, byte v) { SetFlag(cpu, 2, (cpu.A & v) == 0); cpu.P = (byte)((cpu.P & 0x3F) | (v & 0xC0)); }");
        sb.AppendLine("    private static byte Asl(ICartCpuContext cpu, byte v) { SetFlag(cpu, 1, (v & 0x80) != 0); return SetNZ(cpu, (byte)(v << 1)); }");
        sb.AppendLine("    private static byte Lsr(ICartCpuContext cpu, byte v) { SetFlag(cpu, 1, (v & 1) != 0); return SetNZ(cpu, (byte)(v >> 1)); }");
        sb.AppendLine("    private static byte Rol(ICartCpuContext cpu, byte v) { byte r = (byte)((v << 1) | (cpu.P & 1)); SetFlag(cpu, 1, (v & 0x80) != 0); return SetNZ(cpu, r); }");
        sb.AppendLine("    private static byte Ror(ICartCpuContext cpu, byte v) { byte r = (byte)((v >> 1) | ((cpu.P & 1) << 7)); SetFlag(cpu, 1, (v & 1) != 0); return SetNZ(cpu, r); }");
        sb.AppendLine("    private static void Branch(ICartCpuContext cpu, bool c, ushort f, ushort t) { if (!c) { cpu.PC = f; return; } cpu.CycleCount += (f & 0xFF00) != (t & 0xFF00) ? 2u : 1u; cpu.PC = t; }");
    }

    private static void EmitBasicBlock(StringBuilder sb, LiftedBasicBlock block)
    {
        sb.Append("    private void Block_B").Append(block.PrgBank.ToString("X2")).Append('_').Append(block.StartAddress.ToString("X4"))
          .AppendLine("(ICartMemoryBus bus, ICartCpuContext cpu)");
        sb.AppendLine("    {");
        ulong baseCycles = 0;
        foreach (DecodedInstruction6502 insn in block.Instructions)
        {
            baseCycles += insn.Spec.BaseCycles;
            EmitInstruction(sb, insn);
        }
        sb.Append("        cpu.CycleCount += ").Append(baseCycles.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
        DecodedInstruction6502 last = block.Instructions[^1];
        if (!Opcode6502Catalog.IsControlFlowTerminator(last.Spec.Mnemonic))
        {
            ushort nextPc = (ushort)(last.Address + last.Spec.Length);
            sb.Append("        cpu.PC = 0x").Append(nextPc.ToString("X4")).AppendLine(";");
        }
        sb.AppendLine("    }");
    }

    private static void EmitInstruction(StringBuilder sb, DecodedInstruction6502 insn)
    {
        Mnemonic6502 m = insn.Spec.Mnemonic;
        if (m is Mnemonic6502.LDA or Mnemonic6502.LDX or Mnemonic6502.LDY)
        {
            string reg = m == Mnemonic6502.LDA ? "A" : (m == Mnemonic6502.LDX ? "X" : "Y");
            sb.Append("        cpu.").Append(reg).Append(" = SetNZ(cpu, ").Append(LifterAddressingEmitter.BuildReadValueExpr(insn)).AppendLine(");");
            return;
        }
        if (m is Mnemonic6502.STA or Mnemonic6502.STX or Mnemonic6502.STY)
        {
            string reg = m == Mnemonic6502.STA ? "A" : (m == Mnemonic6502.STX ? "X" : "Y");
            sb.Append("        bus.Write(").Append(LifterAddressingEmitter.BuildEffectiveAddressExpr(insn)).Append(", cpu.").Append(reg).AppendLine(");");
            return;
        }
        if (m is Mnemonic6502.ASL or Mnemonic6502.LSR or Mnemonic6502.ROL or Mnemonic6502.ROR or Mnemonic6502.INC or Mnemonic6502.DEC)
        {
            EmitRmw(sb, insn);
            return;
        }
        EmitAluTransferAndFlow(sb, insn);
    }

    private static void EmitRmw(StringBuilder sb, DecodedInstruction6502 insn)
    {
        string fn = insn.Spec.Mnemonic switch
        {
            Mnemonic6502.ASL => "Asl(cpu, {0})",
            Mnemonic6502.LSR => "Lsr(cpu, {0})",
            Mnemonic6502.ROL => "Rol(cpu, {0})",
            Mnemonic6502.ROR => "Ror(cpu, {0})",
            Mnemonic6502.INC => "SetNZ(cpu, (byte)({0} + 1))",
            _ => "SetNZ(cpu, (byte)({0} - 1))"
        };
        if (insn.Spec.Mode == AddressingMode6502.Accumulator)
        {
            sb.Append("        cpu.A = ").AppendFormat(CultureInfo.InvariantCulture, fn, "cpu.A").AppendLine(";");
            return;
        }
        string ea = LifterAddressingEmitter.BuildEffectiveAddressExpr(insn);
        sb.Append("        { ushort ea = ").Append(ea).Append("; bus.Write(ea, ")
          .AppendFormat(CultureInfo.InvariantCulture, fn, "bus.Read(ea)").AppendLine("); }");
    }

    private static void EmitAluTransferAndFlow(StringBuilder sb, DecodedInstruction6502 insn)
    {
        ushort pc = insn.Address;
        ushort fall = (ushort)(pc + insn.Spec.Length);
        ushort rel = (ushort)(pc + 2 + (sbyte)insn.OperandLo);
        string line = insn.Spec.Mnemonic switch
        {
            Mnemonic6502.ADC => $"Adc(cpu, {LifterAddressingEmitter.BuildReadValueExpr(insn)});",
            Mnemonic6502.SBC => $"Adc(cpu, (byte)({LifterAddressingEmitter.BuildReadValueExpr(insn)} ^ 0xFF));",
            Mnemonic6502.AND => $"cpu.A = SetNZ(cpu, (byte)(cpu.A & {LifterAddressingEmitter.BuildReadValueExpr(insn)}));",
            Mnemonic6502.ORA => $"cpu.A = SetNZ(cpu, (byte)(cpu.A | {LifterAddressingEmitter.BuildReadValueExpr(insn)}));",
            Mnemonic6502.EOR => $"cpu.A = SetNZ(cpu, (byte)(cpu.A ^ {LifterAddressingEmitter.BuildReadValueExpr(insn)}));",
            Mnemonic6502.CMP => $"Cmp(cpu, cpu.A, {LifterAddressingEmitter.BuildReadValueExpr(insn)});",
            Mnemonic6502.CPX => $"Cmp(cpu, cpu.X, {LifterAddressingEmitter.BuildReadValueExpr(insn)});",
            Mnemonic6502.CPY => $"Cmp(cpu, cpu.Y, {LifterAddressingEmitter.BuildReadValueExpr(insn)});",
            Mnemonic6502.BIT => $"Bit(cpu, {LifterAddressingEmitter.BuildReadValueExpr(insn)});",
            Mnemonic6502.INX => "cpu.X = SetNZ(cpu, (byte)(cpu.X + 1));",
            Mnemonic6502.DEX => "cpu.X = SetNZ(cpu, (byte)(cpu.X - 1));",
            Mnemonic6502.INY => "cpu.Y = SetNZ(cpu, (byte)(cpu.Y + 1));",
            Mnemonic6502.DEY => "cpu.Y = SetNZ(cpu, (byte)(cpu.Y - 1));",
            Mnemonic6502.TAX => "cpu.X = SetNZ(cpu, cpu.A);",
            Mnemonic6502.TXA => "cpu.A = SetNZ(cpu, cpu.X);",
            Mnemonic6502.TAY => "cpu.Y = SetNZ(cpu, cpu.A);",
            Mnemonic6502.TYA => "cpu.A = SetNZ(cpu, cpu.Y);",
            Mnemonic6502.TSX => "cpu.X = SetNZ(cpu, cpu.SP);",
            Mnemonic6502.TXS => "cpu.SP = cpu.X;",
            Mnemonic6502.CLC => "SetFlag(cpu, 0x01, false);",
            Mnemonic6502.SEC => "SetFlag(cpu, 0x01, true);",
            Mnemonic6502.CLI => "SetFlag(cpu, 0x04, false);",
            Mnemonic6502.SEI => "SetFlag(cpu, 0x04, true);",
            Mnemonic6502.CLV => "SetFlag(cpu, 0x40, false);",
            Mnemonic6502.CLD => "SetFlag(cpu, 0x08, false);",
            Mnemonic6502.SED => "SetFlag(cpu, 0x08, true);",
            Mnemonic6502.PHA => "Push(bus, cpu, cpu.A);",
            Mnemonic6502.PLA => "cpu.A = SetNZ(cpu, Pop(bus, cpu));",
            Mnemonic6502.PHP => "Push(bus, cpu, (byte)(cpu.P | 0x30));",
            Mnemonic6502.PLP => "cpu.P = (byte)((Pop(bus, cpu) & 0xEF) | 0x20);",
            Mnemonic6502.JMP when insn.Spec.Mode == AddressingMode6502.Absolute => $"cpu.PC = 0x{insn.Operand16:X4};",
            Mnemonic6502.JMP => $"cpu.PC = Read16IndWrap(bus, 0x{insn.Operand16:X4});",
            Mnemonic6502.JSR => $"{{ ushort ret = 0x{(ushort)(pc + 2):X4}; Push(bus, cpu, (byte)(ret >> 8)); Push(bus, cpu, (byte)(ret & 0xFF)); cpu.PC = 0x{insn.Operand16:X4}; }}",
            Mnemonic6502.RTS => "{ byte lo = Pop(bus, cpu); byte hi = Pop(bus, cpu); cpu.PC = (ushort)(((lo | (hi << 8)) + 1) & 0xFFFF); }",
            Mnemonic6502.RTI => "{ cpu.P = (byte)((Pop(bus, cpu) & 0xEF) | 0x20); byte lo = Pop(bus, cpu); byte hi = Pop(bus, cpu); cpu.PC = (ushort)(lo | (hi << 8)); _hitRti = true; }",
            Mnemonic6502.BRK => $"{{ ushort ret = 0x{(ushort)(pc + 2):X4}; Push(bus, cpu, (byte)(ret >> 8)); Push(bus, cpu, (byte)(ret & 0xFF)); Push(bus, cpu, (byte)(cpu.P | 0x30)); cpu.P |= 0x04; cpu.PC = Read16(bus, 0xFFFE); }}",
            Mnemonic6502.BCC => $"Branch(cpu, (cpu.P & 0x01) == 0, 0x{fall:X4}, 0x{rel:X4});",
            Mnemonic6502.BCS => $"Branch(cpu, (cpu.P & 0x01) != 0, 0x{fall:X4}, 0x{rel:X4});",
            Mnemonic6502.BEQ => $"Branch(cpu, (cpu.P & 0x02) != 0, 0x{fall:X4}, 0x{rel:X4});",
            Mnemonic6502.BNE => $"Branch(cpu, (cpu.P & 0x02) == 0, 0x{fall:X4}, 0x{rel:X4});",
            Mnemonic6502.BMI => $"Branch(cpu, (cpu.P & 0x80) != 0, 0x{fall:X4}, 0x{rel:X4});",
            Mnemonic6502.BPL => $"Branch(cpu, (cpu.P & 0x80) == 0, 0x{fall:X4}, 0x{rel:X4});",
            Mnemonic6502.BVC => $"Branch(cpu, (cpu.P & 0x40) == 0, 0x{fall:X4}, 0x{rel:X4});",
            Mnemonic6502.BVS => $"Branch(cpu, (cpu.P & 0x40) != 0, 0x{fall:X4}, 0x{rel:X4});",
            _ => "// NOP"
        };
        sb.Append("        ").AppendLine(line);
    }

    private static (string Namespace, string ClassName) SplitTypeName(string? moduleTypeName)
    {
        if (string.IsNullOrWhiteSpace(moduleTypeName)) return ("reCart.Lifted", "CartLiftedModule");
        string trimmed = moduleTypeName.Trim();
        int idx = trimmed.LastIndexOf('.');
        return idx <= 0 || idx >= trimmed.Length - 1
            ? ("reCart.Lifted", trimmed)
            : (trimmed[..idx], trimmed[(idx + 1)..]);
    }

    private static string Esc(string? value) =>
        (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
}
