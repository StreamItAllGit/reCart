// @module: reCart.Core.Cpu.Cpu2A03Addressing
// @purpose: Cycle-accurate 6502 addressing mode resolution, dummy reads, zero-page wrapping, and JMP ($xxFF) bug.
// @depends-on: System
// @exports: Cpu2A03Core (partial)

namespace reCart.Core.Cpu;

public sealed partial class Cpu2A03Core
{
    private byte FetchByte() => BusRead(PC++);

    private ushort FetchWord()
    {
        byte lo = FetchByte();
        byte hi = FetchByte();
        return (ushort)(lo | (hi << 8));
    }

    private ushort AddrZeroPage() => FetchByte();

    private ushort AddrZeroPageIndexed(byte index)
    {
        byte baseZp = FetchByte();
        BusRead(baseZp);
        return (byte)(baseZp + index);
    }

    private ushort AddrAbsolute() => FetchWord();

    private ushort AddrAbsoluteIndexedRead(byte index)
    {
        ushort baseAddr = FetchWord();
        ushort effective = (ushort)(baseAddr + index);
        if ((baseAddr & 0xFF00) != (effective & 0xFF00))
        {
            BusRead((ushort)((baseAddr & 0xFF00) | (effective & 0x00FF)));
        }

        return effective;
    }

    private ushort AddrAbsoluteIndexedWrite(byte index)
    {
        ushort baseAddr = FetchWord();
        ushort effective = (ushort)(baseAddr + index);
        BusRead((ushort)((baseAddr & 0xFF00) | (effective & 0x00FF)));
        return effective;
    }

    private ushort AddrIndirectX()
    {
        byte baseZp = FetchByte();
        BusRead(baseZp);
        byte ptr = (byte)(baseZp + X);
        byte lo = BusRead(ptr);
        byte hi = BusRead((byte)(ptr + 1));
        return (ushort)(lo | (hi << 8));
    }

    private ushort AddrIndirectYRead()
    {
        byte ptr = FetchByte();
        byte lo = BusRead(ptr);
        byte hi = BusRead((byte)(ptr + 1));
        ushort baseAddr = (ushort)(lo | (hi << 8));
        ushort effective = (ushort)(baseAddr + Y);
        if ((baseAddr & 0xFF00) != (effective & 0xFF00))
        {
            BusRead((ushort)((baseAddr & 0xFF00) | (effective & 0x00FF)));
        }

        return effective;
    }

    private ushort AddrIndirectYWrite()
    {
        byte ptr = FetchByte();
        byte lo = BusRead(ptr);
        byte hi = BusRead((byte)(ptr + 1));
        ushort baseAddr = (ushort)(lo | (hi << 8));
        ushort effective = (ushort)(baseAddr + Y);
        BusRead((ushort)((baseAddr & 0xFF00) | (effective & 0x00FF)));
        return effective;
    }

    private ushort AddrIndirectJmp()
    {
        ushort ptr = FetchWord();
        byte lo = BusRead(ptr);
        // Hardware 6502 page-wrap bug: JMP ($xxFF) reads high byte from $xx00 instead of $(xx+1)00.
        ushort hiPtr = (ushort)((ptr & 0xFF00) | ((ptr + 1) & 0x00FF));
        byte hi = BusRead(hiPtr);
        return (ushort)(lo | (hi << 8));
    }

    private void DummyImpliedRead() => BusRead(PC);
}
