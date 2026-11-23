// @module: reCart.Core.Mappers.NromMapper
// @purpose: Mapper 0 (NROM-128 / NROM-256) fixed 16/32 KB PRG-ROM, 8 KB CHR, and optional PRG-RAM.
// @depends-on: System, reCart.Core.Rom
// @exports: NromMapper

using reCart.Core.Rom;

namespace reCart.Core.Mappers;

/// <summary>
/// Clean-room iNES Mapper <c>0</c> (<c>NROM</c>) implementation supporting 16 KB mirrored and 32 KB PRG-ROM boards.
/// </summary>
public sealed class NromMapper : CartMapperBase
{
    /// <summary>Initializes an <c>NROM</c> mapper from a loaded <see cref="InesRomImage"/>.</summary>
    public NromMapper(InesRomImage rom) : base(rom)
    {
        Reset();
    }

    /// <inheritdoc />
    public override string Name => "NROM";

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
        if (address is >= 0x6000 and < 0x8000)
        {
            WritePrgRam(address, value);
        }
    }

    /// <inheritdoc />
    public override void Reset()
    {
        Mirroring = InitialMirroring;
        ActivePrgBank = 0;
        ActiveChrBank = 0;
    }

    /// <inheritdoc />
    public override byte[] CaptureState() => EncodeState(ReadOnlySpan<byte>.Empty);

    /// <inheritdoc />
    public override void RestoreState(ReadOnlySpan<byte> state)
    {
        _ = DecodeState(state, expectedRegisterLength: 0);
    }
}
