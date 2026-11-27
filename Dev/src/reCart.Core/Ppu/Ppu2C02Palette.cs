// @module: reCart.Core.Ppu.Ppu2C02Palette
// @purpose: RP2C02 64-color RGBA8888 master palette, palette RAM mirroring, greyscale masking, and RGB color emphasis.
// @depends-on: System
// @exports: Ppu2C02Palette

namespace reCart.Core.Ppu;

/// <summary>
/// Clean-room RP2C02 64-color master palette, palette RAM mirroring (<c>$3F00-$3F1F</c>),
/// greyscale masking (<c>PPUMASK &amp; 0x01</c>), and RGB emphasis attenuation (<c>PPUMASK &amp; 0xE0</c>).
/// </summary>
internal static class Ppu2C02Palette
{
    private static readonly uint[] MasterRgb =
    [
        0x545454u, 0x001E74u, 0x081090u, 0x300088u, 0x440064u, 0x5C0030u, 0x540400u, 0x3C1800u,
        0x202A00u, 0x083A00u, 0x004000u, 0x003C00u, 0x00323Cu, 0x000000u, 0x000000u, 0x000000u,
        0x989698u, 0x084CC4u, 0x3032ECu, 0x5C1EE4u, 0x8814B0u, 0xA01464u, 0x982220u, 0x783C00u,
        0x545A00u, 0x287200u, 0x087C00u, 0x007628u, 0x006678u, 0x000000u, 0x000000u, 0x000000u,
        0xECEEECu, 0x4C9AECu, 0x787CECu, 0xB062ECu, 0xE454ECu, 0xEC58B4u, 0xEC6A64u, 0xD48820u,
        0xA0AA00u, 0x74C400u, 0x4CD020u, 0x38CC6Cu, 0x38B4CCu, 0x3C3C3Cu, 0x000000u, 0x000000u,
        0xECEEECu, 0xA8CCEBu, 0xBCBCECu, 0xD4B2ECu, 0xECAEECu, 0xECAED4u, 0xECB4B0u, 0xE4C490u,
        0xCCD278u, 0xB4DE78u, 0xA8E290u, 0x98E2B4u, 0xA0D6E4u, 0xA0A2A0u, 0x000000u, 0x000000u
    ];

    private static readonly uint[] PackedRgbaLut = BuildPackedRgbaLut();

    /// <summary>Resolves a mirrored <c>$3F00-$3FFF</c> palette RAM index (<c>0..31</c>).</summary>
    public static int ResolvePaletteIndex(ushort address)
    {
        int index = address & 0x1F;
        if ((index & 0x13) == 0x10)
        {
            index &= 0x0F;
        }

        return index;
    }

    /// <summary>Looks up a pre-packed RGBA8888 pixel (<c>uint</c>) applying greyscale and emphasis bits.</summary>
    public static uint LookupPackedRgba(byte colorIndex, byte ppuMask, bool swapRedGreenEmphasis)
    {
        int masked = (ppuMask & 0x01) != 0 ? (colorIndex & 0x30) : (colorIndex & 0x3F);
        int swapBit = swapRedGreenEmphasis ? 512 : 0;
        int emphasisBits = (ppuMask & 0xE0) << 1;
        return PackedRgbaLut[swapBit | emphasisBits | masked];
    }

    /// <summary>Writes a single RGBA8888 pixel into <paramref name="frameBuffer"/> applying greyscale and emphasis bits.</summary>
    public static void WriteRgbaPixel(Span<byte> frameBuffer, int byteOffset, byte colorIndex, byte ppuMask, bool swapRedGreenEmphasis)
    {
        uint packed = LookupPackedRgba(colorIndex, ppuMask, swapRedGreenEmphasis);
        System.Runtime.InteropServices.MemoryMarshal.Write(frameBuffer.Slice(byteOffset, 4), in packed);
    }

    private static uint[] BuildPackedRgbaLut()
    {
        var lut = new uint[1024];
        for (int swap = 0; swap < 2; swap++)
        {
            bool swapRg = swap != 0;
            for (int emphasis = 0; emphasis < 8; emphasis++)
            {
                bool empR = swapRg ? (emphasis & 0x02) != 0 : (emphasis & 0x01) != 0;
                bool empG = swapRg ? (emphasis & 0x01) != 0 : (emphasis & 0x02) != 0;
                bool empB = (emphasis & 0x04) != 0;

                for (int colorIdx = 0; colorIdx < 64; colorIdx++)
                {
                    uint rgb = MasterRgb[colorIdx];
                    int r = (int)((rgb >> 16) & 0xFF);
                    int g = (int)((rgb >> 8) & 0xFF);
                    int b = (int)(rgb & 0xFF);

                    if (emphasis != 0)
                    {
                        if (!empR) r = (r * 191) >> 8;
                        if (!empG) g = (g * 191) >> 8;
                        if (!empB) b = (b * 191) >> 8;
                    }

                    lut[(swap << 9) | (emphasis << 6) | colorIdx] = BitConverter.IsLittleEndian
                        ? ((uint)r | ((uint)g << 8) | ((uint)b << 16) | 0xFF000000u)
                        : (((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | 0xFFu);
                }
            }
        }

        return lut;
    }
}
