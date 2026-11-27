// @module: reCart.Core.Ppu.Ppu2C02Renderer
// @purpose: Dot-stepped RP2C02 background/sprite shift registers, OAM evaluation, priority mux, and VRAM/CIRAM bus.
// @depends-on: System, System.IO, System.Runtime.InteropServices, reCart.Contracts
// @exports: Ppu2C02Renderer

using System.Runtime.InteropServices;
using reCart.Contracts;

namespace reCart.Core.Ppu;

/// <summary>
/// Implements dot-stepped <c>256x240</c> RP2C02 background and sprite pipelines,
/// CIRAM nametable mirroring, <c>A12</c> edge signaling, and sprite-0/overflow detection.
/// </summary>
internal sealed class Ppu2C02Renderer
{
    private readonly ICartMapper _mapper;
    private readonly Ppu2C02Registers _regs;
    private readonly bool _swapRedGreenEmphasis;

    public readonly byte[] Oam = new byte[256];
    public readonly byte[] PaletteRam = new byte[32];
    public readonly byte[] Nametables = new byte[4096];
    public readonly byte[] FrameBuffer = new byte[CartEngineConstants.FrameWidth * CartEngineConstants.FrameHeight * 4];

    private readonly byte[] _secOam = new byte[32];
    private readonly byte[] _sprPatternLo = new byte[8];
    private readonly byte[] _sprPatternHi = new byte[8];
    private readonly byte[] _sprAttr = new byte[8];
    private readonly byte[] _sprX = new byte[8];
    private int _secOamCount;
    private bool _sprite0OnLine;

    private ushort _bgShiftPatternLo;
    private ushort _bgShiftPatternHi;
    private ushort _bgShiftAttrLo;
    private ushort _bgShiftAttrHi;
    private byte _ntLatch;
    private byte _atLatch;
    private byte _ptLoLatch;
    private byte _ptHiLatch;

    public Ppu2C02Renderer(ICartMapper mapper, Ppu2C02Registers regs, bool swapRedGreenEmphasis)
    {
        _mapper = mapper;
        _regs = regs;
        _swapRedGreenEmphasis = swapRedGreenEmphasis;
        Array.Fill(Oam, (byte)0xFF);
        Array.Fill(_secOam, (byte)0xFF);
    }

    public void Reset()
    {
        Array.Fill(Oam, (byte)0xFF);
        Array.Clear(PaletteRam);
        Array.Clear(Nametables);
        Array.Clear(FrameBuffer);
        ClearSpriteScanlineState();
        _bgShiftPatternLo = 0;
        _bgShiftPatternHi = 0;
        _bgShiftAttrLo = 0;
        _bgShiftAttrHi = 0;
        _ntLatch = 0;
        _atLatch = 0;
        _ptLoLatch = 0;
        _ptHiLatch = 0;
    }

    public void ClearSpriteScanlineState()
    {
        Array.Fill(_secOam, (byte)0xFF);
        Array.Clear(_sprPatternLo);
        Array.Clear(_sprPatternHi);
        Array.Clear(_sprAttr);
        Array.Clear(_sprX);
        _secOamCount = 0;
        _sprite0OnLine = false;
    }

    public void NotifyAddressBus(ushort address, int scanline, int dot)
    {
        bool a12High = (address & 0x1000) != 0;
        if (a12High != _regs.LastA12High)
        {
            _regs.LastA12High = a12High;
            _mapper.OnPpuA12Edge(a12High, scanline, dot);
        }
    }

    public byte ReadPaletteRam(ushort address) => PaletteRam[Ppu2C02Palette.ResolvePaletteIndex(address)];

    public void WritePaletteRam(ushort address, byte value) =>
        PaletteRam[Ppu2C02Palette.ResolvePaletteIndex(address)] = (byte)(value & 0x3F);

    public byte ReadVram(ushort address, int scanline = 0, int dot = 0)
    {
        ushort masked = (ushort)(address & 0x3FFF);
        if (masked < 0x2000)
        {
            NotifyAddressBus(masked, scanline, dot);
            return _mapper.PpuRead(masked);
        }

        if (masked < 0x3F00)
        {
            NotifyAddressBus(masked, scanline, dot);
            return Nametables[ResolveNametableOffset(masked)];
        }

        return ReadPaletteRam(masked);
    }

    public void WriteVram(ushort address, byte value, int scanline = 0, int dot = 0)
    {
        ushort masked = (ushort)(address & 0x3FFF);
        if (masked < 0x2000)
        {
            NotifyAddressBus(masked, scanline, dot);
            _mapper.PpuWrite(masked, value);
            return;
        }

        if (masked < 0x3F00)
        {
            NotifyAddressBus(masked, scanline, dot);
            Nametables[ResolveNametableOffset(masked)] = value;
            return;
        }

        WritePaletteRam(masked, value);
    }

    public void StepBackgroundFetch(int scanline, int dot)
    {
        ShiftBackgroundRegisters();
        switch (dot & 7)
        {
            case 1:
                _ntLatch = ReadVram((ushort)(0x2000 | (_regs.V & 0x0FFF)), scanline, dot);
                break;
            case 3:
                ushort attrAddr = (ushort)(0x23C0 | (_regs.V & 0x0C00) | ((_regs.V >> 4) & 0x38) | ((_regs.V >> 2) & 0x07));
                byte attr = ReadVram(attrAddr, scanline, dot);
                int shift = ((_regs.V >> 4) & 4) | (_regs.V & 2);
                _atLatch = (byte)((attr >> shift) & 0x03);
                break;
            case 5:
                ushort loAddr = (ushort)(_regs.BgPatternTableBase | (_ntLatch << 4) | ((_regs.V >> 12) & 0x07));
                _ptLoLatch = ReadVram(loAddr, scanline, dot);
                break;
            case 7:
                ushort hiAddr = (ushort)(_regs.BgPatternTableBase | (_ntLatch << 4) | ((_regs.V >> 12) & 0x07) | 8);
                _ptHiLatch = ReadVram(hiAddr, scanline, dot);
                break;
            case 0:
                ReloadBackgroundShifters();
                _regs.IncrementCoarseX();
                break;
        }
    }

    public void EvaluateSpritesForNextScanline(int currentScanline)
    {
        int height = _regs.IsSprite8x16 ? 16 : 8;
        int found = 0;
        _sprite0OnLine = false;
        Array.Fill(_secOam, (byte)0xFF);

        for (int i = 0; i < 64; i++)
        {
            int oamBase = i * 4;
            int spriteY = Oam[oamBase];
            int diff = currentScanline - spriteY;
            if (diff >= 0 && diff < height)
            {
                if (found < 8)
                {
                    int secBase = found * 4;
                    _secOam[secBase] = Oam[oamBase];
                    _secOam[secBase + 1] = Oam[oamBase + 1];
                    _secOam[secBase + 2] = Oam[oamBase + 2];
                    _secOam[secBase + 3] = Oam[oamBase + 3];
                    if (i == 0) _sprite0OnLine = true;
                    found++;
                }
                else
                {
                    _regs.Status |= 0x20;
                    break;
                }
            }
        }

        _secOamCount = found;
    }

    public void StepSpriteFetch(int scanline, int dot)
    {
        int slot = (dot - 257) >> 3;
        int phase = (dot - 257) & 7;
        if (slot < 0 || slot >= 8) return;

        if (phase == 0)
        {
            ReadVram((ushort)(0x2000 | (_regs.V & 0x0FFF)), scanline, dot);
        }
        else if (phase == 2)
        {
            ReadVram((ushort)(0x23C0 | (_regs.V & 0x0C00)), scanline, dot);
        }
        else if (phase == 4 || phase == 6)
        {
            int secBase = slot * 4;
            ushort addr = ResolveSpritePatternAddress(scanline, slot, _secOam[secBase], _secOam[secBase + 1], _secOam[secBase + 2]);
            if (phase == 6) addr |= 8;
            byte data = ReadVram(addr, scanline, dot);
            if (slot < _secOamCount)
            {
                byte bits = (_secOam[secBase + 2] & 0x40) != 0 ? ReverseBits(data) : data;
                if (phase == 4) _sprPatternLo[slot] = bits;
                else _sprPatternHi[slot] = bits;
                _sprAttr[slot] = _secOam[secBase + 2];
                _sprX[slot] = _secOam[secBase + 3];
            }
            else if (phase == 4)
            {
                _sprPatternLo[slot] = 0;
                _sprPatternHi[slot] = 0;
            }
        }
    }

    public void RenderPixel(int scanline, int x)
    {
        byte bgPixel = 0, bgPal = 0;
        if (_regs.ShowBg && (x >= 8 || _regs.ShowBgLeft8))
        {
            int bit = 15 - _regs.FineX;
            bgPixel = (byte)((((_bgShiftPatternHi >> bit) & 1) << 1) | ((_bgShiftPatternLo >> bit) & 1));
            bgPal = (byte)((((_bgShiftAttrHi >> bit) & 1) << 1) | ((_bgShiftAttrLo >> bit) & 1));
        }

        byte sprPixel = 0, sprPal = 0;
        bool sprBehindBg = false, isSpr0 = false;
        if (_regs.ShowSprites && (x >= 8 || _regs.ShowSpritesLeft8))
        {
            for (int i = 0; i < _secOamCount; i++)
            {
                int offset = x - _sprX[i];
                if ((uint)offset < 8u)
                {
                    int bit = 7 - offset;
                    byte px = (byte)((((_sprPatternHi[i] >> bit) & 1) << 1) | ((_sprPatternLo[i] >> bit) & 1));
                    if (px != 0)
                    {
                        sprPixel = px;
                        sprPal = (byte)(_sprAttr[i] & 0x03);
                        sprBehindBg = (_sprAttr[i] & 0x20) != 0;
                        isSpr0 = i == 0 && _sprite0OnLine;
                        break;
                    }
                }
            }
        }

        byte colorAddr = ResolveMuxPaletteAddress(x, bgPixel, bgPal, sprPixel, sprPal, sprBehindBg, isSpr0);
        byte colorIdx = ReadPaletteRam((ushort)(0x3F00 | colorAddr));
        Span<uint> pixels = MemoryMarshal.Cast<byte, uint>(FrameBuffer.AsSpan());
        pixels[(scanline * CartEngineConstants.FrameWidth) + x] =
            Ppu2C02Palette.LookupPackedRgba(colorIdx, _regs.Mask, _swapRedGreenEmphasis);
    }

    private byte ResolveMuxPaletteAddress(int x, byte bgPixel, byte bgPal, byte sprPixel, byte sprPal, bool sprBehindBg, bool isSpr0)
    {
        if (!_regs.IsRenderingEnabled)
        {
            ushort v = (ushort)(_regs.V & 0x3FFF);
            return v >= 0x3F00 ? (byte)(v & 0x1F) : (byte)0;
        }

        if (bgPixel == 0 && sprPixel == 0) return 0;
        if (bgPixel == 0) return (byte)(0x10 | (sprPal << 2) | sprPixel);
        if (sprPixel == 0) return (byte)((bgPal << 2) | bgPixel);

        if (isSpr0 && x < 255 && _regs.ShowBg && _regs.ShowSprites && (x >= 8 || (_regs.ShowBgLeft8 && _regs.ShowSpritesLeft8)))
        {
            _regs.Status |= 0x40;
        }

        return sprBehindBg ? (byte)((bgPal << 2) | bgPixel) : (byte)(0x10 | (sprPal << 2) | sprPixel);
    }

    private ushort ResolveSpritePatternAddress(int scanline, int slot, byte spriteY, byte tile, byte attr)
    {
        if (slot >= _secOamCount)
        {
            ushort emptyTable = _regs.IsSprite8x16 ? (ushort)0x1000 : _regs.SpritePatternTableBase;
            return (ushort)(emptyTable | 0x0FF0);
        }

        int height = _regs.IsSprite8x16 ? 16 : 8;
        int row = scanline - spriteY;
        if ((attr & 0x80) != 0) row = (height - 1) - row;
        row = Math.Clamp(row, 0, height - 1);

        if (!_regs.IsSprite8x16)
        {
            return (ushort)(_regs.SpritePatternTableBase | (tile << 4) | row);
        }

        ushort table = (tile & 1) != 0 ? (ushort)0x1000 : (ushort)0x0000;
        int baseTile = tile & 0xFE;
        if (row >= 8)
        {
            baseTile++;
            row -= 8;
        }

        return (ushort)(table | (baseTile << 4) | row);
    }

    private void ShiftBackgroundRegisters()
    {
        _bgShiftPatternLo <<= 1;
        _bgShiftPatternHi <<= 1;
        _bgShiftAttrLo <<= 1;
        _bgShiftAttrHi <<= 1;
    }

    private void ReloadBackgroundShifters()
    {
        _bgShiftPatternLo = (ushort)((_bgShiftPatternLo & 0xFF00) | _ptLoLatch);
        _bgShiftPatternHi = (ushort)((_bgShiftPatternHi & 0xFF00) | _ptHiLatch);
        _bgShiftAttrLo = (ushort)((_bgShiftAttrLo & 0xFF00) | ((_atLatch & 0x01) != 0 ? 0xFF : 0x00));
        _bgShiftAttrHi = (ushort)((_bgShiftAttrHi & 0xFF00) | ((_atLatch & 0x02) != 0 ? 0xFF : 0x00));
    }

    private int ResolveNametableOffset(ushort address)
    {
        int nt = address & 0x0FFF;
        int table = nt >> 10;
        int offset = nt & 0x03FF;
        int bank = _mapper.Mirroring switch
        {
            CartMirroringMode.Horizontal => table >> 1,
            CartMirroringMode.Vertical => table & 1,
            CartMirroringMode.SingleScreenLower => 0,
            CartMirroringMode.SingleScreenUpper => 1,
            CartMirroringMode.FourScreen => table,
            _ => table >> 1
        };

        return (bank * 1024) + offset;
    }

    private static byte ReverseBits(byte b)
    {
        b = (byte)(((b & 0xF0) >> 4) | ((b & 0x0F) << 4));
        b = (byte)(((b & 0xCC) >> 2) | ((b & 0x33) << 2));
        return (byte)(((b & 0xAA) >> 1) | ((b & 0x55) << 1));
    }

    public void WriteState(BinaryWriter writer)
    {
        writer.Write(Oam);
        writer.Write(PaletteRam);
        writer.Write(Nametables);
        writer.Write(FrameBuffer);
        writer.Write(_secOam);
        writer.Write(_sprPatternLo);
        writer.Write(_sprPatternHi);
        writer.Write(_sprAttr);
        writer.Write(_sprX);
        writer.Write(_secOamCount);
        writer.Write(_sprite0OnLine);
        writer.Write(_bgShiftPatternLo);
        writer.Write(_bgShiftPatternHi);
        writer.Write(_bgShiftAttrLo);
        writer.Write(_bgShiftAttrHi);
        writer.Write(_ntLatch);
        writer.Write(_atLatch);
        writer.Write(_ptLoLatch);
        writer.Write(_ptHiLatch);
    }

    public void ReadState(BinaryReader reader)
    {
        reader.BaseStream.ReadExactly(Oam);
        reader.BaseStream.ReadExactly(PaletteRam);
        reader.BaseStream.ReadExactly(Nametables);
        reader.BaseStream.ReadExactly(FrameBuffer);
        reader.BaseStream.ReadExactly(_secOam);
        reader.BaseStream.ReadExactly(_sprPatternLo);
        reader.BaseStream.ReadExactly(_sprPatternHi);
        reader.BaseStream.ReadExactly(_sprAttr);
        reader.BaseStream.ReadExactly(_sprX);
        _secOamCount = reader.ReadInt32();
        _sprite0OnLine = reader.ReadBoolean();
        _bgShiftPatternLo = reader.ReadUInt16();
        _bgShiftPatternHi = reader.ReadUInt16();
        _bgShiftAttrLo = reader.ReadUInt16();
        _bgShiftAttrHi = reader.ReadUInt16();
        _ntLatch = reader.ReadByte();
        _atLatch = reader.ReadByte();
        _ptLoLatch = reader.ReadByte();
        _ptHiLatch = reader.ReadByte();
    }
}
