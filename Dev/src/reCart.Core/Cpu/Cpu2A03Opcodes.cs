// @module: reCart.Core.Cpu.Cpu2A03Opcodes
// @purpose: Complete 56-mnemonic / 151-opcode official RP2A03 6502 instruction decoder and ALU/RMW/branch execution.
// @depends-on: System
// @exports: Cpu2A03Core (partial)

namespace reCart.Core.Cpu;

public sealed partial class Cpu2A03Core
{
    private void ExecuteOpcode(byte opcode)
    {
        switch (opcode)
        {
            // Load / Store
            case 0xA9: A = SetZeroNegative(FetchByte()); break;
            case 0xA5: A = SetZeroNegative(BusRead(AddrZeroPage())); break;
            case 0xB5: A = SetZeroNegative(BusRead(AddrZeroPageIndexed(X))); break;
            case 0xAD: A = SetZeroNegative(BusRead(AddrAbsolute())); break;
            case 0xBD: A = SetZeroNegative(BusRead(AddrAbsoluteIndexedRead(X))); break;
            case 0xB9: A = SetZeroNegative(BusRead(AddrAbsoluteIndexedRead(Y))); break;
            case 0xA1: A = SetZeroNegative(BusRead(AddrIndirectX())); break;
            case 0xB1: A = SetZeroNegative(BusRead(AddrIndirectYRead())); break;

            case 0xA2: X = SetZeroNegative(FetchByte()); break;
            case 0xA6: X = SetZeroNegative(BusRead(AddrZeroPage())); break;
            case 0xB6: X = SetZeroNegative(BusRead(AddrZeroPageIndexed(Y))); break;
            case 0xAE: X = SetZeroNegative(BusRead(AddrAbsolute())); break;
            case 0xBE: X = SetZeroNegative(BusRead(AddrAbsoluteIndexedRead(Y))); break;

            case 0xA0: Y = SetZeroNegative(FetchByte()); break;
            case 0xA4: Y = SetZeroNegative(BusRead(AddrZeroPage())); break;
            case 0xB4: Y = SetZeroNegative(BusRead(AddrZeroPageIndexed(X))); break;
            case 0xAC: Y = SetZeroNegative(BusRead(AddrAbsolute())); break;
            case 0xBC: Y = SetZeroNegative(BusRead(AddrAbsoluteIndexedRead(X))); break;

            case 0x85: BusWrite(AddrZeroPage(), A); break;
            case 0x95: BusWrite(AddrZeroPageIndexed(X), A); break;
            case 0x8D: BusWrite(AddrAbsolute(), A); break;
            case 0x9D: BusWrite(AddrAbsoluteIndexedWrite(X), A); break;
            case 0x99: BusWrite(AddrAbsoluteIndexedWrite(Y), A); break;
            case 0x81: BusWrite(AddrIndirectX(), A); break;
            case 0x91: BusWrite(AddrIndirectYWrite(), A); break;

            case 0x86: BusWrite(AddrZeroPage(), X); break;
            case 0x96: BusWrite(AddrZeroPageIndexed(Y), X); break;
            case 0x8E: BusWrite(AddrAbsolute(), X); break;

            case 0x84: BusWrite(AddrZeroPage(), Y); break;
            case 0x94: BusWrite(AddrZeroPageIndexed(X), Y); break;
            case 0x8C: BusWrite(AddrAbsolute(), Y); break;

            // Transfers & Stack
            case 0xAA: DummyImpliedRead(); X = SetZeroNegative(A); break;
            case 0xA8: DummyImpliedRead(); Y = SetZeroNegative(A); break;
            case 0x8A: DummyImpliedRead(); A = SetZeroNegative(X); break;
            case 0x98: DummyImpliedRead(); A = SetZeroNegative(Y); break;
            case 0xBA: DummyImpliedRead(); X = SetZeroNegative(SP); break;
            case 0x9A: DummyImpliedRead(); SP = X; break;

            case 0x48: DummyImpliedRead(); Push(A); break;
            case 0x08: DummyImpliedRead(); Push((byte)(P | FlagB | FlagU)); break;
            case 0x68: DummyImpliedRead(); TickInternalCycle(); A = SetZeroNegative(Pop()); break;
            case 0x28: DummyImpliedRead(); TickInternalCycle(); P = (byte)((Pop() & ~FlagB) | FlagU); break;

            // Logical & BIT
            case 0x29: OpAnd(FetchByte()); break;
            case 0x25: OpAnd(BusRead(AddrZeroPage())); break;
            case 0x35: OpAnd(BusRead(AddrZeroPageIndexed(X))); break;
            case 0x2D: OpAnd(BusRead(AddrAbsolute())); break;
            case 0x3D: OpAnd(BusRead(AddrAbsoluteIndexedRead(X))); break;
            case 0x39: OpAnd(BusRead(AddrAbsoluteIndexedRead(Y))); break;
            case 0x21: OpAnd(BusRead(AddrIndirectX())); break;
            case 0x31: OpAnd(BusRead(AddrIndirectYRead())); break;

            case 0x49: OpEor(FetchByte()); break;
            case 0x45: OpEor(BusRead(AddrZeroPage())); break;
            case 0x55: OpEor(BusRead(AddrZeroPageIndexed(X))); break;
            case 0x4D: OpEor(BusRead(AddrAbsolute())); break;
            case 0x5D: OpEor(BusRead(AddrAbsoluteIndexedRead(X))); break;
            case 0x59: OpEor(BusRead(AddrAbsoluteIndexedRead(Y))); break;
            case 0x41: OpEor(BusRead(AddrIndirectX())); break;
            case 0x51: OpEor(BusRead(AddrIndirectYRead())); break;

            case 0x09: OpOra(FetchByte()); break;
            case 0x05: OpOra(BusRead(AddrZeroPage())); break;
            case 0x15: OpOra(BusRead(AddrZeroPageIndexed(X))); break;
            case 0x0D: OpOra(BusRead(AddrAbsolute())); break;
            case 0x1D: OpOra(BusRead(AddrAbsoluteIndexedRead(X))); break;
            case 0x19: OpOra(BusRead(AddrAbsoluteIndexedRead(Y))); break;
            case 0x01: OpOra(BusRead(AddrIndirectX())); break;
            case 0x11: OpOra(BusRead(AddrIndirectYRead())); break;

            case 0x24: OpBit(BusRead(AddrZeroPage())); break;
            case 0x2C: OpBit(BusRead(AddrAbsolute())); break;

            // Arithmetic (2A03 lacks BCD decimal mode: ADC/SBC always compute pure binary)
            case 0x69: OpAdc(FetchByte()); break;
            case 0x65: OpAdc(BusRead(AddrZeroPage())); break;
            case 0x75: OpAdc(BusRead(AddrZeroPageIndexed(X))); break;
            case 0x6D: OpAdc(BusRead(AddrAbsolute())); break;
            case 0x7D: OpAdc(BusRead(AddrAbsoluteIndexedRead(X))); break;
            case 0x79: OpAdc(BusRead(AddrAbsoluteIndexedRead(Y))); break;
            case 0x61: OpAdc(BusRead(AddrIndirectX())); break;
            case 0x71: OpAdc(BusRead(AddrIndirectYRead())); break;

            case 0xE9: OpAdc((byte)~FetchByte()); break;
            case 0xE5: OpAdc((byte)~BusRead(AddrZeroPage())); break;
            case 0xF5: OpAdc((byte)~BusRead(AddrZeroPageIndexed(X))); break;
            case 0xED: OpAdc((byte)~BusRead(AddrAbsolute())); break;
            case 0xFD: OpAdc((byte)~BusRead(AddrAbsoluteIndexedRead(X))); break;
            case 0xF9: OpAdc((byte)~BusRead(AddrAbsoluteIndexedRead(Y))); break;
            case 0xE1: OpAdc((byte)~BusRead(AddrIndirectX())); break;
            case 0xF1: OpAdc((byte)~BusRead(AddrIndirectYRead())); break;

            // Compare
            case 0xC9: OpCompare(A, FetchByte()); break;
            case 0xC5: OpCompare(A, BusRead(AddrZeroPage())); break;
            case 0xD5: OpCompare(A, BusRead(AddrZeroPageIndexed(X))); break;
            case 0xCD: OpCompare(A, BusRead(AddrAbsolute())); break;
            case 0xDD: OpCompare(A, BusRead(AddrAbsoluteIndexedRead(X))); break;
            case 0xD9: OpCompare(A, BusRead(AddrAbsoluteIndexedRead(Y))); break;
            case 0xC1: OpCompare(A, BusRead(AddrIndirectX())); break;
            case 0xD1: OpCompare(A, BusRead(AddrIndirectYRead())); break;

            case 0xE0: OpCompare(X, FetchByte()); break;
            case 0xE4: OpCompare(X, BusRead(AddrZeroPage())); break;
            case 0xEC: OpCompare(X, BusRead(AddrAbsolute())); break;

            case 0xC0: OpCompare(Y, FetchByte()); break;
            case 0xC4: OpCompare(Y, BusRead(AddrZeroPage())); break;
            case 0xCC: OpCompare(Y, BusRead(AddrAbsolute())); break;

            // Increment / Decrement
            case 0xE6: OpRmw(AddrZeroPage(), RmwOp.Inc); break;
            case 0xF6: OpRmw(AddrZeroPageIndexed(X), RmwOp.Inc); break;
            case 0xEE: OpRmw(AddrAbsolute(), RmwOp.Inc); break;
            case 0xFE: OpRmw(AddrAbsoluteIndexedWrite(X), RmwOp.Inc); break;

            case 0xC6: OpRmw(AddrZeroPage(), RmwOp.Dec); break;
            case 0xD6: OpRmw(AddrZeroPageIndexed(X), RmwOp.Dec); break;
            case 0xCE: OpRmw(AddrAbsolute(), RmwOp.Dec); break;
            case 0xDE: OpRmw(AddrAbsoluteIndexedWrite(X), RmwOp.Dec); break;

            case 0xE8: DummyImpliedRead(); X = SetZeroNegative((byte)(X + 1)); break;
            case 0xC8: DummyImpliedRead(); Y = SetZeroNegative((byte)(Y + 1)); break;
            case 0xCA: DummyImpliedRead(); X = SetZeroNegative((byte)(X - 1)); break;
            case 0x88: DummyImpliedRead(); Y = SetZeroNegative((byte)(Y - 1)); break;

            // Shifts / Rotates
            case 0x0A: DummyImpliedRead(); A = OpAsl(A); break;
            case 0x06: OpRmw(AddrZeroPage(), RmwOp.Asl); break;
            case 0x16: OpRmw(AddrZeroPageIndexed(X), RmwOp.Asl); break;
            case 0x0E: OpRmw(AddrAbsolute(), RmwOp.Asl); break;
            case 0x1E: OpRmw(AddrAbsoluteIndexedWrite(X), RmwOp.Asl); break;

            case 0x4A: DummyImpliedRead(); A = OpLsr(A); break;
            case 0x46: OpRmw(AddrZeroPage(), RmwOp.Lsr); break;
            case 0x56: OpRmw(AddrZeroPageIndexed(X), RmwOp.Lsr); break;
            case 0x4E: OpRmw(AddrAbsolute(), RmwOp.Lsr); break;
            case 0x5E: OpRmw(AddrAbsoluteIndexedWrite(X), RmwOp.Lsr); break;

            case 0x2A: DummyImpliedRead(); A = OpRol(A); break;
            case 0x26: OpRmw(AddrZeroPage(), RmwOp.Rol); break;
            case 0x36: OpRmw(AddrZeroPageIndexed(X), RmwOp.Rol); break;
            case 0x2E: OpRmw(AddrAbsolute(), RmwOp.Rol); break;
            case 0x3E: OpRmw(AddrAbsoluteIndexedWrite(X), RmwOp.Rol); break;

            case 0x6A: DummyImpliedRead(); A = OpRor(A); break;
            case 0x66: OpRmw(AddrZeroPage(), RmwOp.Ror); break;
            case 0x76: OpRmw(AddrZeroPageIndexed(X), RmwOp.Ror); break;
            case 0x6E: OpRmw(AddrAbsolute(), RmwOp.Ror); break;
            case 0x7E: OpRmw(AddrAbsoluteIndexedWrite(X), RmwOp.Ror); break;

            // Jumps, Subroutines, Branches, Interrupts, Flags, NOP
            case 0x4C: PC = FetchWord(); break;
            case 0x6C: PC = AddrIndirectJmp(); break;
            case 0x20: OpJsr(); break;
            case 0x60: OpRts(); break;
            case 0x40: OpRti(); break;
            case 0x00: OpBrk(); break;

            case 0x90: OpBranch(!GetFlag(FlagC)); break;
            case 0xB0: OpBranch(GetFlag(FlagC)); break;
            case 0xF0: OpBranch(GetFlag(FlagZ)); break;
            case 0xD0: OpBranch(!GetFlag(FlagZ)); break;
            case 0x30: OpBranch(GetFlag(FlagN)); break;
            case 0x10: OpBranch(!GetFlag(FlagN)); break;
            case 0x70: OpBranch(GetFlag(FlagV)); break;
            case 0x50: OpBranch(!GetFlag(FlagV)); break;

            case 0x18: DummyImpliedRead(); SetFlag(FlagC, false); break;
            case 0xD8: DummyImpliedRead(); SetFlag(FlagD, false); break;
            case 0x58: DummyImpliedRead(); SetFlag(FlagI, false); break;
            case 0xB8: DummyImpliedRead(); SetFlag(FlagV, false); break;
            case 0x38: DummyImpliedRead(); SetFlag(FlagC, true); break;
            case 0xF8: DummyImpliedRead(); SetFlag(FlagD, true); break;
            case 0x78: DummyImpliedRead(); SetFlag(FlagI, true); break;
            case 0xEA: DummyImpliedRead(); break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported 6502 opcode 0x{opcode:X2} at PC=0x{(ushort)(PC - 1):X4}.");
        }
    }

    private void OpAnd(byte value) => A = SetZeroNegative((byte)(A & value));
    private void OpEor(byte value) => A = SetZeroNegative((byte)(A ^ value));
    private void OpOra(byte value) => A = SetZeroNegative((byte)(A | value));

    private void OpBit(byte value)
    {
        SetFlag(FlagZ, (A & value) == 0);
        SetFlag(FlagV, (value & 0x40) != 0);
        SetFlag(FlagN, (value & 0x80) != 0);
    }

    private void OpAdc(byte value)
    {
        int sum = A + value + (GetFlag(FlagC) ? 1 : 0);
        SetFlag(FlagC, sum > 0xFF);
        SetFlag(FlagV, (~(A ^ value) & (A ^ sum) & 0x80) != 0);
        A = SetZeroNegative((byte)sum);
    }

    private void OpCompare(byte reg, byte value)
    {
        SetFlag(FlagC, reg >= value);
        SetZeroNegative((byte)(reg - value));
    }

    private enum RmwOp : byte { Inc, Dec, Asl, Lsr, Rol, Ror }

    private void OpRmw(ushort address, RmwOp op)
    {
        byte original = BusRead(address);
        BusWrite(address, original);
        byte updated = op switch
        {
            RmwOp.Inc => SetZeroNegative((byte)(original + 1)),
            RmwOp.Dec => SetZeroNegative((byte)(original - 1)),
            RmwOp.Asl => OpAsl(original),
            RmwOp.Lsr => OpLsr(original),
            RmwOp.Rol => OpRol(original),
            _ => OpRor(original)
        };
        BusWrite(address, updated);
    }

    private byte OpAsl(byte value)
    {
        SetFlag(FlagC, (value & 0x80) != 0);
        return SetZeroNegative((byte)(value << 1));
    }

    private byte OpLsr(byte value)
    {
        SetFlag(FlagC, (value & 0x01) != 0);
        return SetZeroNegative((byte)(value >> 1));
    }

    private byte OpRol(byte value)
    {
        byte carryIn = GetFlag(FlagC) ? (byte)0x01 : (byte)0x00;
        SetFlag(FlagC, (value & 0x80) != 0);
        return SetZeroNegative((byte)((value << 1) | carryIn));
    }

    private byte OpRor(byte value)
    {
        byte carryIn = GetFlag(FlagC) ? (byte)0x80 : (byte)0x00;
        SetFlag(FlagC, (value & 0x01) != 0);
        return SetZeroNegative((byte)((value >> 1) | carryIn));
    }

    private void OpBranch(bool condition)
    {
        sbyte offset = (sbyte)FetchByte();
        if (!condition)
        {
            return;
        }

        BusRead(PC);
        ushort target = (ushort)(PC + offset);
        if ((PC & 0xFF00) != (target & 0xFF00))
        {
            BusRead((ushort)((PC & 0xFF00) | (target & 0x00FF)));
        }

        PC = target;
    }

    private void OpJsr()
    {
        byte lo = FetchByte();
        BusRead((ushort)(0x0100 | SP));
        Push16(PC);
        byte hi = BusRead(PC);
        PC = (ushort)(lo | (hi << 8));
    }

    private void OpRts()
    {
        DummyImpliedRead();
        BusRead((ushort)(0x0100 | SP));
        PC = (ushort)(Pop16() + 1);
        TickInternalCycle();
    }

    private void OpRti()
    {
        DummyImpliedRead();
        BusRead((ushort)(0x0100 | SP));
        P = (byte)((Pop() & ~FlagB) | FlagU);
        PC = Pop16();
    }

    private void OpBrk()
    {
        FetchByte();
        Push16(PC);
        Push((byte)(P | FlagB | FlagU));
        SetFlag(FlagI, true);
        byte lo = BusRead(IrqVector);
        byte hi = BusRead((ushort)(IrqVector + 1));
        PC = (ushort)(lo | (hi << 8));
    }
}
