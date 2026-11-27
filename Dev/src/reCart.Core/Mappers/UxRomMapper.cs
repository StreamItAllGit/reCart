// @module: reCart.Core.Mappers.UxRomMapper
// @purpose: Mapper 2 (UxROM / UNROM / UOROM) 16 KB switchable + fixed last bank with PRG bus-conflict ANDing.
// @depends-on: System, System.Buffers.Binary, reCart.Core.Rom
// @exports: UxRomMapper

using System.Buffers.Binary;
using reCart.Core.Rom;

namespace reCart.Core.Mappers;

/// <summary>
/// Clean-room iNES Mapper <c>2</c> (<c>UxROM</c>) switching <c>$8000-$BFFF</c> with fixed last bank at <c>$C000-$FFFF</c>
/// and hardware bus-conflict ANDing (<c>value &amp; prgByte</c>, e.g. <c>$FFD0-$FFD7</c> bank tables) unless Submapper <c>1</c>.
/// </summary>
public sealed class UxRomMapper : CartMapperBase
{
    private readonly int _prgBankCount16K;
    private readonly int _prgByteMask;
    private readonly int _prgBankMask;
    private int _switchableBank;

    /// <summary>Initializes a <c>UxROM</c> mapper from a loaded <see cref="InesRomImage"/>.</summary>
    public UxRomMapper(InesRomImage rom) : base(rom)
    {
        _prgBankCount16K = Math.Max(1, PrgRom.Length / 16384);
        _prgByteMask = PrgRom.Length > 0 && (PrgRom.Length & (PrgRom.Length - 1)) == 0 ? PrgRom.Length - 1 : -1;
        _prgBankMask = _prgBankCount16K > 0 && (_prgBankCount16K & (_prgBankCount16K - 1)) == 0 ? _prgBankCount16K - 1 : -1;
        Reset();
    }

    /// <inheritdoc />
    public override string Name => "UxROM";

    /// <inheritdoc />
    public override byte CpuRead(ushort address)
    {
        if (address >= 0xC000)
        {
            int fixedBank = _prgBankCount16K - 1;
            int offset = (fixedBank << 14) | (address & 0x3FFF);
            return PrgRom[_prgByteMask >= 0 ? offset & _prgByteMask : offset % PrgRom.Length];
        }

        if (address >= 0x8000)
        {
            int offset = (_switchableBank << 14) | (address & 0x3FFF);
            return PrgRom[_prgByteMask >= 0 ? offset & _prgByteMask : offset % PrgRom.Length];
        }

        if (address >= 0x6000)
        {
            return ReadPrgRam(address);
        }

        return OpenBus(address);
    }

    /// <inheritdoc />
    public override void CpuWrite(ushort address, byte value)
    {
        if (address >= 0x8000)
        {
            byte effective = SubmapperId == 1 ? value : (byte)(value & CpuRead(address));
            _switchableBank = _prgBankMask >= 0 ? effective & _prgBankMask : effective % _prgBankCount16K;
            ActivePrgBank = (uint)_switchableBank;
            return;
        }

        if (address >= 0x6000)
        {
            WritePrgRam(address, value);
        }
    }

    /// <inheritdoc />
    public override void Reset()
    {
        Mirroring = InitialMirroring;
        _switchableBank = 0;
        ActivePrgBank = 0;
        ActiveChrBank = 0;
    }

    /// <inheritdoc />
    public override byte[] CaptureState()
    {
        Span<byte> regs = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(regs, _switchableBank);
        return EncodeState(regs);
    }

    /// <inheritdoc />
    public override void RestoreState(ReadOnlySpan<byte> state)
    {
        ReadOnlySpan<byte> regs = DecodeState(state, expectedRegisterLength: 4);
        _switchableBank = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(regs)) % _prgBankCount16K;
        ActivePrgBank = (uint)_switchableBank;
    }

    protected override void OnDirectPrgBankSwitch(uint bank)
    {
        _switchableBank = (int)(bank % (uint)_prgBankCount16K);
        ActivePrgBank = (uint)_switchableBank;
    }
}
