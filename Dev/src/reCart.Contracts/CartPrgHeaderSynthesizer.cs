// @module: reCart.Contracts.CartPrgHeaderSynthesizer
// @purpose: Clean-room iNES/NES 2.0 16-byte header synthesis for verified headerless PRG cartridge dumps (.prg).
// @depends-on: System, System.IO
// @exports: CartPrgHeaderSynthesizer

namespace reCart.Contracts;

/// <summary>
/// Wraps verified headerless <c>.prg</c> cartridge dumps (e.g., <c>131,072</c>-byte CHR-RAM carts)
/// with a deterministic 16-byte iNES/NES 2.0 container header in memory after raw SHA-256 verification.
/// </summary>
public static class CartPrgHeaderSynthesizer
{
    /// <summary>Standard PRG bank granularity in bytes (<c>16 KB</c>).</summary>
    public const int PrgBankSizeBytes = 16384;

    /// <summary>Returns whether <paramref name="bytes"/> already starts with the 4-byte iNES magic signature.</summary>
    public static bool HasInesMagic(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= CartRomHeaderParser.HeaderSizeBytes &&
        bytes[0] == 0x4E && bytes[1] == 0x45 && bytes[2] == 0x53 && bytes[3] == 0x1A;

    /// <summary>
    /// Returns <paramref name="rawBytes"/> untouched when already an iNES container, or prepends a canonical 16-byte
    /// iNES/NES 2.0 header for headerless CHR-RAM PRG payloads whose byte count is an exact multiple of 16 KB.
    /// </summary>
    public static byte[] SynthesizeIfHeaderless(
        byte[] rawBytes,
        ushort mapperId = 2,
        byte submapperId = 0,
        CartMirroringMode mirroring = CartMirroringMode.Vertical,
        CartVideoRegionStandard region = CartVideoRegionStandard.Ntsc)
    {
        ArgumentNullException.ThrowIfNull(rawBytes);
        if (HasInesMagic(rawBytes)) return rawBytes;

        if (rawBytes.Length <= 0 || (rawBytes.Length % PrgBankSizeBytes) != 0)
        {
            throw new InvalidDataException(
                $"Headerless PRG image length ({rawBytes.Length} bytes) is not a valid positive multiple of {PrgBankSizeBytes} bytes.");
        }

        int prgBanks16K = rawBytes.Length / PrgBankSizeBytes;
        if (prgBanks16K > 255)
        {
            throw new InvalidDataException($"Headerless PRG bank count {prgBanks16K} exceeds iNES 8-bit header limit.");
        }

        byte[] output = new byte[CartRomHeaderParser.HeaderSizeBytes + rawBytes.Length];
        output[0] = 0x4E;
        output[1] = 0x45;
        output[2] = 0x53;
        output[3] = 0x1A;
        output[4] = (byte)prgBanks16K;
        output[5] = 0x00;

        byte flags6 = (byte)((mapperId & 0x0F) << 4);
        if (mirroring == CartMirroringMode.Vertical) flags6 |= 0x01;
        else if (mirroring == CartMirroringMode.FourScreen) flags6 |= 0x08;
        output[6] = flags6;

        bool needsNes20 = mapperId > 255 || submapperId > 0 || region == CartVideoRegionStandard.Dendy;
        byte flags7 = (byte)(mapperId & 0xF0);
        if (needsNes20) flags7 |= 0x08;
        output[7] = flags7;

        if (needsNes20)
        {
            output[8] = (byte)(((submapperId & 0x0F) << 4) | ((mapperId >> 8) & 0x0F));
            output[12] = region switch
            {
                CartVideoRegionStandard.Pal => 0x01,
                CartVideoRegionStandard.Dendy => 0x03,
                _ => 0x00
            };
        }
        else if (region == CartVideoRegionStandard.Pal)
        {
            output[9] = 0x01;
        }

        Buffer.BlockCopy(rawBytes, 0, output, CartRomHeaderParser.HeaderSizeBytes, rawBytes.Length);
        return output;
    }
}
