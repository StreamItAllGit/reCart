// @module: reCart.Tests.CompilerAndAotCacheTests
// @purpose: Verifies 6502->C# static lifter (UxROM/trainer/jump-tables/zero-BCD), 3-member clean-room .recomp validation, cold AOT cache compile/hit, and fail-closed staticAotOnly trapping.
// @depends-on: System.IO.Compression, Xunit, Foundation.Core, reCart.Compiler, reCart.Contracts, reCart.Core.Runtime
// @exports: CompilerAndAotCacheTests

using System.Buffers.Binary;
using System.IO.Compression;
using Foundation.Core;
using reCart.Compiler;
using reCart.Contracts;
using reCart.Core.Runtime;
using Xunit;

namespace reCart.Tests;

public sealed class CompilerAndAotCacheTests
{
    [Fact]
    public void Lifter_LiftsUxRomWith512ByteTrainerJumpTableAndZeroBcd()
    {
        byte[] rom = new byte[16 + 512 + 32768 + 8192];
        rom[0] = 0x4E; rom[1] = 0x45; rom[2] = 0x53; rom[3] = 0x1A;
        rom[4] = 2; rom[5] = 1; rom[6] = 0x24; // Mapper 2 + trainer
        int fixedBase = 16 + 512 + 16384;
        // $C000: SED; CLC; LDA #$19; ADC #$09; STA $10; LDA #$10; STA $20; LDA #$C0; STA $21; JMP ($0020)
        byte[] resetCode = [0xF8, 0x18, 0xA9, 0x19, 0x69, 0x09, 0x85, 0x10, 0xA9, 0x14, 0x85, 0x20, 0xA9, 0xC0, 0x85, 0x21, 0x6C, 0x20, 0x00, 0xEA, 0x4C, 0x14, 0xC0];
        resetCode.CopyTo(rom.AsSpan(fixedBase));
        BinaryPrimitives.WriteUInt16LittleEndian(rom.AsSpan(fixedBase + 0x3FFA, 2), 0xC014);
        BinaryPrimitives.WriteUInt16LittleEndian(rom.AsSpan(fixedBase + 0x3FFC, 2), 0xC000);
        BinaryPrimitives.WriteUInt16LittleEndian(rom.AsSpan(fixedBase + 0x3FFE, 2), 0xC014);

        var manifest = new CartPackageManifest { ModuleId = "uxrom-trainer-test", MapperId = 2 };
        LiftedProgramResult lifted = new Lifter6502ToCSharp().Lift(rom, manifest);

        Assert.Equal(2, lifted.PrgBankCount);
        Assert.Equal(1u, lifted.FixedBankIndex);
        Assert.Contains("0xC000", lifted.DispatchMetaJson);
        Assert.Contains("UnliftedPcTrapException", lifted.CSharpSource);
    }

    [Fact]
    public void Packager_ProducesStrictly3CleanRoomEntriesAndRejectsViolations()
    {
        string temp = TestPaths.CreateTempDir("recart_comp_pkg");
        try
        {
            string okZip = Path.Combine(temp, "ironframe.recomp");
            SyntheticCleanRoomCartPackage pkg = SyntheticCleanRoomCartBuilder.CreateRecompPackage(okZip);
            CleanRoomArchiveValidationResult res = CleanRoomRecompPackager.ValidateCleanRoomArchive(okZip);

            Assert.True(res.IsValid, res.Summary);
            Assert.Equal(3, res.Entries.Count);
            Assert.Contains(CleanRoomRecompPackager.ManifestEntryName, res.Entries);
            Assert.Contains(CleanRoomRecompPackager.LiftedSourceEntryName, res.Entries);
            Assert.Contains(CleanRoomRecompPackager.DispatchMetaEntryName, res.Entries);
            Assert.DoesNotContain(res.Entries, e => e.EndsWith(".Generated.dll", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(res.Entries, e => e.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(res.Entries, e => e.EndsWith(".nes", StringComparison.OrdinalIgnoreCase));

            // Dirty archive with 4 entries / .nes leak
            string badZip = Path.Combine(temp, "dirty.recomp");
            File.Copy(okZip, badZip, overwrite: true);
            using (ZipArchive zip = ZipFile.Open(badZip, ZipArchiveMode.Update))
                zip.CreateEntry("data/rom/leak.nes");
            CleanRoomArchiveValidationResult badRes = CleanRoomRecompPackager.ValidateCleanRoomArchive(badZip);
            Assert.False(badRes.IsValid);

            // Zip-slip archive
            string slipZip = Path.Combine(temp, "slip.recomp");
            using (ZipArchive zip = ZipFile.Open(slipZip, ZipArchiveMode.Create))
                zip.CreateEntry("../escape.txt");
            Assert.False(CleanRoomRecompPackager.ValidateCleanRoomArchive(slipZip).IsValid);
            Assert.Equal(64, pkg.RomSha256Hex.Length);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task ColdAotCacheCompiler_CompilesToCacheHitsWarmAndExecutesMovedSpriteAndAudio()
    {
        string temp = TestPaths.CreateTempDir("recart_aot_e2e");
        try
        {
            string dataDir = Path.Combine(temp, "data");
            string recompPath = Path.Combine(temp, "ironframe-outerfortress-cart-01.recomp");
            SyntheticCleanRoomCartPackage pkg = SyntheticCleanRoomCartBuilder.CreateRecompPackage(recompPath);

            var progress = new List<string>();
            var compiler = new ColdAotCacheCompiler();
            ICartRecompGameModule modCold = await compiler.CompileAndLoadAsync(recompPath, pkg.RomSha256Hex, dataDir, msg => progress.Add(msg));
            Assert.Contains(progress, p => p.Contains("Compiling", StringComparison.OrdinalIgnoreCase));
            Assert.Single(Directory.EnumerateFiles(Path.Combine(dataDir, "cache"), "*.Generated.dll", SearchOption.AllDirectories));

            // Warm cache hit skips dotnet build
            progress.Clear();
            ICartRecompGameModule modWarm = await compiler.CompileAndLoadAsync(recompPath, pkg.RomSha256Hex, dataDir, msg => progress.Add(msg));
            Assert.Contains(progress, p => p.Contains("Cache hit", StringComparison.OrdinalIgnoreCase));

            using CartMachineHost host = CartMachineFactory.CreateFromRomBytes(pkg.RomBytes, CartVideoRegionStandard.Ntsc, CartExecutionTierMode.StaticAotOnly);
            host.MountStaticModule(modWarm, pkg.Manifest, Path.Combine(temp, "rom"), Path.Combine(temp, "saves"));

            host.StepFrame();
            byte[] frameBefore = host.Ppu.FrameBufferRgba.ToArray();

            // Hold Right + Down across 12 NMI frames and confirm real pixel delta + non-silent Pulse1 audio
            host.SetControllerButtons(0, (uint)(0x80 | 0x20));
            for (int f = 0; f < 12; f++) host.StepFrame();
            byte[] frameAfter = host.Ppu.FrameBufferRgba.ToArray();

            int diffPixels = 0;
            for (int i = 0; i < frameBefore.Length; i += 4)
            {
                if (frameBefore[i] != frameAfter[i] || frameBefore[i + 1] != frameAfter[i + 1] || frameBefore[i + 2] != frameAfter[i + 2])
                    diffPixels++;
            }
            Assert.True(diffPixels > 16, $"Expected movable sprite pixel delta, saw diffPixels={diffPixels}");

            Span<short> pcm = stackalloc short[4096];
            int samples = host.Apu.ReadSamples(pcm);
            Assert.True(samples > 0);
            Assert.Equal(0L, host.Apu.DroppedSampleCount);
            short maxAbs = 0;
            for (int i = 0; i < samples; i++) maxAbs = Math.Max(maxAbs, pcm[i] == short.MinValue ? short.MaxValue : Math.Abs(pcm[i]));
            Assert.True(maxAbs > 100);

            // Fail-closed trap under StaticAotOnly on unlifted PC
            Assert.Throws<UnliftedPcTrapException>(() => modWarm.TryDispatch(0, 0x9F7F));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }
}
