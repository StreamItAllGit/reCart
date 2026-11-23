// @module: reCart.Core.Rom.CartridgeLoader
// @purpose: Loads iNES 1.0 / NES 2.0 ROM containers, skips 512-byte trainers, slices PRG/CHR, and constructs mappers.
// @depends-on: System, System.IO, System.Security.Cryptography, reCart.Contracts, reCart.Core.Mappers
// @exports: CartridgeLoader

using System.Security.Cryptography;
using reCart.Contracts;
using reCart.Core.Mappers;

namespace reCart.Core.Rom;

/// <summary>
/// Clean-room iNES 1.0 / NES 2.0 cartridge loader and SHA-256 verifier.
/// </summary>
public static class CartridgeLoader
{
    /// <summary>Parses a cartridge container byte span into an <see cref="InesRomImage"/>.</summary>
    /// <param name="romBytes">Complete <c>.nes</c> container bytes.</param>
    /// <returns>Parsed immutable <see cref="InesRomImage"/>.</returns>
    public static InesRomImage Load(ReadOnlySpan<byte> romBytes)
    {
        CartRomHeader header = CartRomHeaderParser.Parse(romBytes);
        int offset = CartRomHeaderParser.HeaderSizeBytes;

        byte[] trainer = Array.Empty<byte>();
        if (header.HasTrainer)
        {
            if (romBytes.Length < offset + CartRomHeaderParser.TrainerSizeBytes)
            {
                throw new InvalidDataException(
                    $"Cartridge image truncated inside 512-byte trainer (expected {offset + CartRomHeaderParser.TrainerSizeBytes} bytes, found {romBytes.Length}).");
            }

            trainer = romBytes.Slice(offset, CartRomHeaderParser.TrainerSizeBytes).ToArray();
            offset += CartRomHeaderParser.TrainerSizeBytes;
        }

        if (header.PrgRomSizeBytes <= 0)
        {
            throw new InvalidDataException("Cartridge header declares zero PRG-ROM bytes.");
        }

        if (romBytes.Length < offset + header.PrgRomSizeBytes)
        {
            throw new InvalidDataException(
                $"Cartridge PRG-ROM truncated: expected {header.PrgRomSizeBytes} bytes at offset {offset}, found {Math.Max(0, romBytes.Length - offset)}.");
        }

        byte[] prgRom = romBytes.Slice(offset, header.PrgRomSizeBytes).ToArray();
        offset += header.PrgRomSizeBytes;

        byte[] chrRom = Array.Empty<byte>();
        if (header.ChrRomSizeBytes > 0)
        {
            if (romBytes.Length < offset + header.ChrRomSizeBytes)
            {
                throw new InvalidDataException(
                    $"Cartridge CHR-ROM truncated: expected {header.ChrRomSizeBytes} bytes at offset {offset}, found {Math.Max(0, romBytes.Length - offset)}.");
            }

            chrRom = romBytes.Slice(offset, header.ChrRomSizeBytes).ToArray();
        }

        string sha256 = ComputeSha256Hex(romBytes);
        string prgSha256 = ComputeSha256Hex(prgRom);
        string chrSha256 = ComputeSha256Hex(chrRom);

        return new InesRomImage(header, prgRom, chrRom, trainer, sha256, prgSha256, chrSha256);
    }

    /// <summary>Loads an iNES 1.0 / NES 2.0 cartridge from disk.</summary>
    /// <param name="romFilePath">Absolute or relative path to a user-supplied <c>.nes</c> ROM file.</param>
    public static InesRomImage LoadFromFile(string romFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(romFilePath);
        byte[] bytes = File.ReadAllBytes(romFilePath);
        return Load(bytes);
    }

    /// <summary>Constructs the matching <see cref="ICartMapper"/> for <paramref name="rom"/>.</summary>
    public static ICartMapper CreateMapper(InesRomImage rom) => CartMapperFactory.Create(rom);

    /// <summary>Loads an iNES ROM container and instantiates its matching <see cref="ICartMapper"/>.</summary>
    public static (InesRomImage Rom, ICartMapper Mapper) LoadWithMapper(ReadOnlySpan<byte> romBytes)
    {
        InesRomImage rom = Load(romBytes);
        return (rom, CartMapperFactory.Create(rom));
    }

    private static string ComputeSha256Hex(ReadOnlySpan<byte> payload)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(payload, digest);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
