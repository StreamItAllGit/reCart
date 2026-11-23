// @module: reCart.Core.Rom.InesRomImage
// @purpose: Immutable parsed iNES 1.0 / NES 2.0 cartridge container holding PRG, CHR, optional trainer, and SHA-256 checksums.
// @depends-on: System, reCart.Contracts
// @exports: InesRomImage

using reCart.Contracts;

namespace reCart.Core.Rom;

/// <summary>
/// Represents a parsed clean-room iNES 1.0 or NES 2.0 cartridge ROM image.
/// </summary>
public sealed class InesRomImage
{
    private readonly byte[] _prgRom;
    private readonly byte[] _chrRom;
    private readonly byte[] _trainer;

    /// <summary>Initializes an immutable <see cref="InesRomImage"/> from parsed header metadata and sliced ROM buffers.</summary>
    public InesRomImage(
        CartRomHeader header,
        byte[] prgRom,
        byte[] chrRom,
        byte[] trainer,
        string sha256Hex,
        string prgSha256Hex,
        string chrSha256Hex)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(prgRom);
        ArgumentNullException.ThrowIfNull(chrRom);
        ArgumentNullException.ThrowIfNull(trainer);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256Hex);

        Header = header;
        _prgRom = prgRom;
        _chrRom = chrRom;
        _trainer = trainer;
        Sha256Hex = sha256Hex;
        PrgSha256Hex = prgSha256Hex;
        ChrSha256Hex = chrSha256Hex;
    }

    /// <summary>Parsed iNES 1.0 or NES 2.0 header fields.</summary>
    public CartRomHeader Header { get; }

    /// <summary>Read-only view of the PRG-ROM payload.</summary>
    public ReadOnlyMemory<byte> PrgRom => _prgRom;

    /// <summary>Read-only view of the CHR-ROM payload (empty when cartridge uses CHR-RAM).</summary>
    public ReadOnlyMemory<byte> ChrRom => _chrRom;

    /// <summary>Optional 512-byte legacy trainer block (empty when <see cref="CartRomHeader.HasTrainer"/> is false).</summary>
    public ReadOnlyMemory<byte> Trainer => _trainer;

    /// <summary>Lowercase SHA-256 hex digest of the complete cartridge container bytes.</summary>
    public string Sha256Hex { get; }

    /// <summary>Lowercase SHA-256 hex digest of the PRG-ROM segment.</summary>
    public string PrgSha256Hex { get; }

    /// <summary>Lowercase SHA-256 hex digest of the CHR-ROM segment.</summary>
    public string ChrSha256Hex { get; }

    /// <summary>Indicates whether the cartridge has zero CHR-ROM and uses writable CHR-RAM instead.</summary>
    public bool UsesChrRam => Header.ChrRomSizeBytes == 0;

    /// <summary>Total 16 KB PRG-ROM banks present in this image.</summary>
    public int PrgBankCount16K => Math.Max(1, _prgRom.Length / 16384);

    /// <summary>Total 8 KB CHR banks (CHR-ROM or allocated CHR-RAM).</summary>
    public int ChrBankCount8K => _chrRom.Length > 0
        ? Math.Max(1, _chrRom.Length / 8192)
        : Math.Max(1, Header.ChrRamSizeBytes / 8192);

    internal ReadOnlySpan<byte> PrgSpan => _prgRom;

    internal ReadOnlySpan<byte> ChrSpan => _chrRom;

    internal byte[] CopyPrgArray() => (byte[])_prgRom.Clone();

    internal byte[] CopyChrArray() => (byte[])_chrRom.Clone();
}
