// @module: reCart.Core.Mappers.CnRomMapper
// @purpose: Mapper 3 (CNROM) fixed 16/32 KB PRG-ROM with 8 KB switchable CHR-ROM banks.
// @depends-on: System, System.Buffers.Binary, reCart.Core.Rom
// @exports: CnRomMapper

using System.Buffers.Binary;
using reCart.Core.Rom;

namespace reCart.Core.Mappers;

/// <summary>
/// Clean-room iNES Mapper <c>3</c> (<c>CNROM</c>) implementation supporting 8 KB CHR bank switching.
/// </summary>
public sealed class CnRomMapper : CartMapperBase
{
    private readonly int _chrBankCount8K;
    private int _chrBank;

    /// <summary>Initializes a <c>CNROM</c> mapper from a loaded <see cref="InesRomImage"/>.</summary>
    public CnRomMapper(InesRomImage rom) : base(rom)
    {
        _chrBankCount8K = Math.Max(1, ChrMemory.Length / 8192);
        Reset();
    }

    /// <inheritdoc />
    public override string Name => "CNROM";

    /// <inheritdoc />
    public override byte CpuRead(ushort address)
    {
        if (address >= 0x8000)
        {
            int offset = PrgRom.Length <= 16384
                ? (address - 0x8000) & 0x3FFF
                : address - 0x8000;
            return PrgRom[offset % PrgRom.Length];
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
            byte effective = SubmapperId == 2 ? (byte)(value & CpuRead(address)) : value;
            _chrBank = effective % _chrBankCount8K;
            ActiveChrBank = (uint)_chrBank;
            return;
        }

        if (address >= 0x6000)
        {
            WritePrgRam(address, value);
        }
    }

    /// <inheritdoc />
    public override byte PpuRead(ushort address)
    {
        return ChrMemory[(_chrBank * 8192 + (address & 0x1FFF)) % ChrMemory.Length];
    }

    /// <inheritdoc />
    public override void PpuWrite(ushort address, byte value)
    {
        if (HasChrRam)
        {
            ChrMemory[(_chrBank * 8192 + (address & 0x1FFF)) % ChrMemory.Length] = value;
        }
    }

    /// <inheritdoc />
    public override void Reset()
    {
        Mirroring = InitialMirroring;
        _chrBank = 0;
        ActivePrgBank = 0;
        ActiveChrBank = 0;
    }

    /// <inheritdoc />
    public override byte[] CaptureState()
    {
        Span<byte> regs = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(regs, _chrBank);
        return EncodeState(regs);
    }

    /// <inheritdoc />
    public override void RestoreState(ReadOnlySpan<byte> state)
    {
        ReadOnlySpan<byte> regs = DecodeState(state, expectedRegisterLength: 4);
        _chrBank = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(regs)) % _chrBankCount8K;
        ActiveChrBank = (uint)_chrBank;
    }

    protected override void OnDirectChrBankSwitch(uint bank)
    {
        _chrBank = (int)(bank % (uint)_chrBankCount8K);
        ActiveChrBank = (uint)_chrBank;
    }
}
