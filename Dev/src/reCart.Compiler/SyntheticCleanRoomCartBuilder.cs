// @module: reCart.Compiler.SyntheticCleanRoomCartBuilder
// @purpose: Builds a 100% original clean-room 32 KB NROM iNES ROM + CartPackageManifest (ironframe-outerfortress-cart-01).
// @depends-on: System, System.IO, System.Security.Cryptography, Foundation.Retro, reCart.Contracts
// @exports: SyntheticCleanRoomCartPackage, SyntheticCleanRoomCartBuilder

using System.Security.Cryptography;
using Foundation.Retro;
using reCart.Contracts;

namespace reCart.Compiler;

/// <summary>
/// Clean-room synthetic cartridge ROM bytes, SHA-256 checksum, and matching <see cref="CartPackageManifest"/>.
/// </summary>
public sealed record SyntheticCleanRoomCartPackage(
    byte[] RomBytes,
    string RomSha256Hex,
    CartPackageManifest Manifest);

/// <summary>
/// Constructs the 100% original clean-room <c>ironframe-outerfortress-cart-01</c> 32 KB NROM iNES ROM
/// with tactical background pattern, <c>$4016</c> D-Pad movable bright amber sprite, and audible APU Pulse1 output.
/// </summary>
public static class SyntheticCleanRoomCartBuilder
{
    /// <summary>Canonical clean-room synthetic module identifier.</summary>
    public const string DefaultModuleId = "ironframe-outerfortress-cart-01";

    private const byte ZpPlayerX = 0x00;
    private const byte ZpPlayerY = 0x01;
    private const byte ZpPad1 = 0x02;
    private const byte ZpRow = 0x03;
    private const byte ZpCol = 0x04;

    /// <summary>
    /// Builds the clean-room 32 KB NROM iNES ROM + <see cref="CartPackageManifest"/> (<c>mapperId: 0</c>).
    /// </summary>
    public static SyntheticCleanRoomCartPackage BuildPackage(string moduleId = DefaultModuleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        byte[] prg = BuildPrgRom();
        byte[] chr = BuildChrRom();
        byte[] rom = new byte[16 + prg.Length + chr.Length];

        // iNES 1.0 header: NES\x1A, 2x 16KB PRG (32KB), 1x 8KB CHR (8KB), Mapper 0, Vertical mirroring.
        rom[0] = 0x4E; rom[1] = 0x45; rom[2] = 0x53; rom[3] = 0x1A;
        rom[4] = 2;
        rom[5] = 1;
        rom[6] = 0x01;
        rom[7] = 0x00;
        Buffer.BlockCopy(prg, 0, rom, 16, prg.Length);
        Buffer.BlockCopy(chr, 0, rom, 16 + prg.Length, chr.Length);

        string sha256 = Convert.ToHexString(SHA256.HashData(rom)).ToLowerInvariant();
        var manifest = new CartPackageManifest
        {
            SchemaVersion = 1,
            ModuleId = moduleId.Trim(),
            Title = "IronFrame OuterFortress (Clean-Room Cart 01)",
            PlatformId = CartEngineConstants.PlatformId,
            Version = CartEngineConstants.Version,
            EngineId = CartEngineConstants.EngineId,
            EngineVersion = CartEngineConstants.Version,
            CompatibleEngineTrack = CartEngineConstants.CompatibleEngineTrack,
            SupportedEngineTracks = [.. CartEngineConstants.SupportedEngineTracks],
            PreloadRomDir = "data/rom",
            ModuleTypeName = "reCart.Lifted.CartLiftedModule",
            BackendId = CartRenderBackendIds.OpenGl,
            AspectRatioMode = "nativePar",
            ExecutionTierMode = "staticAotOnly",
            VideoRegionStandardName = "ntsc",
            MapperId = 0,
            SubmapperId = 0,
            DefaultFramerateMode = RecompTimingMode.LockedCap,
            RequiredDiscChecksums = [new DiscChecksumEntry($"{moduleId.Trim()}.nes", sha256, rom.Length, false)]
        };

        return new SyntheticCleanRoomCartPackage(rom, sha256, manifest);
    }

    /// <summary>
    /// Builds the synthetic cart and packages it into a strictly 3-member clean-room <c>.recomp</c> archive.
    /// </summary>
    public static SyntheticCleanRoomCartPackage CreateRecompPackage(string outputRecompZipPath, string moduleId = DefaultModuleId)
    {
        SyntheticCleanRoomCartPackage pkg = BuildPackage(moduleId);
        CleanRoomRecompPackager.PackageCleanRoomRecomp(pkg.RomBytes, pkg.Manifest, outputRecompZipPath);
        return pkg;
    }

    private static byte[] BuildPrgRom()
    {
        var a = new Assembler6502();
        EmitResetVector(a);
        EmitNmiHandler(a);
        EmitIrqHandler(a);
        EmitSubroutines(a);
        EmitPaletteData(a);
        return a.FinalizeRom("NMI", "RESET", "IRQ");
    }

    private static void EmitResetVector(Assembler6502 a)
    {
        a.MarkLabel("RESET");
        a.Emit(0x78, 0xD8);             // SEI; CLD
        a.LdxImm(0x40); a.StxAbs(0x4017); // Disable APU frame IRQ
        a.LdxImm(0xFF); a.Emit(0x9A);   // TXS
        a.Emit(0xE8);                   // INX -> 0
        a.StxAbs(0x2000); a.StxAbs(0x2001); a.StxAbs(0x4010);

        // Clear OAM shadow ($0200-$02FF) offscreen ($FE)
        a.LdaImm(0xFE);
        a.MarkLabel("ClearOam");
        a.StaAbsX(0x0200);
        a.Emit(0xE8);                   // INX
        a.Bne("ClearOam");

        // Upload 32-byte Palette ($3F00-$3F1F)
        a.BitAbs(0x2002);
        a.LdaImm(0x3F); a.StaAbs(0x2006);
        a.LdaImm(0x00); a.StaAbs(0x2006);
        a.LdxImm(0x00);
        a.MarkLabel("PalLoop");
        a.LdaAbsXLabel("PaletteData");
        a.StaAbs(0x2007);
        a.Emit(0xE8);
        a.CpxImm(0x20);
        a.Bne("PalLoop");

        // Draw crisp 32x30 tactical pattern background onto Nametable 0 ($2000)
        a.BitAbs(0x2002);
        a.LdaImm(0x20); a.StaAbs(0x2006);
        a.LdaImm(0x00); a.StaAbs(0x2006);
        a.LdaImm(0x00); a.StaZp(ZpRow);

        a.MarkLabel("RowLoop");
        a.LdaImm(0x00); a.StaZp(ZpCol);
        a.MarkLabel("ColLoop");
        a.LdaZp(ZpRow); a.Beq("DrawWall");
        a.CmpImm(29); a.Beq("DrawWall");
        a.LdaZp(ZpCol); a.Beq("DrawWall");
        a.CmpImm(31); a.Beq("DrawWall");
        // Corner pylons at row 6/23, col 8/23
        a.LdaZp(ZpRow); a.CmpImm(6); a.Beq("CheckPylonCol");
        a.CmpImm(23); a.Bne("DrawFloor");
        a.MarkLabel("CheckPylonCol");
        a.LdaZp(ZpCol); a.CmpImm(8); a.Beq("DrawPylon");
        a.CmpImm(23); a.Beq("DrawPylon");
        a.MarkLabel("DrawFloor");
        a.LdaImm(0x02); a.Jmp("WriteTile");
        a.MarkLabel("DrawWall");
        a.LdaImm(0x01); a.Jmp("WriteTile");
        a.MarkLabel("DrawPylon");
        a.LdaImm(0x03);
        a.MarkLabel("WriteTile");
        a.StaAbs(0x2007);
        a.IncZp(ZpCol); a.LdaZp(ZpCol); a.CmpImm(32); a.Bne("ColLoop");
        a.IncZp(ZpRow); a.LdaZp(ZpRow); a.CmpImm(30); a.Bne("RowLoop");

        // Attributes ($23C0-$23FF): top/bottom tactical emerald border, inner slate-blue grid
        a.LdxImm(0x00);
        a.MarkLabel("AttrLoop");
        a.LdaImm(0x00);
        a.Emit(0xE0, 0x08); a.Bcc("AttrBorder"); // CPX #8
        a.Emit(0xE0, 0x38); a.Bcs("AttrBorder"); // CPX #56
        a.Jmp("AttrWrite");
        a.MarkLabel("AttrBorder");
        a.LdaImm(0x55);
        a.MarkLabel("AttrWrite");
        a.StaAbs(0x2007);
        a.Emit(0xE8); a.CpxImm(64); a.Bne("AttrLoop");

        // Initialize player centered at (120, 112) and push initial OAM
        a.LdaImm(120); a.StaZp(ZpPlayerX);
        a.LdaImm(112); a.StaZp(ZpPlayerY);
        a.Jsr("UpdatePlayerOam");
        a.LdaImm(0x02); a.StaAbs(0x4014);

        // Configure APU Pulse1 for continuous audible ~440 Hz PCM tone
        a.LdaImm(0x01); a.StaAbs(0x4015);
        a.LdaImm(0xBF); a.StaAbs(0x4000);
        a.LdaImm(0x08); a.StaAbs(0x4001);
        a.LdaImm(0xFD); a.StaAbs(0x4002);
        a.LdaImm(0x00); a.StaAbs(0x4003);

        // Enable rendering + VBlank NMI (sprite table $1000)
        a.BitAbs(0x2002);
        a.LdaImm(0x00); a.StaAbs(0x2005); a.StaAbs(0x2005);
        a.LdaImm(0x1E); a.StaAbs(0x2001);
        a.LdaImm(0x88); a.StaAbs(0x2000);
        a.Emit(0x58);                   // CLI
        a.MarkLabel("Forever");
        a.Jmp("Forever");
    }

    private static void EmitNmiHandler(Assembler6502 a)
    {
        a.MarkLabel("NMI");
        a.Emit(0x48, 0x8A, 0x48, 0x98, 0x48); // PHA; TXA; PHA; TYA; PHA
        a.Jsr("ReadPad1");

        // Right ($01)
        a.LdaZp(ZpPad1); a.AndImm(0x01); a.Beq("ChkLeft");
        a.LdaZp(ZpPlayerX); a.CmpImm(0xE0); a.Bcs("ChkLeft");
        a.Emit(0x18); a.AdcImm(0x02); a.StaZp(ZpPlayerX);
        a.MarkLabel("ChkLeft");
        a.LdaZp(ZpPad1); a.AndImm(0x02); a.Beq("ChkDown");
        a.LdaZp(ZpPlayerX); a.CmpImm(0x12); a.Bcc("ChkDown");
        a.Emit(0x38); a.SbcImm(0x02); a.StaZp(ZpPlayerX);
        a.MarkLabel("ChkDown");
        a.LdaZp(ZpPad1); a.AndImm(0x04); a.Beq("ChkUp");
        a.LdaZp(ZpPlayerY); a.CmpImm(0xD0); a.Bcs("ChkUp");
        a.Emit(0x18); a.AdcImm(0x02); a.StaZp(ZpPlayerY);
        a.MarkLabel("ChkUp");
        a.LdaZp(ZpPad1); a.AndImm(0x08); a.Beq("AfterMove");
        a.LdaZp(ZpPlayerY); a.CmpImm(0x12); a.Bcc("AfterMove");
        a.Emit(0x38); a.SbcImm(0x02); a.StaZp(ZpPlayerY);

        a.MarkLabel("AfterMove");
        a.Jsr("UpdatePlayerOam");
        a.LdaImm(0x02); a.StaAbs(0x4014);
        a.BitAbs(0x2002);
        a.LdaImm(0x00); a.StaAbs(0x2005); a.StaAbs(0x2005);
        a.LdaImm(0x1E); a.StaAbs(0x2001);
        a.LdaImm(0x88); a.StaAbs(0x2000);
        a.LdaImm(0x01); a.StaAbs(0x4015);
        a.Emit(0x68, 0xA8, 0x68, 0xAA, 0x68, 0x40); // PLA; TAY; PLA; TAX; PLA; RTI
    }

    private static void EmitIrqHandler(Assembler6502 a)
    {
        a.MarkLabel("IRQ");
        a.Emit(0x40); // RTI
    }

    private static void EmitSubroutines(Assembler6502 a)
    {
        a.MarkLabel("ReadPad1");
        a.LdaImm(0x01); a.StaAbs(0x4016);
        a.LdaImm(0x00); a.StaAbs(0x4016);
        a.StaZp(ZpPad1);
        a.LdxImm(0x08);
        a.MarkLabel("PadLoop");
        a.LdaAbs(0x4016);
        a.Emit(0x4A);                   // LSR A
        a.RolZp(ZpPad1);
        a.Emit(0xCA);                   // DEX
        a.Bne("PadLoop");
        a.Emit(0x60);                   // RTS

        a.MarkLabel("UpdatePlayerOam");
        // 2x2 metasprite at $0200..$020F using sprite tiles 4..7, palette 0
        a.LdaZp(ZpPlayerY); a.StaAbs(0x0200); a.StaAbs(0x0204);
        a.Emit(0x18); a.AdcImm(0x08); a.StaAbs(0x0208); a.StaAbs(0x020C);
        a.LdaZp(ZpPlayerX); a.StaAbs(0x0203); a.StaAbs(0x020B);
        a.Emit(0x18); a.AdcImm(0x08); a.StaAbs(0x0207); a.StaAbs(0x020F);
        a.LdaImm(0x04); a.StaAbs(0x0201);
        a.LdaImm(0x05); a.StaAbs(0x0205);
        a.LdaImm(0x06); a.StaAbs(0x0209);
        a.LdaImm(0x07); a.StaAbs(0x020D);
        a.LdaImm(0x00); a.StaAbs(0x0202); a.StaAbs(0x0206); a.StaAbs(0x020A); a.StaAbs(0x020E);
        a.Emit(0x60);                   // RTS
    }

    private static void EmitPaletteData(Assembler6502 a)
    {
        a.EmitDataBlock("PaletteData",
        [
            0x0F, 0x01, 0x11, 0x00, // BG0: deep black + tactical navy/slate-blue grid
            0x0F, 0x0A, 0x1A, 0x2A, // BG1: tactical perimeter emerald accent
            0x0F, 0x01, 0x11, 0x30, // BG2
            0x0F, 0x00, 0x10, 0x30, // BG3
            0x0F, 0x17, 0x27, 0x37, // SP0: deep amber + bright amber (#F59E0B) + gold visor
            0x0F, 0x17, 0x27, 0x37,
            0x0F, 0x17, 0x27, 0x37,
            0x0F, 0x17, 0x27, 0x37
        ]);
    }

    private static byte[] BuildChrRom()
    {
        byte[] chr = new byte[8192];
        // Pattern table 0 ($0000): BG tiles
        WriteTile(chr, 0x0010, ["22222222", "20100102", "21000012", "20011002", "20011002", "21000012", "20100102", "22222222"]); // Tile 1: border
        WriteTile(chr, 0x0020, ["21010101", "10000000", "00000000", "10001000", "00012100", "10001000", "00000000", "10000000"]); // Tile 2: dark tactical grid
        WriteTile(chr, 0x0030, ["00033000", "00322300", "03211230", "32133123", "32133123", "03211230", "00322300", "00033000"]); // Tile 3: tactical pylon

        // Pattern table 1 ($1000): 16x16 bright amber operator metasprite (Tiles 4..7)
        WriteTile(chr, 0x1040, ["00011111", "00122222", "01223333", "01233333", "01222222", "11222211", "12222222", "12233222"]);
        WriteTile(chr, 0x1050, ["11111000", "22222100", "33332210", "33333210", "22222210", "11222211", "22222221", "22233221"]);
        WriteTile(chr, 0x1060, ["12222222", "12222222", "01222222", "01222122", "00122122", "00122112", "00122011", "00111000"]);
        WriteTile(chr, 0x1070, ["22222221", "22222221", "22222210", "22122210", "22122100", "21122100", "11022100", "00011100"]);
        return chr;
    }

    private static void WriteTile(byte[] chr, int offset, string[] rows)
    {
        for (int y = 0; y < 8; y++)
        {
            byte p0 = 0, p1 = 0;
            for (int x = 0; x < 8; x++)
            {
                int color = rows[y][x] - '0';
                int bit = 7 - x;
                p0 |= (byte)((color & 1) << bit);
                p1 |= (byte)(((color >> 1) & 1) << bit);
            }
            chr[offset + y] = p0;
            chr[offset + 8 + y] = p1;
        }
    }
}
