// @module: reCart.Core.Ppu.Ppu2C02Registers
// @purpose: RP2C02 register latches, Loopy v/t/x/w scroll counters, and VBlank/NMI race state.
// @depends-on: System, System.IO
// @exports: Ppu2C02Registers

namespace reCart.Core.Ppu;

/// <summary>
/// Holds <c>RP2C02</c> control/mask/status latches, Loopy <c>v/t/x/w</c> scroll registers,
/// and VBlank/NMI race-suppression state.
/// </summary>
internal sealed class Ppu2C02Registers
{
    public byte Ctrl;
    public byte Mask;
    public byte Status;
    public byte OamAddr;
    public byte DataBuffer;
    public byte IoOpenBus;

    public ushort V;
    public ushort T;
    public byte FineX;
    public bool WriteLatch;

    public bool NmiPending;
    public bool NmiPreviousLine;
    public bool SuppressVBlankFlag;
    public bool SuppressNmi;
    public bool LastA12High;

    public bool NmiEnable => (Ctrl & 0x80) != 0;
    public bool IsSprite8x16 => (Ctrl & 0x20) != 0;
    public ushort BgPatternTableBase => (Ctrl & 0x10) != 0 ? (ushort)0x1000 : (ushort)0x0000;
    public ushort SpritePatternTableBase => (Ctrl & 0x08) != 0 ? (ushort)0x1000 : (ushort)0x0000;
    public ushort VramIncrement => (Ctrl & 0x04) != 0 ? (ushort)32 : (ushort)1;
    public bool IsRenderingEnabled => (Mask & 0x18) != 0;
    public bool ShowBg => (Mask & 0x08) != 0;
    public bool ShowSprites => (Mask & 0x10) != 0;
    public bool ShowBgLeft8 => (Mask & 0x02) != 0;
    public bool ShowSpritesLeft8 => (Mask & 0x04) != 0;

    public void Reset()
    {
        Ctrl = 0;
        Mask = 0;
        Status = 0;
        OamAddr = 0;
        DataBuffer = 0;
        IoOpenBus = 0;
        V = 0;
        T = 0;
        FineX = 0;
        WriteLatch = false;
        NmiPending = false;
        NmiPreviousLine = false;
        SuppressVBlankFlag = false;
        SuppressNmi = false;
        LastA12High = false;
    }

    public void IncrementCoarseX()
    {
        if ((V & 0x001F) == 31)
        {
            V &= unchecked((ushort)~0x001F);
            V ^= 0x0400;
        }
        else
        {
            V++;
        }
    }

    public void IncrementY()
    {
        if ((V & 0x7000) != 0x7000)
        {
            V += 0x1000;
            return;
        }

        V &= unchecked((ushort)~0x7000);
        int y = (V & 0x03E0) >> 5;
        if (y == 29)
        {
            y = 0;
            V ^= 0x0800;
        }
        else if (y == 31)
        {
            y = 0;
        }
        else
        {
            y++;
        }

        V = (ushort)((V & ~0x03E0) | (y << 5));
    }

    public void CopyHorizontal()
    {
        V = (ushort)((V & ~0x041F) | (T & 0x041F));
    }

    public void CopyVertical()
    {
        V = (ushort)((V & ~0x7BE0) | (T & 0x7BE0));
    }

    public void WriteState(BinaryWriter writer)
    {
        writer.Write(Ctrl);
        writer.Write(Mask);
        writer.Write(Status);
        writer.Write(OamAddr);
        writer.Write(DataBuffer);
        writer.Write(IoOpenBus);
        writer.Write(V);
        writer.Write(T);
        writer.Write(FineX);
        writer.Write(WriteLatch);
        writer.Write(NmiPending);
        writer.Write(NmiPreviousLine);
        writer.Write(SuppressVBlankFlag);
        writer.Write(SuppressNmi);
        writer.Write(LastA12High);
    }

    public void ReadState(BinaryReader reader)
    {
        Ctrl = reader.ReadByte();
        Mask = reader.ReadByte();
        Status = reader.ReadByte();
        OamAddr = reader.ReadByte();
        DataBuffer = reader.ReadByte();
        IoOpenBus = reader.ReadByte();
        V = reader.ReadUInt16();
        T = reader.ReadUInt16();
        FineX = reader.ReadByte();
        WriteLatch = reader.ReadBoolean();
        NmiPending = reader.ReadBoolean();
        NmiPreviousLine = reader.ReadBoolean();
        SuppressVBlankFlag = reader.ReadBoolean();
        SuppressNmi = reader.ReadBoolean();
        LastA12High = reader.ReadBoolean();
    }
}
