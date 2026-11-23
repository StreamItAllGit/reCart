// @module: reCart.Tests.PlayerV013PrelaunchTests
// @purpose: Regression tests for v0.1.3 fixes: recipe.JumpTables inline dispatch lifting, .7z extraction + temp cleanup, raw-ROM clean-room guard, and .recomp live drop.
// @depends-on: System, System.Diagnostics, System.IO, System.Linq, System.Security.Cryptography, System.Threading.Tasks, Foundation.Retro, reCart.Compiler, reCart.Contracts, reCart.Core.Runtime, reCart.Player, Xunit
// @exports: PlayerV013PrelaunchTests

using System.Diagnostics;
using System.Security.Cryptography;
using Foundation.Retro;
using reCart.Compiler;
using reCart.Contracts;
using reCart.Core.Runtime;
using reCart.Player;
using Xunit;

namespace reCart.Tests;

[Collection("IsolatedProcessEnv")]
public sealed class PlayerV013PrelaunchTests
{
    [Fact]
    public async Task ColdAotCacheCompiler_HonorsRecipeJumpTables_AndLiftsInlineDispatchTargetsWithoutFallthroughCorruption()
    {
        string temp = TestPaths.CreateTempDir("recart_v013_jt");
        try
        {
            byte[] rom = BuildInlineJumpTableTestRom();
            string sha = Convert.ToHexString(SHA256.HashData(rom)).ToLowerInvariant();
            var manifest = new CartPackageManifest
            {
                SchemaVersion = 1,
                ModuleId = "inline-jt-test",
                Title = "Inline Jump Table Test",
                PlatformId = "nes",
                Version = "0.1.3",
                EngineId = "reCart",
                EngineVersion = "0.1.3",
                CompatibleEngineTrack = "0.1.x",
                SupportedEngineTracks = ["0.1.x"],
                MapperId = 2,
                RequiredDiscChecksums = [new DiscChecksumEntry("data/rom/test.nes", sha, rom.Length, IsRequired: true)]
            };
            var recipe = new CartAotRecipeSpec
            {
                ModuleId = manifest.ModuleId,
                MapperId = 2,
                EntryAddresses = ["0xC000", "0xC050"],
                Seeds =
                [
                    new CartAotRecipeSeed(0, "0x8000", "function", "bank0_target"),
                    new CartAotRecipeSeed(1, "0xC6B7", "jumpTable", "inline_dispatcher")
                ],
                JumpTables = [new CartAotJumpTableHint(1, "0xC6B7", "inline pointer table after JSR")]
            };

            string recompPath = Path.Combine(temp, "app", "inline-jt.recomp");
            CleanRoomRecompPackager.PackageCleanRoomRecipeRecomp(manifest, recipe, recompPath);

            var compiler = new ColdAotCacheCompiler();
            ICartRecompGameModule loaded = await compiler.CompileAndLoadAsync(recompPath, sha, Path.Combine(temp, "data"), rom);

            using CartMachineHost host = CartMachineFactory.CreateFromRomBytes(rom, CartVideoRegionStandard.Ntsc, CartExecutionTierMode.StaticAotOnly);
            host.MountStaticModule(loaded, manifest, Path.Combine(temp, "data", "rom"), Path.Combine(temp, "saves"));
            host.StepFrame();

            Assert.Equal((byte)0x55, host.Bus.Read(0x0040));
            Assert.Equal((byte)0x77, host.Bus.Read(0x0041));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void MediaLoader_ExtractsSevenZipRomAndCleansTempFolderAndRejectsCorruptArchive()
    {
        string temp = TestPaths.CreateTempDir("recart_v013_7z");
        try
        {
            string? sevenZipExe = FindNative7z();
            Assert.False(string.IsNullOrWhiteSpace(sevenZipExe), "Bundled 7z.exe must exist for .7z regression verification.");

            SyntheticCleanRoomCartPackage pkg = SyntheticCleanRoomCartBuilder.BuildPackage();
            string srcRom = Path.Combine(temp, "src", "clean_test_cart.nes");
            Directory.CreateDirectory(Path.GetDirectoryName(srcRom)!);
            File.WriteAllBytes(srcRom, pkg.RomBytes);

            string dataDir = Path.Combine(temp, "data");
            string archiveRom = Path.Combine(dataDir, "rom", "clean_test_cart.7z");
            Directory.CreateDirectory(Path.GetDirectoryName(archiveRom)!);
            using (Process p = Process.Start(new ProcessStartInfo(sevenZipExe!, $"a -t7z \"{archiveRom}\" \"{srcRom}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            })!)
            {
                Assert.True(p.WaitForExit(15000));
                Assert.Equal(0, p.ExitCode);
            }

            int tempDirsBefore = Directory.GetDirectories(Path.GetTempPath(), "recart_7z_*").Length;
            string recompPath = Path.Combine(temp, "app", "clean.recomp");
            Directory.CreateDirectory(Path.GetDirectoryName(recompPath)!);
            CleanRoomRecompPackager.PackageCleanRoomRecomp(pkg.RomBytes, pkg.Manifest, recompPath);

            var opts = PlayerCliOptions.Parse(
            [
                "--open", recompPath, "--data-dir", dataDir,
                "--config", Path.Combine(dataDir, "config", "settings.json"),
                "--log-dir", Path.Combine(temp, "logs")
            ]);
            ReCartSessionHost.LoadedCartSessionInput loaded = ReCartSessionHost.LoadCartInputs(opts);
            int tempDirsAfter = Directory.GetDirectories(Path.GetTempPath(), "recart_7z_*").Length;

            Assert.Equal(pkg.RomSha256Hex, loaded.RomSha256Hex);
            Assert.NotNull(loaded.RomBytes);
            Assert.True(tempDirsAfter <= tempDirsBefore, "Temporary .7z extraction directory leaked.");

            string bad7z = Path.Combine(temp, "corrupt.7z");
            File.WriteAllBytes(bad7z, [0x37, 0x7A, 0xBC, 0xAF, 0x00, 0x01]);
            Assert.Throws<InvalidDataException>(() => CartMediaArchiveLoader.ReadRomBytesFromFileOrArchive(bad7z, sevenZipExe));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task RawRomWithoutRecomp_FailsClosedInHeadlessAndShowsMediaGateGuidanceInSession()
    {
        string temp = TestPaths.CreateTempDir("recart_v013_rawguard");
        try
        {
            SyntheticCleanRoomCartPackage pkg = SyntheticCleanRoomCartBuilder.BuildPackage();
            string rawRom = Path.Combine(temp, "raw_test.nes");
            File.WriteAllBytes(rawRom, pkg.RomBytes);
            string dataDir = Path.Combine(temp, "data");
            string logDir = Path.Combine(temp, "logs");

            int headlessExit = await Program.RunMainAsync(
            [
                "--headless", "--frames", "2", "--rom", rawRom,
                "--data-dir", dataDir, "--config", Path.Combine(dataDir, "config", "settings.json"), "--log-dir", logDir
            ]);
            Assert.NotEqual(0, headlessExit);

            PlayerCliOptions noRecompOpts = PlayerCliOptions.Parse(
                ["--data-dir", dataDir, "--config", Path.Combine(dataDir, "config", "settings.json"), "--log-dir", logDir]);
            var session = new ReCartSessionHost(
                noRecompOpts,
                runtimeFactory: (r, t, bytes) => bytes is not null ? CartMachineFactory.CreateFromRomBytes(bytes, r, t) : null,
                compilerPipeline: new ColdAotCacheCompiler());

            string? err = session.HandleMediaFileDropped(rawRom);
            Assert.NotNull(err);
            Assert.Contains("Select .recomp Module", err, StringComparison.Ordinal);

            string recompPath = Path.Combine(temp, "app", $"{pkg.Manifest.ModuleId}.recomp");
            Directory.CreateDirectory(Path.GetDirectoryName(recompPath)!);
            CleanRoomRecompPackager.PackageCleanRoomRecomp(pkg.RomBytes, pkg.Manifest, recompPath);

            Assert.Null(session.HandleMediaFileDropped(recompPath));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    private static string? FindNative7z()
    {
        string bundled = Path.Combine(AppContext.BaseDirectory, "tools", "7z.exe");
        if (File.Exists(bundled)) return bundled;
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir, "Dev", "assets", "native", "7z.exe");
            if (File.Exists(candidate)) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }

        string pf7z = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe");
        if (File.Exists(pf7z)) return pf7z;

        string pf867z = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe");
        if (File.Exists(pf867z)) return pf867z;

        return null;
    }

    private static byte[] BuildInlineJumpTableTestRom()
    {
        byte[] rom = new byte[16 + 32768];
        rom[0] = (byte)'N'; rom[1] = (byte)'E'; rom[2] = (byte)'S'; rom[3] = 0x1A;
        rom[4] = 2; // 2x 16KB PRG banks (Bank 0 switchable, Bank 1 fixed at $C000)
        rom[6] = 0x20; // Mapper 2 UNROM

        int b0 = 16;
        int b1 = 16 + 16384;
        void WriteBytes(int bankBase, ushort addr, params byte[] bytes)
        {
            int start = addr >= 0xC000 ? bankBase + (addr - 0xC000) : bankBase + (addr - 0x8000);
            Array.Copy(bytes, 0, rom, start, bytes.Length);
        }

        // $C050: RTI
        WriteBytes(b1, 0xC050, 0x40);

        // $C000: JMP $C100 (main reset)
        WriteBytes(b1, 0xC000, 0x4C, 0x00, 0xC1);

        // $C100: LDX #$FF, TXS, JSR $C120, JSR $C140, idle loop at $C109
        WriteBytes(b1, 0xC100,
            0xA2, 0xFF, 0x9A, // LDX #$FF; TXS
            0x20, 0x20, 0xC1, // JSR $C120
            0x20, 0x40, 0xC1, // JSR $C140
            0x4C, 0x09, 0xC1  // JMP $C109 (idle loop)
        );

        // $C120: JSR $C6B7; .word $C127, $C12D
        WriteBytes(b1, 0xC120,
            0x20, 0xB7, 0xC6,
            0x27, 0xC1,
            0x2D, 0xC1,
            // $C127: LDA #$55; STA $40; RTS
            0xA9, 0x55, 0x85, 0x40, 0x60,
            // $C12D: LDA #$AA; STA $40; RTS
            0xA9, 0xAA, 0x85, 0x40, 0x60
        );

        // $C140: backward inline table ($C13A) + switchable bank target ($8000 in bank 0)
        WriteBytes(b1, 0xC13A,
            0xA9, 0x77, 0x85, 0x41, 0x60 // $C13A: LDA #$77; STA $41; RTS
        );
        WriteBytes(b1, 0xC140,
            0xA9, 0x00,             // LDA #$00
            0x20, 0xB7, 0xC6,       // JSR $C6B7
            0x3A, 0xC1,             // .word $C13A (backward target < tableStartPc!)
            0x00, 0x80              // .word $8000 (switchable low window target!)
        );
        WriteBytes(b0, 0x8000, 0xA9, 0x88, 0x85, 0x42, 0x60);

        // $C6B7: clean-room inline table dispatcher reading entry 0 (or Y-indexed; uses $82/$83)
        WriteBytes(b1, 0xC6B7,
            0x68, 0x85, 0x82, // PLA; STA $82
            0x68, 0x85, 0x83, // PLA; STA $83
            0xA0, 0x01,       // LDY #$01
            0xB1, 0x82,       // LDA ($82),Y
            0xAA,             // TAX
            0xC8,             // INY
            0xB1, 0x82,       // LDA ($82),Y
            0x85, 0x83,       // STA $83
            0x86, 0x82,       // STX $82
            0x6C, 0x82, 0x00  // JMP ($0082)
        );

        // Vectors at $FFFA-$FFFF -> $C050 NMI/IRQ, $C000 RESET
        WriteBytes(b1, 0xFFFA, 0x50, 0xC0, 0x00, 0xC0, 0x50, 0xC0);
        return rom;
    }
}
