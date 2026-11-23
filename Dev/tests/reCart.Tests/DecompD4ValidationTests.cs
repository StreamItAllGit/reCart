// @module: reCart.Tests.DecompD4ValidationTests
// @purpose: Verifies reCart.Decomp D0-D5 pipeline, all 6 SixTellDetector rules, D4/D5 schema validation, clean-room guards, and read-only research oracle checks.
// @depends-on: Xunit, Foundation.Core, reCart.Compiler, reCart.Contracts, reCart.Decomp, reEngine.Core.Decomp
// @exports: DecompD4ValidationTests

using Foundation.Core;
using reCart.Compiler;
using reCart.Contracts;
using reCart.Decomp;
using reEngine.Core.Decomp;
using Xunit;

namespace reCart.Tests;

public sealed class DecompD4ValidationTests
{
    private const string OracleRepoEnvVar = "RECART_ORACLE_REPO_DIR";

    [Theory]
    [InlineData(SixTellKind.RawDecompilerTokens, "player_logic.c", "// @module: player_logic\n// Clean docs\n#include <stdint.h>\nuint8_t step(void) { undefined1 x = (uint8_t)FUN_8000(); return x; }")]
    [InlineData(SixTellKind.SplitFragmentFiles, "bank00_01.c", "// @module: bank00_01\n// Fragment file\n#include <stdint.h>\nuint8_t ok(void) { return 1; }")]
    [InlineData(SixTellKind.LowIdentifierReadability, "enemy_ai.c", "// @module: enemy_ai\n// Uses hexrays names\n#include <stdint.h>\nuint8_t tick_ai(uint8_t a1) { uint8_t v1 = a1; uint8_t v2 = v1 + 1; uint8_t v3 = v2; return v3; }")]
    [InlineData(SixTellKind.MissingModuleHeaderOrComments, "nocomment.c", "#include <stdint.h>\nuint8_t update_sprite_coords(uint8_t player_x, uint8_t delta_x) { return (uint8_t)(player_x + delta_x); }")]
    [InlineData(SixTellKind.AbiMismatch, "mmio_bad.c", "// @module: mmio_bad\n// Bad wide MMIO\n#include <stdint.h>\nvoid bad_ppu_write(void) { *(volatile uint32_t *)0x2000 = 0x80; }")]
    public void SixTellDetector_FlagsSourceTellsAndPassesCleanSource(SixTellKind expectedKind, string fileName, string sourceText)
    {
        string temp = TestPaths.CreateTempDir("recart_tell");
        try
        {
            File.WriteAllText(Path.Combine(temp, fileName), sourceText);
            SixTellScanResult res = SixTellDetector.ScanDirectory(temp);
            Assert.False(res.IsClean);
            Assert.Contains(expectedKind, res.DetectedKinds);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Theory]
    [InlineData("undefined1 bad_var = 0;")]
    [InlineData("uint8_t val = (uint8_t)CONCAT11(a, b);")]
    [InlineData("uint8_t FUN_8040(void) { return in_A; }")]
    public void SixTellDetector_FlagsIndividualRawDecompilerSignatures(string snippet)
    {
        string temp = TestPaths.CreateTempDir("recart_tell_sig");
        try
        {
            File.WriteAllText(Path.Combine(temp, "sample.c"),
                $"// @module: sample\n// Test snippet\n#include <stdint.h>\nvoid f(void) {{ {snippet} }}\n");
            SixTellScanResult res = SixTellDetector.ScanDirectory(temp);
            Assert.False(res.IsClean);
            Assert.Contains(SixTellKind.RawDecompilerTokens, res.DetectedKinds);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void SixTellDetector_FlagsBannedWarningSuppressionInBuildConfig()
    {
        string temp = TestPaths.CreateTempDir("recart_warn_flag");
        try
        {
            File.WriteAllText(Path.Combine(temp, "Makefile"), "CFLAGS = -std=c99 -Wno-everything\n");
            File.WriteAllText(Path.Combine(temp, "clean_mod.c"),
                "// @module: clean_mod\n// Updates tactical player timer\n#include <stdint.h>\nuint8_t advance_timer(uint8_t current_ticks) { /* Wrap cleanly mod 256 */ return (uint8_t)(current_ticks + 1); }\n");

            SixTellScanResult bad = SixTellDetector.ScanDirectory(temp, includeBuildConfigs: true);
            Assert.False(bad.IsClean);
            Assert.Contains(SixTellKind.BannedWarningSuppression, bad.DetectedKinds);

            File.WriteAllText(Path.Combine(temp, "Makefile"), "CFLAGS = -std=c99 -Wall -Wextra -Wno-unused-parameter\n");
            SixTellScanResult clean = SixTellDetector.ScanDirectory(temp, includeBuildConfigs: true);
            Assert.True(clean.IsClean, clean.Summary);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void D4BuildReportValidator_EnforcesZeroWarningsAndRejectsSuppressions()
    {
        string goodJson = """
            {
              "compilerErrorCount": 0,
              "compilerWarningCount": 0,
              "craftsmanshipTellCount": 0,
              "zeroWarningsVerified": true,
              "compilerFlags": ["-std=c99", "-Wall", "-Wextra", "-Wno-unused-parameter"],
              "isPassed": true
            }
            """;
        D4BuildReportValidationResult pass = D4BuildReportValidator.ValidateD4BuildReportJson(goodJson);
        Assert.True(pass.IsValid, pass.Summary);

        string badJson = goodJson.Replace("-Wextra", "-Wno-everything");
        D4BuildReportValidationResult fail = D4BuildReportValidator.ValidateD4BuildReportJson(badJson);
        Assert.False(fail.IsValid);
    }

    [Fact]
    public void DecompProvider_ReadOnlyVerifiesResearchOracleWithoutMutation()
    {
        string? configuredOracle = Environment.GetEnvironmentVariable(OracleRepoEnvVar);
        string? tempOracle = null;
        try
        {
            string oracleRoot;
            int minBinaries = 1;
            if (!string.IsNullOrWhiteSpace(configuredOracle))
            {
                Assert.True(Directory.Exists(configuredOracle), $"Expected oracle repo at '{configuredOracle}'.");
                oracleRoot = configuredOracle;
            }
            else
            {
                tempOracle = TestPaths.CreateTempDir("recart_ro_oracle");
                oracleRoot = SeedSyntheticReadOnlyOracleFixture(tempOracle);
            }

            string manifestPath = Path.Combine(oracleRoot, "Dev", "research.manifest.json");
            string d4Path = Path.Combine(oracleRoot, "Dev", "schemas", "d4_build_report.json");
            string d5Path = Path.Combine(oracleRoot, "Dev", "schemas", "d5_byte_match.json");
            DateTime beforeManifestWrite = File.GetLastWriteTimeUtc(manifestPath);

            ResearchManifestValidationResult manifestRes = D4BuildReportValidator.ValidateResearchManifestFile(manifestPath, "nes");
            Assert.True(manifestRes.IsValid, manifestRes.Summary);
            Assert.Equal("nes", manifestRes.Platform, ignoreCase: true);
            Assert.True(manifestRes.BinariesCount >= minBinaries, $"Expected >= {minBinaries} binaries, found {manifestRes.BinariesCount}.");

            D4BuildReportValidationResult d4Res = D4BuildReportValidator.ValidateD4BuildReportFile(d4Path);
            Assert.True(d4Res.IsValid, d4Res.Summary);
            Assert.Equal(0, d4Res.CompilerErrors);
            Assert.Equal(0, d4Res.CompilerWarnings);

            D5ByteMatchValidationResult d5Res = D4BuildReportValidator.ValidateD5ByteMatchFile(d5Path);
            Assert.True(d5Res.IsValid, d5Res.Summary);
            Assert.Equal(0, d5Res.MismatchedBytes);
            Assert.Equal(beforeManifestWrite, File.GetLastWriteTimeUtc(manifestPath));
        }
        finally
        {
            if (tempOracle is not null && Directory.Exists(tempOracle))
                Directory.Delete(tempOracle, recursive: true);
        }
    }

    private static string SeedSyntheticReadOnlyOracleFixture(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "Dev", "schemas"));
        File.WriteAllText(Path.Combine(root, "Dev", "research.manifest.json"), """
            {
              "schemaVersion": 1,
              "platformId": "nes",
              "moduleId": "ironframe-outerfortress-cart-01",
              "abiProfile": "6502",
              "currentDecompTier": "D5",
              "goalDecompTier": "D5",
              "d4ParityTier": "D4b",
              "binaries": [
                { "name": "bank_main.prg", "kind": "prg", "sha256": "0000000000000000000000000000000000000000000000000000000000000000" }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(root, "Dev", "schemas", "d4_build_report.json"), """
            {
              "compilerErrorCount": 0,
              "compilerWarningCount": 0,
              "craftsmanshipTellCount": 0,
              "zeroWarningsVerified": true,
              "compilerFlags": ["-std=c99", "-Wall", "-Wextra", "-Wno-unused-parameter"],
              "isPassed": true
            }
            """);
        File.WriteAllText(Path.Combine(root, "Dev", "schemas", "d5_byte_match.json"), """
            {
              "passed": true,
              "matchedBinaries": 1,
              "mismatchedBytes": 0,
              "romBytes": 131088
            }
            """);
        return root;
    }

    [Fact]
    public async Task DecompProvider_RunsD0ToD5CleanRoomPipelineAndRejectsRomLeaks()
    {
        string temp = TestPaths.CreateTempDir("recart_decomp_pipe");
        try
        {
            string pkgDir = Path.Combine(temp, "pkg");
            string romDir = Path.Combine(temp, "byo", "data", "rom");
            string srcDir = Path.Combine(temp, "clean_c");
            string outDir = Path.Combine(temp, "out");
            Directory.CreateDirectory(pkgDir);
            Directory.CreateDirectory(romDir);
            Directory.CreateDirectory(srcDir);

            SyntheticCleanRoomCartPackage synth = SyntheticCleanRoomCartBuilder.BuildPackage();
            File.WriteAllBytes(Path.Combine(romDir, "ironframe.nes"), synth.RomBytes);
            File.WriteAllText(Path.Combine(pkgDir, "module.decomp.json"), $$"""
                {
                  "schemaVersion": 1,
                  "moduleId": "{{synth.Manifest.ModuleId}}",
                  "title": "{{synth.Manifest.Title}}",
                  "platformId": "nes",
                  "engineId": "reCart",
                  "compatibleEngineTrack": "0.1.x",
                  "version": "0.1.0",
                  "expectedSha256": "{{synth.RomSha256Hex}}"
                }
                """);
            File.WriteAllText(Path.Combine(srcDir, "tactical_loop.c"),
                "// @module: tactical_loop\n// Advances tactical frame counter cleanly\n#include <stdint.h>\nuint8_t advance_frame_counter(uint8_t frame_index) { /* Wrap frame counter */ return (uint8_t)(frame_index + 1); }\n");
            File.WriteAllText(Path.Combine(srcDir, "d4_build_report.json"),
                """{"compilerErrors":0,"compilerWarnings":0,"zeroWarningsVerified":true,"compilerFlags":["-Wall","-Wextra","-Wno-unused-parameter"],"parityPassed":true}""");
            File.WriteAllText(Path.Combine(srcDir, "d5_byte_match.json"),
                $$"""{"passed":true,"matchedBinaries":1,"mismatchedBytes":0,"romBytes":{{synth.RomBytes.Length}}}""");

            var provider = new CartDecompPlatformProvider();
            var opts = new DecompRunOptions(
                pkgDir,
                outDir,
                Path.Combine(temp, "byo"),
                DecompStage.D5,
                PlatformArguments: new Dictionary<string, string>
                {
                    ["d4SourcesDir"] = srcDir,
                    ["d4BuildReportPath"] = Path.Combine(srcDir, "d4_build_report.json"),
                    ["d5ByteMatchPath"] = Path.Combine(srcDir, "d5_byte_match.json")
                });
            DecompStageResult result = await provider.RunDecompAsync(opts);
            Assert.True(result.Success, result.Message);
            Assert.True(provider.ValidateCleanRoomOutput(outDir).IsValid);

            // Clean-room guard rejects dropped retail .nes inside output directory
            File.WriteAllBytes(Path.Combine(outDir, "leak.nes"), synth.RomBytes);
            Assert.False(provider.ValidateCleanRoomOutput(outDir).IsValid);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }
}
