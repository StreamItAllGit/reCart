// @module: reCart.Compiler.LifterJumpTableScanner
// @purpose: Resolves static JMP (addr) pointers and PHA/RTS jump-table targets from decoded 6502 instruction streams.
// @depends-on: System, System.Collections.Generic
// @exports: LifterJumpTableScanner

namespace reCart.Compiler;

internal static class LifterJumpTableScanner
{
    private const int MaxTableEntries = 64;

    public static IReadOnlyList<ushort> DiscoverIndirectJumpTargets(
        ReadOnlySpan<byte> prgRom,
        PrgBankLayout layout,
        uint bank,
        IReadOnlyList<DecodedInstruction6502> trace,
        ushort pointerAddress)
    {
        var targets = new List<ushort>();
        if (pointerAddress >= PrgBankLayout.PrgBaseAddress)
        {
            ushort direct = layout.ReadRom16(prgRom, bank, pointerAddress, pageWrap6502: true);
            if (IsLikelyValidCodeEntry(prgRom, layout, bank, direct)) targets.Add(direct);
            return targets;
        }

        if (TryFindStorePair(trace, pointerAddress, out var loSrc, out var hiSrc))
        {
            ExtractFromSourcePair(prgRom, layout, bank, loSrc, hiSrc, addOneForRts: false, targets);
        }
        return targets;
    }

    public static IReadOnlyList<ushort> DiscoverRtsTrampolineTargets(
        ReadOnlySpan<byte> prgRom,
        PrgBankLayout layout,
        uint bank,
        IReadOnlyList<DecodedInstruction6502> trace)
    {
        var targets = new List<ushort>();
        int pha2 = FindLastIndex(trace, trace.Count - 2, Mnemonic6502.PHA);
        if (pha2 < 1) return targets;
        int pha1 = FindLastIndex(trace, pha2 - 1, Mnemonic6502.PHA);
        if (pha1 < 1) return targets;

        DecodedInstruction6502? hiLoad = FindPrecedingLoadA(trace, pha1 - 1);
        DecodedInstruction6502? loLoad = FindPrecedingLoadA(trace, pha2 - 1);
        if (hiLoad is { } hi && loLoad is { } lo)
        {
            ExtractFromSourcePair(prgRom, layout, bank, lo, hi, addOneForRts: true, targets);
        }
        return targets;
    }

    public static IReadOnlyList<ushort> DiscoverInlineJumpTableTargets(
        ReadOnlySpan<byte> prgRom,
        PrgBankLayout layout,
        uint curBank,
        ushort tableStartPc,
        IReadOnlySet<(uint Bank, ushort Pc)>? knownEntryPoints = null)
    {
        var targets = new List<ushort>();
        ushort? minForwardSameWindow = null;
        bool tableInFixedWindow = tableStartPc >= 0xC000;

        for (int i = 0; i < MaxTableEntries; i++)
        {
            uint rawPc = (uint)tableStartPc + (uint)(i * 2);
            if (rawPc + 1 > 0xFFFF || (curBank != layout.FixedBankIndex && rawPc >= 0xC000)) break;

            ushort entryPc = (ushort)rawPc;
            if (targets.Count > 0)
            {
                if (minForwardSameWindow is ushort minFwd && entryPc >= minFwd) break;
                if (knownEntryPoints is not null && knownEntryPoints.Contains((curBank, entryPc))) break;
            }

            int loOff = layout.MapAddressToOffset(curBank, entryPc);
            int hiOff = layout.MapAddressToOffset(curBank, (ushort)(entryPc + 1));
            if (loOff < 0 || hiOff < 0 || loOff >= prgRom.Length || hiOff >= prgRom.Length) break;

            ushort target = (ushort)(prgRom[loOff] | (prgRom[hiOff] << 8));
            if (!IsValidInlineTarget(prgRom, layout, curBank, target)) break;

            if (!targets.Contains(target)) targets.Add(target);
            bool targetSameWindow = (target >= 0xC000) == tableInFixedWindow;
            if (targetSameWindow && target >= tableStartPc)
            {
                minForwardSameWindow = minForwardSameWindow.HasValue ? Math.Min(minForwardSameWindow.Value, target) : target;
            }
        }
        return targets;
    }

    private static bool IsValidInlineTarget(ReadOnlySpan<byte> prgRom, PrgBankLayout layout, uint curBank, ushort target)
    {
        if (target < PrgBankLayout.PrgBaseAddress) return false;
        if (!layout.UsesSwitchableLowWindow || target >= 0xC000 || curBank != layout.FixedBankIndex)
        {
            return IsLikelyValidCodeEntry(prgRom, layout, layout.NormalizeBank(curBank, target), target);
        }

        int switchableCount = Math.Min(layout.BankCount, 16);
        for (uint b = 0; b < (uint)switchableCount; b++)
        {
            if (IsLikelyValidCodeEntry(prgRom, layout, b, target)) return true;
        }
        return false;
    }

    public static bool IsLikelyValidCodeEntry(ReadOnlySpan<byte> prgRom, PrgBankLayout layout, uint bank, ushort pc)
    {
        if (pc < PrgBankLayout.PrgBaseAddress) return false;
        ushort cur = pc;
        for (int i = 0; i < 6; i++)
        {
            int offset = layout.MapAddressToOffset(bank, cur);
            if (!Opcode6502Catalog.TryDecode(prgRom, offset, cur, out DecodedInstruction6502 insn))
            {
                return false;
            }
            if (insn.Spec.Mnemonic is Mnemonic6502.RTS or Mnemonic6502.RTI or Mnemonic6502.JMP or Mnemonic6502.JSR)
            {
                return true;
            }
            if (insn.Spec.Mnemonic == Mnemonic6502.BRK && i == 0) return false;
            cur = (ushort)(cur + insn.Spec.Length);
        }
        return true;
    }

    private static void ExtractFromSourcePair(
        ReadOnlySpan<byte> prgRom,
        PrgBankLayout layout,
        uint bank,
        DecodedInstruction6502 loSrc,
        DecodedInstruction6502 hiSrc,
        bool addOneForRts,
        List<ushort> targets)
    {
        if (loSrc.Spec.Mode == AddressingMode6502.Immediate && hiSrc.Spec.Mode == AddressingMode6502.Immediate)
        {
            ushort addr = (ushort)((loSrc.OperandLo | (hiSrc.OperandLo << 8)) + (addOneForRts ? 1 : 0));
            if (IsLikelyValidCodeEntry(prgRom, layout, bank, addr)) targets.Add(addr);
            return;
        }

        if (!IsIndexedRomLoad(loSrc) || !IsIndexedRomLoad(hiSrc)) return;
        ushort loBase = loSrc.Operand16;
        ushort hiBase = hiSrc.Operand16;
        int stride = 1;
        if (hiBase == loBase)
        {
            hiBase = (ushort)(loBase + 1);
            stride = 2;
        }
        else if (Math.Abs(hiBase - loBase) == 1)
        {
            stride = 2;
        }

        for (int i = 0; i < MaxTableEntries; i++)
        {
            int loOff = layout.MapAddressToOffset(bank, (ushort)(loBase + (i * stride)));
            int hiOff = layout.MapAddressToOffset(bank, (ushort)(hiBase + (i * stride)));
            if (loOff < 0 || hiOff < 0 || loOff >= prgRom.Length || hiOff >= prgRom.Length) break;

            ushort target = (ushort)((prgRom[loOff] | (prgRom[hiOff] << 8)) + (addOneForRts ? 1 : 0));
            if (!IsLikelyValidCodeEntry(prgRom, layout, bank, target)) break;
            if (!targets.Contains(target)) targets.Add(target);
        }
    }

    private static bool TryFindStorePair(
        IReadOnlyList<DecodedInstruction6502> trace,
        ushort ptrAddr,
        out DecodedInstruction6502 loSrc,
        out DecodedInstruction6502 hiSrc)
    {
        loSrc = default;
        hiSrc = default;
        int staLo = -1;
        int staHi = -1;
        for (int i = trace.Count - 2; i >= 0; i--)
        {
            DecodedInstruction6502 cur = trace[i];
            if (cur.Spec.Mnemonic != Mnemonic6502.STA) continue;
            ushort target = cur.Spec.Mode == AddressingMode6502.ZeroPage ? cur.OperandLo : cur.Operand16;
            if (staLo < 0 && target == ptrAddr) staLo = i;
            else if (staHi < 0 && target == (ushort)(ptrAddr + 1)) staHi = i;
            if (staLo >= 0 && staHi >= 0) break;
        }
        if (staLo < 1 || staHi < 1) return false;

        DecodedInstruction6502? lo = FindPrecedingLoadA(trace, staLo - 1);
        DecodedInstruction6502? hi = FindPrecedingLoadA(trace, staHi - 1);
        if (lo is null || hi is null) return false;
        loSrc = lo.Value;
        hiSrc = hi.Value;
        return true;
    }

    private static DecodedInstruction6502? FindPrecedingLoadA(IReadOnlyList<DecodedInstruction6502> trace, int startIdx)
    {
        for (int i = startIdx; i >= Math.Max(0, startIdx - 6); i--)
        {
            Mnemonic6502 m = trace[i].Spec.Mnemonic;
            if (m == Mnemonic6502.LDA) return trace[i];
            if (m is Mnemonic6502.STA or Mnemonic6502.STX or Mnemonic6502.STY or Mnemonic6502.PHA
                or Mnemonic6502.INX or Mnemonic6502.INY or Mnemonic6502.DEX or Mnemonic6502.DEY) continue;
            break;
        }
        return null;
    }

    private static int FindLastIndex(IReadOnlyList<DecodedInstruction6502> trace, int startIdx, Mnemonic6502 mnemonic)
    {
        for (int i = startIdx; i >= Math.Max(0, startIdx - 12); i--)
        {
            if (trace[i].Spec.Mnemonic == mnemonic) return i;
        }
        return -1;
    }

    private static bool IsIndexedRomLoad(DecodedInstruction6502 insn) =>
        insn.Spec.Mode is AddressingMode6502.AbsoluteX or AddressingMode6502.AbsoluteY &&
        insn.Operand16 >= PrgBankLayout.PrgBaseAddress;
}
