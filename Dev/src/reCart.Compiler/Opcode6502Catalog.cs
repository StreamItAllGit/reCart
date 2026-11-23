// @module: reCart.Compiler.Opcode6502Catalog
// @purpose: Complete 56-mnemonic / 151-opcode official RP2A03 6502 instruction table and decoder.
// @depends-on: System
// @exports: AddressingMode6502, Mnemonic6502, OpcodeSpec6502, DecodedInstruction6502, Opcode6502Catalog

namespace reCart.Compiler;

internal enum AddressingMode6502 : byte
{
    Implied, Accumulator, Immediate, ZeroPage, ZeroPageX, ZeroPageY,
    Relative, Absolute, AbsoluteX, AbsoluteY, Indirect, IndirectX, IndirectY
}

internal enum Mnemonic6502 : byte
{
    Invalid = 0,
    ADC, AND, ASL, BCC, BCS, BEQ, BIT, BMI, BNE, BPL, BRK, BVC, BVS,
    CLC, CLD, CLI, CLV, CMP, CPX, CPY, DEC, DEX, DEY, EOR, INC, INX, INY,
    JMP, JSR, LDA, LDX, LDY, LSR, NOP, ORA, PHA, PHP, PLA, PLP, ROL, ROR,
    RTI, RTS, SBC, SEC, SED, SEI, STA, STX, STY, TAX, TAY, TSX, TXA, TXS, TYA
}

internal readonly record struct OpcodeSpec6502(
    byte Opcode,
    Mnemonic6502 Mnemonic,
    AddressingMode6502 Mode,
    byte Length,
    byte BaseCycles,
    bool CheckPageCross)
{
    public bool IsValid => Mnemonic != Mnemonic6502.Invalid;
}

internal readonly record struct DecodedInstruction6502(
    ushort Address,
    OpcodeSpec6502 Spec,
    byte OperandLo,
    byte OperandHi,
    ushort Operand16);

internal static class Opcode6502Catalog
{
    private static readonly OpcodeSpec6502[] Specs = BuildTable();

    public static OpcodeSpec6502 GetSpec(byte opcode) => Specs[opcode];

    public static bool TryDecode(ReadOnlySpan<byte> prgRom, int offset, ushort pc, out DecodedInstruction6502 instruction)
    {
        instruction = default;
        if ((uint)offset >= (uint)prgRom.Length) return false;
        OpcodeSpec6502 spec = Specs[prgRom[offset]];
        if (!spec.IsValid || offset + spec.Length > prgRom.Length) return false;

        byte lo = spec.Length >= 2 ? prgRom[offset + 1] : (byte)0;
        byte hi = spec.Length >= 3 ? prgRom[offset + 2] : (byte)0;
        ushort op16 = (ushort)(lo | (hi << 8));
        instruction = new DecodedInstruction6502(pc, spec, lo, hi, op16);
        return true;
    }

    public static bool IsControlFlowTerminator(Mnemonic6502 m) => m is
        Mnemonic6502.JMP or Mnemonic6502.JSR or Mnemonic6502.RTS or Mnemonic6502.RTI or Mnemonic6502.BRK or
        Mnemonic6502.BCC or Mnemonic6502.BCS or Mnemonic6502.BEQ or Mnemonic6502.BMI or
        Mnemonic6502.BNE or Mnemonic6502.BPL or Mnemonic6502.BVC or Mnemonic6502.BVS;

    private static OpcodeSpec6502[] BuildTable()
    {
        var t = new OpcodeSpec6502[256];
        void Set(byte op, Mnemonic6502 m, AddressingMode6502 mode, byte len, byte cyc, bool px = false)
            => t[op] = new OpcodeSpec6502(op, m, mode, len, cyc, px);
        void Alu8(Mnemonic6502 m, byte imm, byte zp, byte zpx, byte abs, byte ax, byte ay, byte ix, byte iy)
        {
            Set(imm, m, AddressingMode6502.Immediate, 2, 2); Set(zp, m, AddressingMode6502.ZeroPage, 2, 3);
            Set(zpx, m, AddressingMode6502.ZeroPageX, 2, 4); Set(abs, m, AddressingMode6502.Absolute, 3, 4);
            Set(ax, m, AddressingMode6502.AbsoluteX, 3, 4, true); Set(ay, m, AddressingMode6502.AbsoluteY, 3, 4, true);
            Set(ix, m, AddressingMode6502.IndirectX, 2, 6); Set(iy, m, AddressingMode6502.IndirectY, 2, 5, true);
        }
        void Rmw5(Mnemonic6502 m, byte acc, byte zp, byte zpx, byte abs, byte ax)
        {
            Set(acc, m, AddressingMode6502.Accumulator, 1, 2); Set(zp, m, AddressingMode6502.ZeroPage, 2, 5);
            Set(zpx, m, AddressingMode6502.ZeroPageX, 2, 6); Set(abs, m, AddressingMode6502.Absolute, 3, 6);
            Set(ax, m, AddressingMode6502.AbsoluteX, 3, 7);
        }

        Alu8(Mnemonic6502.ORA, 0x09, 0x05, 0x15, 0x0D, 0x1D, 0x19, 0x01, 0x11);
        Alu8(Mnemonic6502.AND, 0x29, 0x25, 0x35, 0x2D, 0x3D, 0x39, 0x21, 0x31);
        Alu8(Mnemonic6502.EOR, 0x49, 0x45, 0x55, 0x4D, 0x5D, 0x59, 0x41, 0x51);
        Alu8(Mnemonic6502.ADC, 0x69, 0x65, 0x75, 0x6D, 0x7D, 0x79, 0x61, 0x71);
        Alu8(Mnemonic6502.LDA, 0xA9, 0xA5, 0xB5, 0xAD, 0xBD, 0xB9, 0xA1, 0xB1);
        Alu8(Mnemonic6502.CMP, 0xC9, 0xC5, 0xD5, 0xCD, 0xDD, 0xD9, 0xC1, 0xD1);
        Alu8(Mnemonic6502.SBC, 0xE9, 0xE5, 0xF5, 0xED, 0xFD, 0xF9, 0xE1, 0xF1);

        Set(0x85, Mnemonic6502.STA, AddressingMode6502.ZeroPage, 2, 3);
        Set(0x95, Mnemonic6502.STA, AddressingMode6502.ZeroPageX, 2, 4);
        Set(0x8D, Mnemonic6502.STA, AddressingMode6502.Absolute, 3, 4);
        Set(0x9D, Mnemonic6502.STA, AddressingMode6502.AbsoluteX, 3, 5);
        Set(0x99, Mnemonic6502.STA, AddressingMode6502.AbsoluteY, 3, 5);
        Set(0x81, Mnemonic6502.STA, AddressingMode6502.IndirectX, 2, 6);
        Set(0x91, Mnemonic6502.STA, AddressingMode6502.IndirectY, 2, 6);

        Rmw5(Mnemonic6502.ASL, 0x0A, 0x06, 0x16, 0x0E, 0x1E);
        Rmw5(Mnemonic6502.ROL, 0x2A, 0x26, 0x36, 0x2E, 0x3E);
        Rmw5(Mnemonic6502.LSR, 0x4A, 0x46, 0x56, 0x4E, 0x5E);
        Rmw5(Mnemonic6502.ROR, 0x6A, 0x66, 0x76, 0x6E, 0x7E);

        Set(0xC6, Mnemonic6502.DEC, AddressingMode6502.ZeroPage, 2, 5);
        Set(0xD6, Mnemonic6502.DEC, AddressingMode6502.ZeroPageX, 2, 6);
        Set(0xCE, Mnemonic6502.DEC, AddressingMode6502.Absolute, 3, 6);
        Set(0xDE, Mnemonic6502.DEC, AddressingMode6502.AbsoluteX, 3, 7);
        Set(0xE6, Mnemonic6502.INC, AddressingMode6502.ZeroPage, 2, 5);
        Set(0xF6, Mnemonic6502.INC, AddressingMode6502.ZeroPageX, 2, 6);
        Set(0xEE, Mnemonic6502.INC, AddressingMode6502.Absolute, 3, 6);
        Set(0xFE, Mnemonic6502.INC, AddressingMode6502.AbsoluteX, 3, 7);

        Set(0xA2, Mnemonic6502.LDX, AddressingMode6502.Immediate, 2, 2);
        Set(0xA6, Mnemonic6502.LDX, AddressingMode6502.ZeroPage, 2, 3);
        Set(0xB6, Mnemonic6502.LDX, AddressingMode6502.ZeroPageY, 2, 4);
        Set(0xAE, Mnemonic6502.LDX, AddressingMode6502.Absolute, 3, 4);
        Set(0xBE, Mnemonic6502.LDX, AddressingMode6502.AbsoluteY, 3, 4, true);

        Set(0xA0, Mnemonic6502.LDY, AddressingMode6502.Immediate, 2, 2);
        Set(0xA4, Mnemonic6502.LDY, AddressingMode6502.ZeroPage, 2, 3);
        Set(0xB4, Mnemonic6502.LDY, AddressingMode6502.ZeroPageX, 2, 4);
        Set(0xAC, Mnemonic6502.LDY, AddressingMode6502.Absolute, 3, 4);
        Set(0xBC, Mnemonic6502.LDY, AddressingMode6502.AbsoluteX, 3, 4, true);

        Set(0x86, Mnemonic6502.STX, AddressingMode6502.ZeroPage, 2, 3);
        Set(0x96, Mnemonic6502.STX, AddressingMode6502.ZeroPageY, 2, 4);
        Set(0x8E, Mnemonic6502.STX, AddressingMode6502.Absolute, 3, 4);
        Set(0x84, Mnemonic6502.STY, AddressingMode6502.ZeroPage, 2, 3);
        Set(0x94, Mnemonic6502.STY, AddressingMode6502.ZeroPageX, 2, 4);
        Set(0x8C, Mnemonic6502.STY, AddressingMode6502.Absolute, 3, 4);

        Set(0xE0, Mnemonic6502.CPX, AddressingMode6502.Immediate, 2, 2);
        Set(0xE4, Mnemonic6502.CPX, AddressingMode6502.ZeroPage, 2, 3);
        Set(0xEC, Mnemonic6502.CPX, AddressingMode6502.Absolute, 3, 4);
        Set(0xC0, Mnemonic6502.CPY, AddressingMode6502.Immediate, 2, 2);
        Set(0xC4, Mnemonic6502.CPY, AddressingMode6502.ZeroPage, 2, 3);
        Set(0xCC, Mnemonic6502.CPY, AddressingMode6502.Absolute, 3, 4);

        Set(0x24, Mnemonic6502.BIT, AddressingMode6502.ZeroPage, 2, 3);
        Set(0x2C, Mnemonic6502.BIT, AddressingMode6502.Absolute, 3, 4);
        Set(0x4C, Mnemonic6502.JMP, AddressingMode6502.Absolute, 3, 3);
        Set(0x6C, Mnemonic6502.JMP, AddressingMode6502.Indirect, 3, 5);
        Set(0x20, Mnemonic6502.JSR, AddressingMode6502.Absolute, 3, 6);

        Set(0x10, Mnemonic6502.BPL, AddressingMode6502.Relative, 2, 2, true);
        Set(0x30, Mnemonic6502.BMI, AddressingMode6502.Relative, 2, 2, true);
        Set(0x50, Mnemonic6502.BVC, AddressingMode6502.Relative, 2, 2, true);
        Set(0x70, Mnemonic6502.BVS, AddressingMode6502.Relative, 2, 2, true);
        Set(0x90, Mnemonic6502.BCC, AddressingMode6502.Relative, 2, 2, true);
        Set(0xB0, Mnemonic6502.BCS, AddressingMode6502.Relative, 2, 2, true);
        Set(0xD0, Mnemonic6502.BNE, AddressingMode6502.Relative, 2, 2, true);
        Set(0xF0, Mnemonic6502.BEQ, AddressingMode6502.Relative, 2, 2, true);

        Set(0x00, Mnemonic6502.BRK, AddressingMode6502.Implied, 1, 7);
        Set(0x40, Mnemonic6502.RTI, AddressingMode6502.Implied, 1, 6);
        Set(0x60, Mnemonic6502.RTS, AddressingMode6502.Implied, 1, 6);
        Set(0x08, Mnemonic6502.PHP, AddressingMode6502.Implied, 1, 3);
        Set(0x28, Mnemonic6502.PLP, AddressingMode6502.Implied, 1, 4);
        Set(0x48, Mnemonic6502.PHA, AddressingMode6502.Implied, 1, 3);
        Set(0x68, Mnemonic6502.PLA, AddressingMode6502.Implied, 1, 4);
        Set(0x18, Mnemonic6502.CLC, AddressingMode6502.Implied, 1, 2);
        Set(0x38, Mnemonic6502.SEC, AddressingMode6502.Implied, 1, 2);
        Set(0x58, Mnemonic6502.CLI, AddressingMode6502.Implied, 1, 2);
        Set(0x78, Mnemonic6502.SEI, AddressingMode6502.Implied, 1, 2);
        Set(0xB8, Mnemonic6502.CLV, AddressingMode6502.Implied, 1, 2);
        Set(0xD8, Mnemonic6502.CLD, AddressingMode6502.Implied, 1, 2);
        Set(0xF8, Mnemonic6502.SED, AddressingMode6502.Implied, 1, 2);
        Set(0x88, Mnemonic6502.DEY, AddressingMode6502.Implied, 1, 2);
        Set(0xC8, Mnemonic6502.INY, AddressingMode6502.Implied, 1, 2);
        Set(0xCA, Mnemonic6502.DEX, AddressingMode6502.Implied, 1, 2);
        Set(0xE8, Mnemonic6502.INX, AddressingMode6502.Implied, 1, 2);
        Set(0xAA, Mnemonic6502.TAX, AddressingMode6502.Implied, 1, 2);
        Set(0x8A, Mnemonic6502.TXA, AddressingMode6502.Implied, 1, 2);
        Set(0xA8, Mnemonic6502.TAY, AddressingMode6502.Implied, 1, 2);
        Set(0x98, Mnemonic6502.TYA, AddressingMode6502.Implied, 1, 2);
        Set(0xBA, Mnemonic6502.TSX, AddressingMode6502.Implied, 1, 2);
        Set(0x9A, Mnemonic6502.TXS, AddressingMode6502.Implied, 1, 2);
        Set(0xEA, Mnemonic6502.NOP, AddressingMode6502.Implied, 1, 2);
        return t;
    }
}
