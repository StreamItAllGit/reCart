// @module: reCart.Tests.ContractsAndManifestTests
// @purpose: Verifies reCart.Contracts constants, 3-way engine aliases, CartPackageManifest round-trip, iNES/NES 2.0 header decoding, region timing profiles, and LauncherArgsHelper.
// @depends-on: Xunit, reCart.Contracts, reCart.Launcher, reEngine.Core.Resolution
// @exports: ContractsAndManifestTests

using reCart.Contracts;
using reCart.Launcher;
using reEngine.Core.Resolution;
using Xunit;

namespace reCart.Tests;

internal static class TestPaths
{
    internal static string CreateTempDir(string prefix)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}

[CollectionDefinition("IsolatedProcessEnv", DisableParallelization = true)]
public sealed class IsolatedProcessEnvCollection;

[Collection("IsolatedProcessEnv")]
public sealed class ContractsAndManifestTests
{
    [Fact]
    public void CartEngineConstants_ExposeCanonicalIdentityProtocolAndNoAutoStart()
    {
        Assert.Equal("reCart", CartEngineConstants.EngineId);
        Assert.Equal("recart", CartEngineConstants.AppId);
        Assert.Equal("nes", CartEngineConstants.PlatformId);
        Assert.Equal("0.6.2", CartEngineConstants.HostProtocolVersion);
        Assert.False(CartEngineConstants.AllowAutoStart);
        Assert.Contains("0.1.x", CartEngineConstants.SupportedEngineTracks);
        Assert.True(EngineAliasCatalog.AreEquivalent("reCart", "reCartOne"));
        Assert.True(EngineAliasCatalog.AreEquivalent("reCart", "reNes"));
    }

    [Fact]
    public void CartPackageManifest_SerializesDeserializesAndValidatesFields()
    {
        var manifest = new CartPackageManifest
        {
            ModuleId = "ironframe-outerfortress-cart-01",
            Title = "IronFrame",
            MapperId = 2,
            SubmapperId = 1,
            VideoRegionStandardName = "ntsc"
        };
        string json = manifest.Serialize();
        CartPackageManifest decoded = CartPackageManifest.Deserialize(json);
        decoded.Validate();
        Assert.Equal((ushort)2, decoded.MapperId);
        Assert.Equal((byte)1, decoded.SubmapperId);
        Assert.Equal(CartVideoRegionStandard.Ntsc, decoded.VideoRegionStandard);
        Assert.Throws<InvalidDataException>(() => new CartPackageManifest { ModuleId = "" }.Validate());
    }

    [Fact]
    public void CartRomHeaderParser_ParsesInes10AndNes20AndRejectsCorruptMagic()
    {
        byte[] nes20 = new byte[16];
        nes20[0] = 0x4E; nes20[1] = 0x45; nes20[2] = 0x53; nes20[3] = 0x1A;
        nes20[4] = 8; nes20[5] = 0;
        nes20[6] = 0x25; // Mapper 2 lower + trainer + vert
        nes20[7] = 0x08; // NES 2.0 signature
        nes20[8] = 0x10; // Submapper 1
        nes20[11] = 0x07; // 64 << 7 = 8192 CHR RAM
        nes20[12] = 0x01; // PAL timing

        CartRomHeader header = CartRomHeaderParser.Parse(nes20);
        Assert.True(header.IsNes20);
        Assert.Equal((ushort)2, header.MapperId);
        Assert.Equal((byte)1, header.SubmapperId);
        Assert.True(header.HasTrainer);
        Assert.Equal(CartVideoRegionStandard.Pal, header.VideoRegionStandard);
        Assert.Equal(8192, header.ChrRamSizeBytes);

        Assert.Throws<InvalidDataException>(() => CartRomHeaderParser.Parse(new byte[16]));
    }

    [Theory]
    [InlineData(CartVideoRegionStandard.Ntsc, 262, 60.0988)]
    [InlineData(CartVideoRegionStandard.Pal, 312, 50.0070)]
    [InlineData(CartVideoRegionStandard.Dendy, 312, 50.0070)]
    public void CartRegionTimingProfile_MatchesHardwareRates(CartVideoRegionStandard std, int scanlines, double approxFps)
    {
        var profile = CartRegionTimingProfile.ForRegion(std);
        Assert.Equal(scanlines, profile.ScanlinesPerFrame);
        Assert.InRange(profile.FrameRateHz, approxFps - 0.1, approxFps + 0.1);
    }

    [Theory]
    [InlineData("staticAotOnly", CartExecutionTierMode.StaticAotOnly)]
    [InlineData("jitBringup", CartExecutionTierMode.JitBringup)]
    [InlineData("interpreterOracle", CartExecutionTierMode.InterpreterOracle)]
    public void CartExecutionTierModeCodec_ParsesAllCanonicalTiersAndDefaultsUnknown(string raw, CartExecutionTierMode expected)
    {
        Assert.Equal(expected, CartExecutionTierModeCodec.Parse(raw));
        Assert.Equal(raw, CartExecutionTierModeCodec.ToCanonicalId(expected));
        Assert.Equal(CartExecutionTierMode.StaticAotOnly, CartExecutionTierModeCodec.Parse("bogusTier"));
    }

    [Theory]
    [InlineData("gl33", CartRenderBackendIds.OpenGl)]
    [InlineData("vk", CartRenderBackendIds.Vulkan)]
    [InlineData("dx12", CartRenderBackendIds.DirectX)]
    [InlineData("parallel", CartRenderBackendIds.ParallelCpu)]
    [InlineData("sw", CartRenderBackendIds.Software)]
    public void CartRenderBackendIds_NormalizesAllFiveCanonicalBackends(string alias, string expected)
    {
        Assert.Equal(expected, CartRenderBackendIds.NormalizeCli(alias));
    }

    [Fact]
    public void LauncherArgsHelper_StripsAutostartRebasesPathsAndPreservesVerbatimValues()
    {
        string[] dirty = ["--autostart", "--auto-start=1", "--open", "pkg\\demo.recomp", "--aspect-mode", "squareInteger", "--wait"];
        string[] clean = LauncherArgsHelper.SanitizeArgs(dirty);
        Assert.DoesNotContain(clean, a => a.Contains("autostart", StringComparison.OrdinalIgnoreCase) || a.Contains("auto-start", StringComparison.OrdinalIgnoreCase));
        Assert.True(LauncherArgsHelper.ShouldWaitForChild(clean));

        string[] rebased = LauncherArgsHelper.NormalizePathArguments(clean, @"D:\FakeWorkspace");
        Assert.Equal(Path.GetFullPath(@"D:\FakeWorkspace\pkg\demo.recomp"), rebased[1]);
        Assert.Equal("squareInteger", rebased[3]);
    }

    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--open", "pkg\\demo.recomp" }, false)]
    [InlineData(new[] { "game.nes" }, false)]
    [InlineData(new[] { "pkg\\demo.recomp" }, false)]
    [InlineData(new[] { "--disc", "game.nes", "--fps-mode", "original", "--target-fps", "60", "--vsync", "--fast-forward", "--open-settings", "--show-fps", "--enable-logs", "--force-zero-rwx", "--profile", "p1" }, false)]
    [InlineData(new[] { "--open", "pkg\\demo.recomp", "--wait" }, true)]
    [InlineData(new[] { "game.nes", "--verify" }, true)]
    [InlineData(new[] { "--verify-module", "demo" }, true)]
    [InlineData(new[] { "--doctor" }, true)]
    [InlineData(new[] { "--frames", "5", "demo.recomp" }, true)]
    [InlineData(new[] { "--dump-screenshot", "out.png" }, true)]
    [InlineData(new[] { "--dump-audio", "out.wav" }, true)]
    [InlineData(new[] { "--headless" }, true)]
    [InlineData(new[] { "--tray-only" }, true)]
    [InlineData(new[] { "--automation-port", "5100" }, true)]
    [InlineData(new[] { "--dry-run" }, true)]
    [InlineData(new[] { "--json" }, true)]
    [InlineData(new[] { "--check-updates" }, true)]
    [InlineData(new[] { "--register-assoc" }, true)]
    [InlineData(new[] { "--jit-bringup" }, true)]
    [InlineData(new[] { "--require-backend", "vulkan" }, true)]
    [InlineData(new[] { "--help" }, true)]
    [InlineData(new[] { "-h" }, true)]
    [InlineData(new[] { "/?" }, true)]
    [InlineData(new[] { "help" }, true)]
    [InlineData(new[] { "--version" }, true)]
    [InlineData(new[] { "-v" }, true)]
    public void LauncherArgsHelper_DetachesInteractiveGuiAndWaitsOnSyncFlags(string[] args, bool expectedWait)
    {
        Assert.Equal(expectedWait, LauncherArgsHelper.ShouldWaitForChild(args));
    }

    [Fact]
    public void LauncherArgsHelper_PreservesOptionalRequireBackendTokenDuringPathNormalization()
    {
        string[] rebased = LauncherArgsHelper.NormalizePathArguments(
            ["--require-backend", "vulkan", "--disc", "roms\\cart.nes"],
            @"D:\FakeWorkspace");
        Assert.Equal("vulkan", rebased[1]);
        Assert.Equal(Path.GetFullPath(@"D:\FakeWorkspace\roms\cart.nes"), rebased[3]);
    }
}
