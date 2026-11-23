// @module: reCart.Contracts.CartRegionAndMapperContracts
// @purpose: Region timing profiles, numeric iNES 1.0 / NES 2.0 header parser, and extensible ICartMapper interface.
// @depends-on: System
// @exports: CartVideoRegionStandard, CartVideoRegionCodec, CartRegionTimingProfile, CartMirroringMode, CartRomHeader, CartRomHeaderParser, ICartMapper

namespace reCart.Contracts;

/// <summary>
/// Target video region &amp; clock standard (Dev Bible Standard 18: <c>videoRegionStandard</c>).
/// </summary>
public enum CartVideoRegionStandard
{
    /// <summary>Auto-detect from NES 2.0 header / manifest metadata (defaults to NTSC when unspecified).</summary>
    Auto = 0,

    /// <summary>60.0988 Hz RP2A03G/RP2C02G NTSC timing (262 scanlines).</summary>
    Ntsc = 1,

    /// <summary>50.0070 Hz RP2A07/RP2C07 PAL timing (312 scanlines).</summary>
    Pal = 2,

    /// <summary>50.0070 Hz UMC UA6538 Dendy timing (312 scanlines with 240-NMI split).</summary>
    Dendy = 3
}

/// <summary>
/// Normalizes and formats <see cref="CartVideoRegionStandard"/> tokens cleanly.
/// </summary>
public static class CartVideoRegionCodec
{
    /// <summary>Parses a region string token, defaulting to <see cref="CartVideoRegionStandard.Auto"/>.</summary>
    /// <param name="raw">Raw CLI or JSON region token.</param>
    /// <returns>Normalized <see cref="CartVideoRegionStandard"/>.</returns>
    public static CartVideoRegionStandard Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return CartVideoRegionStandard.Auto;
        return raw.Trim().ToLowerInvariant() switch
        {
            "ntsc" => CartVideoRegionStandard.Ntsc,
            "pal" => CartVideoRegionStandard.Pal,
            "dendy" => CartVideoRegionStandard.Dendy,
            _ => CartVideoRegionStandard.Auto
        };
    }

    /// <summary>Converts <paramref name="region"/> to its canonical camelCase JSON string.</summary>
    /// <param name="region">Region standard enum.</param>
    /// <returns>Canonical region token.</returns>
    public static string ToCanonicalId(CartVideoRegionStandard region) => region switch
    {
        CartVideoRegionStandard.Ntsc => "ntsc",
        CartVideoRegionStandard.Pal => "pal",
        CartVideoRegionStandard.Dendy => "dendy",
        _ => "auto"
    };
}

/// <summary>
/// Clock rates, scanline counts, and pixel aspect geometry for a resolved video standard.
/// </summary>
/// <param name="Name">Human-readable timing profile label.</param>
/// <param name="Region">Resolved concrete <see cref="CartVideoRegionStandard"/>.</param>
/// <param name="ScanlinesPerFrame">Total scanlines per frame (<c>262</c> NTSC, <c>312</c> PAL/Dendy).</param>
/// <param name="VBlankStartScanline">First VBlank NMI scanline (<c>241</c> NTSC/PAL, <c>291</c> Dendy).</param>
/// <param name="DotsPerScanline">PPU dots per scanline (<c>341</c>).</param>
/// <param name="CpuClockHz">CPU master frequency in Hz.</param>
/// <param name="FrameRateHz">Nominal frame cadence in Hz.</param>
/// <param name="DefaultPixelAspectRatio">Hardware pixel aspect ratio (<c>8/7</c> NTSC, <c>1.3862</c> PAL).</param>
public sealed record CartRegionTimingProfile(
    string Name,
    CartVideoRegionStandard Region,
    int ScanlinesPerFrame,
    int VBlankStartScanline,
    int DotsPerScanline,
    double CpuClockHz,
    double FrameRateHz,
    double DefaultPixelAspectRatio)
{
    /// <summary>Canonical RP2A03/RP2C02 NTSC timing profile.</summary>
    public static CartRegionTimingProfile Ntsc { get; } = new("NTSC", CartVideoRegionStandard.Ntsc, 262, 241, 341, 1_789_772.727, 60.0988, 8.0 / 7.0);

    /// <summary>Canonical RP2A07/RP2C07 PAL timing profile (<c>v0.2.0+</c> activation).</summary>
    public static CartRegionTimingProfile Pal { get; } = new("PAL", CartVideoRegionStandard.Pal, 312, 241, 341, 1_662_607.0, 50.0070, 1.3862);

    /// <summary>Canonical Dendy hybrid timing profile (<c>v0.2.0+</c> activation).</summary>
    public static CartRegionTimingProfile Dendy { get; } = new("Dendy", CartVideoRegionStandard.Dendy, 312, 291, 341, 1_773_447.0, 50.0070, 8.0 / 7.0);

    /// <summary>Resolves the timing profile for <paramref name="region"/> (<c>Auto</c> resolves to <see cref="Ntsc"/> in v0.1.0).</summary>
    /// <param name="region">Requested or header-detected region standard.</param>
    /// <returns>Concrete timing profile.</returns>
    public static CartRegionTimingProfile ForRegion(CartVideoRegionStandard region) => region switch
    {
        CartVideoRegionStandard.Pal => Pal,
        CartVideoRegionStandard.Dendy => Dendy,
        _ => Ntsc
    };
}

/// <summary>
/// Nametable mirroring configuration controlled by cartridge wiring or mapper registers.
/// </summary>
public enum CartMirroringMode
{
    /// <summary>Vertical arrangement / Horizontal mirroring (<c>CIRAM A10 = PPU A11</c>).</summary>
    Horizontal = 0,

    /// <summary>Horizontal arrangement / Vertical mirroring (<c>CIRAM A10 = PPU A10</c>).</summary>
    Vertical = 1,

    /// <summary>Single-screen lower nametable bank (<c>CIRAM A10 = 0</c>).</summary>
    SingleScreenLower = 2,

    /// <summary>Single-screen upper nametable bank (<c>CIRAM A10 = 1</c>).</summary>
    SingleScreenUpper = 3,

    /// <summary>Four-screen cartridge-provided extended nametable RAM.</summary>
    FourScreen = 4
}

/// <summary>
/// Parsed iNES 1.0 or NES 2.0 header geometry using extensible numeric mapper and submapper IDs.
/// </summary>
/// <param name="MapperId">Numeric 12-bit iNES/NES 2.0 mapper identifier (<c>0..4095</c>).</param>
/// <param name="SubmapperId">Numeric NES 2.0 submapper identifier (<c>0..15</c>).</param>
/// <param name="PrgRomSizeBytes">Total PRG-ROM payload length in bytes.</param>
/// <param name="ChrRomSizeBytes">Total CHR-ROM payload length in bytes (<c>0</c> selects CHR-RAM).</param>
/// <param name="PrgRamSizeBytes">Volatile or battery-backed PRG-RAM size in bytes.</param>
/// <param name="ChrRamSizeBytes">CHR-RAM size in bytes when <see cref="ChrRomSizeBytes"/> is zero.</param>
/// <param name="HasTrainer">Whether a 512-byte legacy trainer block precedes PRG-ROM.</param>
/// <param name="HasBatteryBackedRam">Whether battery-backed persistent PRG-RAM is declared.</param>
/// <param name="IsNes20">Whether the header carries a valid NES 2.0 signature.</param>
/// <param name="Mirroring">Initial hardware nametable mirroring arrangement.</param>
/// <param name="VideoRegionStandard">Detected video timing standard declared in the header.</param>
public sealed record CartRomHeader(
    ushort MapperId,
    byte SubmapperId,
    int PrgRomSizeBytes,
    int ChrRomSizeBytes,
    int PrgRamSizeBytes,
    int ChrRamSizeBytes,
    bool HasTrainer,
    bool HasBatteryBackedRam,
    bool IsNes20,
    CartMirroringMode Mirroring,
    CartVideoRegionStandard VideoRegionStandard);

/// <summary>
/// Clean-room iNES 1.0 and NES 2.0 16-byte header parser shared across compiler, core, decompiler, and player.
/// </summary>
public static class CartRomHeaderParser
{
    /// <summary>Size in bytes of a standard iNES / NES 2.0 cartridge header.</summary>
    public const int HeaderSizeBytes = 16;

    /// <summary>Size in bytes of an optional legacy iNES trainer preceding PRG-ROM.</summary>
    public const int TrainerSizeBytes = 512;

    /// <summary>Parses a 16-byte iNES 1.0 or NES 2.0 header from <paramref name="romBytes"/>.</summary>
    /// <param name="romBytes">Complete cartridge container buffer starting with <c>NES\x1A</c>.</param>
    /// <returns>Parsed <see cref="CartRomHeader"/>.</returns>
    public static CartRomHeader Parse(ReadOnlySpan<byte> romBytes)
    {
        if (romBytes.Length < HeaderSizeBytes)
        {
            throw new InvalidDataException($"Cartridge image is shorter than {HeaderSizeBytes}-byte iNES header.");
        }
        if (romBytes[0] != 0x4E || romBytes[1] != 0x45 || romBytes[2] != 0x53 || romBytes[3] != 0x1A)
        {
            throw new InvalidDataException("Missing iNES 'NES\\x1A' header magic.");
        }

        byte f6 = romBytes[6], f7 = romBytes[7];
        bool isNes20 = (f7 & 0x0C) == 0x08;
        int prgUnits = romBytes[4], chrUnits = romBytes[5];
        ushort mapperId = (ushort)((f7 & 0xF0) | (f6 >> 4));
        byte submapperId = 0;
        int prgRomSize = prgUnits * 16384;
        int chrRomSize = chrUnits * 8192;
        int prgRamSize = 8192;
        int chrRamSize = chrRomSize == 0 ? 8192 : 0;
        CartVideoRegionStandard region = CartVideoRegionStandard.Ntsc;

        if (isNes20)
        {
            mapperId |= (ushort)((romBytes[8] & 0x0F) << 8);
            submapperId = (byte)(romBytes[8] >> 4);
            prgRomSize = DecodeNes20Size(prgUnits, romBytes[9] & 0x0F, 16384);
            chrRomSize = DecodeNes20Size(chrUnits, (romBytes[9] >> 4) & 0x0F, 8192);
            prgRamSize = DecodeShiftSize(romBytes[10] & 0x0F) + DecodeShiftSize(romBytes[10] >> 4);
            chrRamSize = DecodeShiftSize(romBytes[11] & 0x0F) + DecodeShiftSize(romBytes[11] >> 4);
            if (chrRamSize == 0 && chrRomSize == 0) chrRamSize = 8192;
            region = (romBytes[12] & 0x03) switch { 1 => CartVideoRegionStandard.Pal, 3 => CartVideoRegionStandard.Dendy, _ => CartVideoRegionStandard.Ntsc };
        }
        else if ((romBytes[12..16].SequenceEqual((ReadOnlySpan<byte>)[0, 0, 0, 0])) == false)
        {
            // Clean iNES 1.0 headers keep bytes 12..15 zero; dirty headers only contribute lower mapper nibble.
            mapperId &= 0x0F;
            mapperId |= (ushort)(f6 >> 4);
        }

        bool hasTrainer = (f6 & 0x04) != 0;
        bool hasBattery = (f6 & 0x02) != 0;
        CartMirroringMode mirroring = (f6 & 0x08) != 0
            ? CartMirroringMode.FourScreen
            : ((f6 & 0x01) != 0 ? CartMirroringMode.Vertical : CartMirroringMode.Horizontal);

        return new CartRomHeader(mapperId, submapperId, prgRomSize, chrRomSize, prgRamSize, chrRamSize, hasTrainer, hasBattery, isNes20, mirroring, region);
    }

    private static int DecodeNes20Size(int lsb, int msbNibble, int unitBytes)
    {
        if (msbNibble != 0x0F) return ((msbNibble << 8) | lsb) * unitBytes;
        int exp = lsb >> 2;
        int mult = (lsb & 0x03) * 2 + 1;
        long total = (long)mult << Math.Min(30, exp);
        return (int)Math.Clamp(total, unitBytes, 64 * 1024 * 1024);
    }

    private static int DecodeShiftSize(int shift) => shift == 0 ? 0 : (64 << Math.Min(20, shift));
}

/// <summary>
/// Extensible cartridge mapper contract implemented by Mappers <c>0–3</c> in <c>v0.1.0</c> and later licensed boards.
/// </summary>
public interface ICartMapper
{
    /// <summary>Numeric iNES / NES 2.0 mapper number.</summary>
    ushort MapperId { get; }

    /// <summary>Numeric NES 2.0 submapper number.</summary>
    byte SubmapperId { get; }

    /// <summary>Human-readable board family name (e.g., <c>NROM</c>, <c>MMC1</c>, <c>UxROM</c>, <c>CNROM</c>).</summary>
    string Name { get; }

    /// <summary>Current hardware nametable mirroring layout.</summary>
    CartMirroringMode Mirroring { get; }

    /// <summary>Currently active PRG bank index visible in the switchable CPU window.</summary>
    uint ActivePrgBank { get; }

    /// <summary>Currently active CHR bank index visible in the PPU pattern window.</summary>
    uint ActiveChrBank { get; }

    /// <summary>Indicates whether the mapper is asserting an active cartridge IRQ line.</summary>
    bool IsIrqPending { get; }

    /// <summary>Reads a byte from cartridge CPU space (<c>$4020-$FFFF</c>).</summary>
    byte CpuRead(ushort address);

    /// <summary>Writes a byte into cartridge CPU/mapper register space (<c>$4020-$FFFF</c>).</summary>
    void CpuWrite(ushort address, byte value);

    /// <summary>Reads a byte from cartridge CHR / nametable pattern space (<c>$0000-$1FFF</c>).</summary>
    byte PpuRead(ushort address);

    /// <summary>Writes a byte into cartridge CHR-RAM space (<c>$0000-$1FFF</c>).</summary>
    void PpuWrite(ushort address, byte value);

    /// <summary>Notifies scanline-counting mappers (e.g., future <c>MMC3</c>) on PPU address line <c>A12</c> edges.</summary>
    void OnPpuA12Edge(bool a12High, int scanline, int dot);

    /// <summary>Resets mapper bank latches and IRQ state to power-on/reset defaults.</summary>
    void Reset();

    /// <summary>Captures deterministic binary mapper state for <c>F5</c> QuickSave snapshots.</summary>
    byte[] CaptureState();

    /// <summary>Restores deterministic binary mapper state from a QuickSave snapshot.</summary>
    void RestoreState(ReadOnlySpan<byte> state);
}
