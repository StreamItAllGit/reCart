// @module: reCart.Core.Ppu.Ppu2C02Core
// @purpose: Dot-stepped RP2C02 PPU controller implementing ICartPpuController across NTSC, PAL, and Dendy timings.
// @depends-on: System, System.IO, System.Text, reCart.Contracts
// @exports: Ppu2C02Core

using System.Text;
using reCart.Contracts;

namespace reCart.Core.Ppu;

/// <summary>
/// Cycle-accurate dot-stepped <c>RP2C02</c> PPU rendering <c>256x240</c> RGBA8888 frames
/// against <see cref="CartRegionTimingProfile"/> and <see cref="ICartMapper"/>.
/// </summary>
public sealed class Ppu2C02Core : ICartPpuController
{
    private static readonly byte[] StateMagic = "PPU2C02\0"u8.ToArray();

    private readonly ICartMapper _mapper;
    private readonly CartRegionTimingProfile _timing;
    private readonly int _scanlinesPerFrame;
    private readonly int _preRenderScanline;
    private readonly int _dotsPerScanline;
    private readonly int _vblankStartScanline;
    private readonly bool _isNtsc;
    private readonly Ppu2C02Registers _regs = new();
    private readonly Ppu2C02Renderer _renderer;

    private int _scanline;
    private int _dot;
    private long _frameCount;

    /// <summary>Initializes a dot-stepped <see cref="Ppu2C02Core"/> bound to <paramref name="mapper"/> and <paramref name="timing"/>.</summary>
    public Ppu2C02Core(ICartMapper mapper, CartRegionTimingProfile? timing = null)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        _mapper = mapper;
        _timing = timing ?? CartRegionTimingProfile.Ntsc;
        _scanlinesPerFrame = _timing.ScanlinesPerFrame;
        _preRenderScanline = _scanlinesPerFrame - 1;
        _dotsPerScanline = _timing.DotsPerScanline;
        _vblankStartScanline = _timing.VBlankStartScanline;
        _isNtsc = _timing.Region == CartVideoRegionStandard.Ntsc;
        bool swapEmphasis = _timing.Region is CartVideoRegionStandard.Pal or CartVideoRegionStandard.Dendy;
        _renderer = new Ppu2C02Renderer(_mapper, _regs, swapEmphasis);
    }

    /// <inheritdoc />
    public int Scanline => _scanline;

    /// <inheritdoc />
    public int Dot => _dot;

    /// <inheritdoc />
    public long FrameCount => _frameCount;

    /// <inheritdoc />
    public ReadOnlySpan<byte> FrameBufferRgba => _renderer.FrameBuffer;

    /// <summary>Resets all PPU registers, scanline/dot counters, and internal VRAM/OAM state.</summary>
    public void Reset()
    {
        _scanline = 0;
        _dot = 0;
        _frameCount = 0;
        _regs.Reset();
        _renderer.Reset();
    }

    /// <inheritdoc />
    public bool StepDot()
    {
        bool isVisible = _scanline < CartEngineConstants.FrameHeight;
        bool isPreRender = _scanline == _preRenderScanline;

        if (isVisible || isPreRender)
        {
            StepRenderScanlineDot(isVisible, isPreRender);
        }
        else if (_scanline == _vblankStartScanline && _dot == 1)
        {
            EnterVBlankAtDotOne();
        }

        return AdvanceDotCursor();
    }

    /// <inheritdoc />
    public void StepScanline()
    {
        int startScanline = _scanline;
        while (_scanline == startScanline)
        {
            StepDot();
        }
    }

    /// <inheritdoc />
    public bool TakeNmiPending()
    {
        bool pending = _regs.NmiPending;
        _regs.NmiPending = false;
        return pending;
    }

    /// <inheritdoc />
    public void WriteOamByte(byte index, byte value) => _renderer.Oam[index] = value;

    /// <inheritdoc />
    public byte ReadRegister(ushort address)
    {
        int reg = address & 0x0007;
        return reg switch
        {
            2 => ReadPpuStatus(),
            4 => ReadOamData(),
            7 => ReadPpuData(),
            _ => _regs.IoOpenBus
        };
    }

    /// <inheritdoc />
    public void WriteRegister(ushort address, byte value)
    {
        _regs.IoOpenBus = value;
        switch (address & 0x0007)
        {
            case 0:
                WritePpuCtrl(value);
                break;
            case 1:
                _regs.Mask = value;
                break;
            case 3:
                _regs.OamAddr = value;
                break;
            case 4:
                _renderer.Oam[_regs.OamAddr] = value;
                _regs.OamAddr++;
                break;
            case 5:
                WritePpuScroll(value);
                break;
            case 6:
                WritePpuAddr(value);
                break;
            case 7:
                _renderer.WriteVram((ushort)(_regs.V & 0x3FFF), value, _scanline, _dot);
                _regs.V = (ushort)((_regs.V + _regs.VramIncrement) & 0x7FFF);
                _renderer.NotifyAddressBus((ushort)(_regs.V & 0x3FFF), _scanline, _dot);
                break;
        }
    }

    /// <inheritdoc />
    public byte[] CaptureState()
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms, Encoding.UTF8, leaveOpen: true);
        writer.Write(StateMagic);
        writer.Write(1);
        writer.Write(_scanline);
        writer.Write(_dot);
        writer.Write(_frameCount);
        _regs.WriteState(writer);
        _renderer.WriteState(writer);
        writer.Flush();
        return ms.ToArray();
    }

    /// <inheritdoc />
    public void RestoreState(ReadOnlySpan<byte> state)
    {
        if (state.Length < StateMagic.Length + 16 || !state[..StateMagic.Length].SequenceEqual(StateMagic))
        {
            throw new InvalidDataException("Invalid Ppu2C02Core snapshot header.");
        }

        using MemoryStream ms = new(state.ToArray());
        using BinaryReader reader = new(ms, Encoding.UTF8, leaveOpen: true);
        reader.BaseStream.Position = StateMagic.Length;
        int version = reader.ReadInt32();
        if (version != 1)
        {
            throw new InvalidDataException($"Unsupported Ppu2C02Core snapshot version {version}.");
        }

        _scanline = reader.ReadInt32();
        _dot = reader.ReadInt32();
        _frameCount = reader.ReadInt64();
        _regs.ReadState(reader);
        _renderer.ReadState(reader);
    }

    private void StepRenderScanlineDot(bool isVisible, bool isPreRender)
    {
        int dot = _dot;
        if (dot == 0) return;

        if (dot <= 256)
        {
            if (isVisible)
            {
                _renderer.RenderPixel(_scanline, dot - 1);
            }
            else if (dot == 1)
            {
                _regs.Status &= unchecked((byte)~0xE0);
                _regs.NmiPreviousLine = false;
                _regs.NmiPending = false;
                _regs.SuppressVBlankFlag = false;
                _regs.SuppressNmi = false;
                _renderer.ClearSpriteScanlineState();
            }

            if (!_regs.IsRenderingEnabled) return;
            _renderer.StepBackgroundFetch(_scanline, dot);
            if (dot == 256) _regs.IncrementY();
        }
        else if (dot == 257)
        {
            if (!_regs.IsRenderingEnabled) return;
            _regs.CopyHorizontal();
            if (isVisible) _renderer.EvaluateSpritesForNextScanline(_scanline);
        }
        else if (dot <= 320)
        {
            if (!_regs.IsRenderingEnabled) return;
            _renderer.StepSpriteFetch(_scanline, dot);
            if (isPreRender && dot >= 280 && dot <= 304) _regs.CopyVertical();
        }
        else if (dot <= 336)
        {
            if (!_regs.IsRenderingEnabled) return;
            _renderer.StepBackgroundFetch(_scanline, dot);
        }
    }

    private void EnterVBlankAtDotOne()
    {
        if (!_regs.SuppressVBlankFlag)
        {
            _regs.Status |= 0x80;
        }

        bool nmiLine = _regs.NmiEnable && (_regs.Status & 0x80) != 0;
        if (nmiLine && !_regs.NmiPreviousLine && !_regs.SuppressNmi)
        {
            _regs.NmiPending = true;
        }

        _regs.NmiPreviousLine = nmiLine;
        _regs.SuppressVBlankFlag = false;
        _regs.SuppressNmi = false;
    }

    private bool AdvanceDotCursor()
    {
        if (_dot >= _dotsPerScanline - 1 || IsOddNtscSkipDot())
        {
            _dot = 0;
            _scanline++;
            if (_scanline >= _scanlinesPerFrame)
            {
                _scanline = 0;
                _frameCount++;
                return true;
            }

            return false;
        }

        _dot++;
        return false;
    }

    private bool IsOddNtscSkipDot() =>
        _scanline == _preRenderScanline &&
        _dot == 339 &&
        (_frameCount & 1) == 1 &&
        _regs.IsRenderingEnabled &&
        _isNtsc;

    private byte ReadPpuStatus()
    {
        if (_scanline == _vblankStartScanline)
        {
            if (_dot == 0)
            {
                _regs.SuppressVBlankFlag = true;
                _regs.SuppressNmi = true;
            }
            else if (_dot is 1 or 2)
            {
                _regs.SuppressNmi = true;
                _regs.NmiPending = false;
            }
        }

        byte result = (byte)((_regs.Status & 0xE0) | (_regs.IoOpenBus & 0x1F));
        _regs.Status &= 0x7F;
        _regs.WriteLatch = false;
        _regs.NmiPreviousLine = false;
        _regs.IoOpenBus = result;
        return result;
    }

    private byte ReadOamData()
    {
        byte val = _renderer.Oam[_regs.OamAddr];
        if ((_regs.OamAddr & 0x03) == 2)
        {
            val &= 0xE3;
        }

        _regs.IoOpenBus = val;
        return val;
    }

    private byte ReadPpuData()
    {
        ushort addr = (ushort)(_regs.V & 0x3FFF);
        byte result;
        if (addr >= 0x3F00)
        {
            byte pal = _renderer.ReadPaletteRam(addr);
            if ((_regs.Mask & 0x01) != 0) pal &= 0x30;
            result = (byte)((pal & 0x3F) | (_regs.IoOpenBus & 0xC0));
            _regs.DataBuffer = _renderer.ReadVram((ushort)(addr & 0x2FFF), _scanline, _dot);
        }
        else
        {
            result = _regs.DataBuffer;
            _regs.DataBuffer = _renderer.ReadVram(addr, _scanline, _dot);
        }

        _regs.IoOpenBus = result;
        _regs.V = (ushort)((_regs.V + _regs.VramIncrement) & 0x7FFF);
        _renderer.NotifyAddressBus((ushort)(_regs.V & 0x3FFF), _scanline, _dot);
        return result;
    }

    private void WritePpuCtrl(byte value)
    {
        _regs.Ctrl = value;
        _regs.T = (ushort)((_regs.T & ~0x0C00) | ((value & 0x03) << 10));
        bool nmiLine = _regs.NmiEnable && (_regs.Status & 0x80) != 0;
        if (nmiLine && !_regs.NmiPreviousLine)
        {
            _regs.NmiPending = true;
        }
        else if (!nmiLine && _scanline == _vblankStartScanline && _dot <= 2)
        {
            _regs.NmiPending = false;
        }

        _regs.NmiPreviousLine = nmiLine;
    }

    private void WritePpuScroll(byte value)
    {
        if (!_regs.WriteLatch)
        {
            _regs.T = (ushort)((_regs.T & ~0x001F) | (value >> 3));
            _regs.FineX = (byte)(value & 0x07);
            _regs.WriteLatch = true;
        }
        else
        {
            _regs.T = (ushort)((_regs.T & ~0x73E0) | ((value & 0x07) << 12) | ((value & 0xF8) << 2));
            _regs.WriteLatch = false;
        }
    }

    private void WritePpuAddr(byte value)
    {
        if (!_regs.WriteLatch)
        {
            _regs.T = (ushort)((_regs.T & 0x00FF) | ((value & 0x3F) << 8));
            _regs.WriteLatch = true;
        }
        else
        {
            _regs.T = (ushort)((_regs.T & 0xFF00) | value);
            _regs.V = _regs.T;
            _regs.WriteLatch = false;
            _renderer.NotifyAddressBus((ushort)(_regs.V & 0x3FFF), _scanline, _dot);
        }
    }
}
