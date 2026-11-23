// @module: reCart.Tests.RecipeAndLockstepTests
// @purpose: Verifies v0.1.1 CartAotRecipeSpec, CartPrgHeaderSynthesizer, 2-member recipe .recomp local AOT compilation, .prg preload, and differential lockstep runner.
// @depends-on: System.Security.Cryptography, Xunit, Foundation.Retro, reCart.Compiler, reCart.Contracts, reCart.Player
// @exports: RecipeAndLockstepTests

using System.Security.Cryptography;
using Foundation.Retro;
using reCart.Compiler;
using reCart.Contracts;
using reCart.Player;
using Xunit;

namespace reCart.Tests;

[Collection("IsolatedProcessEnv")]
public sealed class RecipeAndLockstepTests
{
    [Fact]
    public void CartAotRecipeSpec_RoundTripsSeedsAndValidatesHex()
    {
        var spec = new CartAotRecipeSpec
        {
            ModuleId = "test-recipe-cart",
            MapperId = 2,
            EntryAddresses = ["0xC001"],
            Seeds =
            [
                new CartAotRecipeSeed(0, "0x8000", "function", "bank0_fn"),
                new CartAotRecipeSeed(7, "$C6B7", "jumpTable", "jt_c6b7"),
                new CartAotRecipeSeed(7, "0xC932", "trampoline", "tramp_c932")
            ],
            JumpTables = [new CartAotJumpTableHint(7, "0xC6B7", "inline jump table")],
            Trampolines = [new CartAotTrampolineHint("0xC932", 0, "unromSwitch")],
            MemoryWatches = [new CartAotMemoryWatchHint("actors", "0x0300", 256)]
        };

        spec.Validate();
        CartAotRecipeSpec roundTrip = CartAotRecipeSpec.Deserialize(spec.Serialize());
        IReadOnlyList<(uint PrgBank, ushort Address)> tuples = roundTrip.ToSeedTuples(fixedBankIndex: 7);

        Assert.Equal(4, tuples.Count);
        Assert.Contains((0u, (ushort)0x8000), tuples);
        Assert.Contains((7u, (ushort)0xC6B7), tuples);
        Assert.Contains((7u, (ushort)0xC932), tuples);
        Assert.Contains((7u, (ushort)0xC001), tuples);
    }

    [Fact]
    public void CartPrgHeaderSynthesizer_WrapsHeaderless128KbPrgAsUxRomAndPreservesInes()
    {
        byte[] rawPrg = new byte[131072];
        Array.Fill(rawPrg, (byte)0xEA);
        byte[] ines = CartPrgHeaderSynthesizer.SynthesizeIfHeaderless(rawPrg, mapperId: 2, submapperId: 0, CartMirroringMode.Vertical);

        Assert.Equal(131088, ines.Length);
        CartRomHeader header = CartRomHeaderParser.Parse(ines);
        Assert.Equal((ushort)2, header.MapperId);
        Assert.Equal(131072, header.PrgRomSizeBytes);
        Assert.Equal(0, header.ChrRomSizeBytes);
        Assert.Same(ines, CartPrgHeaderSynthesizer.SynthesizeIfHeaderless(ines, mapperId: 2));
    }

    [Fact]
    public async Task CleanRoomRecipeRecomp_Packages2MembersAndCompilesLocallyIntoCacheAsync()
    {
        string temp = TestPaths.CreateTempDir("recart_recipe_aot");
        try
        {
            SyntheticCleanRoomCartPackage pkg = SyntheticCleanRoomCartBuilder.BuildPackage();
            var recipe = new CartAotRecipeSpec
            {
                ModuleId = pkg.Manifest.ModuleId,
                MapperId = pkg.Manifest.MapperId,
                SubmapperId = pkg.Manifest.SubmapperId,
                EntryAddresses = ["0x8000", "0x8050"]
            };

            string recompPath = Path.Combine(temp, "app", $"{pkg.Manifest.ModuleId}.recomp");
            CleanRoomRecompPackager.PackageCleanRoomRecipeRecomp(pkg.Manifest, recipe, recompPath);

            Assert.True(CleanRoomRecompPackager.IsRecipeArchive(recompPath));
            CleanRoomArchiveValidationResult valid = CleanRoomRecompPackager.ValidateCleanRoomArchive(recompPath);
            Assert.True(valid.IsValid, valid.Summary);
            Assert.Equal(2, valid.Entries.Count);

            var compiler = new ColdAotCacheCompiler();
            ICartRecompGameModule loaded = await compiler.CompileAndLoadAsync(recompPath, pkg.RomSha256Hex, Path.Combine(temp, "data"), pkg.RomBytes);
            Assert.Equal(pkg.Manifest.ModuleId, loaded.ModuleId);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void MediaLoader_AcceptsHeaderlessPrgWithDeclaredAlternateSha256()
    {
        string temp = TestPaths.CreateTempDir("recart_prg_preload");
        try
        {
            SyntheticCleanRoomCartPackage pkg = SyntheticCleanRoomCartBuilder.BuildPackage();
            byte[] headerlessPrg = pkg.RomBytes[16..(16 + 32768)];
            string prgSha = Convert.ToHexString(SHA256.HashData(headerlessPrg)).ToLowerInvariant();

            string romDir = Path.Combine(temp, "data", "rom");
            Directory.CreateDirectory(romDir);
            File.WriteAllBytes(Path.Combine(romDir, "test_cart.prg"), headerlessPrg);

            CartPackageManifest manifest = new()
            {
                SchemaVersion = 1,
                ModuleId = pkg.Manifest.ModuleId,
                Title = pkg.Manifest.Title,
                EngineId = "reCart",
                EngineVersion = CartEngineConstants.Version,
                CompatibleEngineTrack = CartEngineConstants.CompatibleEngineTrack,
                SupportedEngineTracks = [CartEngineConstants.CompatibleEngineTrack],
                Version = pkg.Manifest.Version,
                MapperId = 0,
                SubmapperId = 0,
                RequiredDiscChecksums =
                [
                    new DiscChecksumEntry("data/rom/primary.nes", pkg.RomSha256Hex, pkg.RomBytes.Length, IsRequired: true),
                    new DiscChecksumEntry("data/rom/alt.prg", prgSha, headerlessPrg.Length, IsRequired: false)
                ]
            };

            string mfPath = Path.Combine(temp, "module.recomp.json");
            File.WriteAllText(mfPath, manifest.Serialize());

            var opts = PlayerCliOptions.Parse(["--open", mfPath, "--data-dir", Path.Combine(temp, "data"), "--config", Path.Combine(temp, "cfg.json"), "--log-dir", Path.Combine(temp, "logs")]);
            ReCartSessionHost.LoadedCartSessionInput loaded = ReCartSessionHost.LoadCartInputs(opts);

            Assert.Equal(prgSha, loaded.RomSha256Hex);
            Assert.NotNull(loaded.RomHeader);
            Assert.Equal(32768, loaded.RomHeader!.PrgRomSizeBytes);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task LockstepRunner_Verifies200StepsAgainstInterpreterOracleWithZeroMismatchesAsync()
    {
        string temp = TestPaths.CreateTempDir("recart_lockstep_runner");
        try
        {
            SyntheticCleanRoomCartPackage pkg = SyntheticCleanRoomCartBuilder.BuildPackage();
            var recipe = new CartAotRecipeSpec
            {
                ModuleId = pkg.Manifest.ModuleId,
                MapperId = pkg.Manifest.MapperId,
                EntryAddresses = ["0x8000", "0xC000"]
            };

            string recompPath = Path.Combine(temp, "app", $"{pkg.Manifest.ModuleId}.recomp");
            string romPath = Path.Combine(temp, "data", "rom", "demo.nes");
            Directory.CreateDirectory(Path.GetDirectoryName(romPath)!);
            File.WriteAllBytes(romPath, pkg.RomBytes);
            CleanRoomRecompPackager.PackageCleanRoomRecipeRecomp(pkg.Manifest, recipe, recompPath);

            var opts = PlayerCliOptions.Parse([
                "--open", recompPath,
                "--rom", romPath,
                "--lockstep-oracle", "--lockstep-steps", "200",
                "--data-dir", Path.Combine(temp, "data"),
                "--config", Path.Combine(temp, "data", "config", "settings.json"),
                "--log-dir", Path.Combine(temp, "logs")
            ]);

            Assert.True(opts.LockstepOracle);
            Assert.Equal(200, opts.LockstepSteps);
            Assert.Equal(recompPath, opts.OpenPath);

            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();
            int exit = await ReCartLockstepRunner.RunAsync(opts, new ColdAotCacheCompiler(), outWriter, errWriter);

            Assert.Equal(0, exit);
            Assert.Contains("mismatchedSteps=0", outWriter.ToString(), StringComparison.Ordinal);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void CartPackageManifest_RoundTripsCamelCaseEnumAndLegacyInt()
    {
        const string camelJson = """
        {
          "schemaVersion": 1,
          "moduleId": "enum-roundtrip-cart",
          "title": "Enum Roundtrip",
          "platformId": "nes",
          "version": "0.1.0",
          "engineId": "reCart",
          "engineVersion": "0.1.2",
          "defaultFramerateMode": "lockedCap"
        }
        """;

        CartPackageManifest parsed = CartPackageManifest.Deserialize(camelJson);
        Assert.Equal(RecompTimingMode.LockedCap, parsed.DefaultFramerateMode);

        string serialized = parsed.Serialize();
        Assert.Contains("\"defaultFramerateMode\": \"lockedCap\"", serialized, StringComparison.Ordinal);

        string legacyIntJson = camelJson.Replace("\"lockedCap\"", "0", StringComparison.Ordinal);
        CartPackageManifest parsedLegacy = CartPackageManifest.Deserialize(legacyIntJson);
        Assert.Equal(RecompTimingMode.LockedCap, parsedLegacy.DefaultFramerateMode);
    }

    [Fact]
    public void LifterOpcodeEmitter_UsesPackedUintSwitchForSwitchableBankCarts()
    {
        byte[] rawPrg = new byte[131072];
        Array.Fill(rawPrg, (byte)0xEA);
        int fixedOffset = 7 * 16384;
        rawPrg[fixedOffset + 1] = 0x78; // SEI
        rawPrg[fixedOffset + 2] = 0xD8; // CLD
        rawPrg[fixedOffset + 3] = 0x4C; // JMP $C001
        rawPrg[fixedOffset + 4] = 0x01;
        rawPrg[fixedOffset + 5] = 0xC0;
        rawPrg[fixedOffset + 0x3FFC] = 0x01;
        rawPrg[fixedOffset + 0x3FFD] = 0xC0;

        byte[] ines = CartPrgHeaderSynthesizer.SynthesizeIfHeaderless(rawPrg, mapperId: 2, submapperId: 0, CartMirroringMode.Vertical);
        var manifest = new CartPackageManifest
        {
            SchemaVersion = 1,
            ModuleId = "uxrom-switch-test",
            Title = "UxROM Switch Test",
            PlatformId = CartEngineConstants.PlatformId,
            Version = "0.1.0",
            EngineId = CartEngineConstants.EngineId,
            EngineVersion = CartEngineConstants.Version,
            MapperId = 2,
            SubmapperId = 0
        };

        LiftedProgramResult result = new Lifter6502ToCSharp().Lift(ines, manifest, [(7u, (ushort)0xC001)]);
        Assert.Contains("switch ((keyBank << 16) | address)", result.CSharpSource, StringComparison.Ordinal);
        Assert.DoesNotContain("switch ((keyBank, address))", result.CSharpSource, StringComparison.Ordinal);
    }
}
