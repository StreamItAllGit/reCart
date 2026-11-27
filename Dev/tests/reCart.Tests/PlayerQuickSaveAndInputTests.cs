// @module: reCart.Tests.PlayerQuickSaveAndInputTests
// @purpose: Verifies reCart.Player CLI/containment, Release ExitCode=2 automation gate, F1/F3/F5/F9 controls, clean-room --verify, and real headless cold-AOT --demo-cart video+audio output.
// @depends-on: System.Text.Json, Xunit, Foundation.Core, reCart.Compiler, reCart.Contracts, reCart.Player, reEngine.Host.Contracts
// @exports: PlayerQuickSaveAndInputTests

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using Foundation.Core;
using Foundation.Retro;
using reCart.Compiler;
using reCart.Contracts;
using reCart.Core.Runtime;
using reCart.Player;
using reEngine.Host.Contracts;
using Xunit;

namespace reCart.Tests;

[Collection("IsolatedProcessEnv")]
public sealed class PlayerQuickSaveAndInputTests
{
    [Fact]
    public void PlayerCliOptions_ParsesAllFlagsAndRejectsSandboxEscape()
    {
        string sandbox = TestPaths.CreateTempDir("recart_player_cli");
        string? prevSandbox = Environment.GetEnvironmentVariable("FOUNDATION_SANDBOX_ROOT");
        string? prevIso = Environment.GetEnvironmentVariable("APP_ISOLATED_TEST");
        Environment.SetEnvironmentVariable("APP_ISOLATED_TEST", null);
        Environment.SetEnvironmentVariable("FOUNDATION_SANDBOX_ROOT", sandbox);
        try
        {
            PlayerCliOptions opts = PlayerCliOptions.Parse(
            [
                "--demo-cart", "--backend", "opengl", "--aspect-mode", "squareInteger",
                "--scale", "4", "--region", "ntsc", "--overscan", "8,8,0,0",
                "--data-dir", Path.Combine(sandbox, "data"), "--log-dir", Path.Combine(sandbox, "logs")
            ]);
            Assert.True(opts.DemoCart);
            Assert.Equal(HostAspectRatioMode.SquareInteger, opts.AspectRatioMode);
            Assert.Equal(4, opts.ResolutionScale);
            Assert.Equal(new HostOverscanCrop(8, 8, 0, 0), opts.OverscanCrop);

            Assert.ThrowsAny<Exception>(() => PlayerCliOptions.Parse(["--data-dir", @"C:\OutsideSandbox\data"]));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FOUNDATION_SANDBOX_ROOT", prevSandbox);
            Environment.SetEnvironmentVariable("APP_ISOLATED_TEST", prevIso);
            Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void PlayerF1DrawerBuilder_IncludesMapperRegionControlsAndNesOptions()
    {
        HostDrawerDescriptor desc = PlayerF1DrawerBuilder.Build(
            "IronFrame", "demo", "opengl", 2, 2, 1,
            CartRegionTimingProfile.Ntsc, CartExecutionTierMode.StaticAotOnly,
            new HostOverscanCrop(8, 8, 0, 0), false, "D-Pad + Stick", true, "15 Hz");

        Assert.NotNull(desc.ExtraRows);
        Assert.NotNull(desc.ControlOptions);
        Assert.Equal(4, desc.ExtraRows.Count);
        Assert.Contains(desc.ExtraRows, r => r.RowId == "Mapper" && r.Value.Contains("2.1"));
        Assert.Contains(desc.ExtraRows, r => r.RowId == "Region");
        Assert.Contains(desc.ExtraRows, r => r.RowId == "ExecutionTier" && r.Value.Contains("AOT"));
        Assert.Contains(desc.ExtraRows, r => r.RowId == "OverscanCrop");
        Assert.DoesNotContain(desc.ExtraRows, r => r.RowId is "FastForward" or "CutsceneSkip");
        Assert.Equal(3, desc.ControlOptions.Count);
        Assert.Contains(desc.ControlOptions, c => c.OptionId == "fourScore" && c.SelectedChoice == "On");
    }

    [Fact]
    public void PlayerCliOptions_ParsesParityFlagsAndWiresHostSessionOptions()
    {
        string temp = TestPaths.CreateTempDir("recart_parity_cli");
        try
        {
            string romPath = Path.Combine(temp, "cart.nes");
            File.WriteAllBytes(romPath, SyntheticCleanRoomCartBuilder.BuildPackage().RomBytes);

            PlayerCliOptions opts = PlayerCliOptions.Parse(
            [
                "--disc", romPath, "--tray-only", "--fps-mode", "original", "--target-fps", "120",
                "--vsync", "--fast-forward", "--open-settings", "--show-fps", "--enable-logs",
                "--doctor", "--force-zero-rwx", "--jit-bringup", "--profile", "speedrun",
                "--require-backend", "software",
                "--data-dir", Path.Combine(temp, "data"), "--log-dir", Path.Combine(temp, "logs")
            ]);

            Assert.Equal(Path.GetFullPath(romPath), opts.RomPath);
            Assert.True(opts.TrayOnly);
            Assert.Equal(HostFrameRateMode.Original, opts.FrameRateMode);
            Assert.Equal(120, opts.TargetFps);
            Assert.True(opts.IsVsyncEnabled);
            Assert.True(opts.IsFastForwardEnabled);
            Assert.True(opts.OpenSettingsInitially);
            Assert.True(opts.ShowFpsInitially);
            Assert.True(opts.ShouldEnableRuntimeLogs);
            Assert.True(opts.Doctor);
            Assert.True(opts.ForceZeroRwx);
            Assert.Equal(CartExecutionTierMode.StaticAotOnly, opts.ExecutionTier);
            Assert.Equal("speedrun", opts.Profile);
            Assert.True(opts.RequireBackend);
            Assert.Equal(CartRenderBackendIds.Software, opts.BackendId);

            PlayerCliOptions noVsync = PlayerCliOptions.Parse(
                ["--no-vsync", "--require-backend=opengl", "--data-dir", Path.Combine(temp, "data"), "--log-dir", Path.Combine(temp, "logs")]);
            Assert.False(noVsync.IsVsyncEnabled);
            Assert.True(noVsync.RequireBackend);
            Assert.Equal(CartRenderBackendIds.OpenGl, noVsync.BackendId);

            var session = new ReCartSessionHost(opts);
            HostDrawerDescriptor desc = PlayerF1DrawerBuilder.Build("Test", "cart.nes", opts.BackendId, opts.ResolutionScale, 0, 0, CartRegionTimingProfile.Ntsc, opts.ExecutionTier, opts.OverscanCrop, opts.IsFastForwardEnabled);
            var loaded = new ReCartSessionHost.LoadedCartSessionInput(null, null, romPath, null, null, null, "test", "Test", "cart.nes", Path.Combine(temp, "data", "rom"));
            HostSessionOptions hostOpts = session.BuildHostSessionOptions(loaded, CartRegionTimingProfile.Ntsc, desc, opts.BackendId);

            Assert.False(hostOpts.RequireVisibleWindow);
            Assert.Equal(HostFrameRateMode.Original, hostOpts.FrameRateMode);
            Assert.Equal(120.0, hostOpts.TargetFps);
            Assert.True(hostOpts.IsVsyncEnabled);
            Assert.True(hostOpts.IsFastForwardEnabled);
            Assert.True(hostOpts.OpenSettingsInitially);
            Assert.True(hostOpts.ShowFpsInitially);
            Assert.True(hostOpts.ShouldEnableRuntimeLogs);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task ReCartSessionHost_OverscanCycleDemoAudioSilenceAndDoctorWork()
    {
        string temp = TestPaths.CreateTempDir("recart_overscan_audio");
        try
        {
            string[] dataArgs = ["--data-dir", Path.Combine(temp, "data"), "--log-dir", Path.Combine(temp, "logs"), "--save-dir", Path.Combine(temp, "saves")];
            Assert.Equal(0, await Program.RunMainAsync(["--doctor", "--backend", "software", .. dataArgs]));

            ReCartSessionHost.LoadedCartSessionInput defaultLoaded = ReCartSessionHost.LoadCartInputs(PlayerCliOptions.Parse(dataArgs));
            Assert.Equal($"{CartEngineConstants.DisplayName} v{CartEngineConstants.Version}", defaultLoaded.ModuleTitle);

            PlayerCliOptions opts = PlayerCliOptions.Parse(["--demo-cart", .. dataArgs]);
            var host = new ReCartSessionHost(opts);

            // Overscan ExtraRow cycles 0 -> 8 -> 16 -> 0 and resets to 0 on secondary action.
            Assert.Equal(HostOverscanCrop.None, host.CurrentOverscanCrop);
            host.OnExtraRowActivated("OverscanCrop", isSecondaryAction: false);
            Assert.Equal(new HostOverscanCrop(8, 8, 8, 8), host.CurrentOverscanCrop);
            host.OnExtraRowActivated("OverscanCrop", isSecondaryAction: false);
            Assert.Equal(new HostOverscanCrop(16, 16, 16, 16), host.CurrentOverscanCrop);
            host.OnExtraRowActivated("OverscanCrop", isSecondaryAction: false);
            Assert.Equal(HostOverscanCrop.None, host.CurrentOverscanCrop);
            host.OnExtraRowActivated("OverscanCrop", isSecondaryAction: false);
            host.OnExtraRowActivated("OverscanCrop", isSecondaryAction: true);
            Assert.Equal(HostOverscanCrop.None, host.CurrentOverscanCrop);

            // Demo audio must be completely silent (maxAbs == 0) when idle and emit a brief soft chime only on input.
            byte[] fb = new byte[CartEngineConstants.FrameWidth * CartEngineConstants.FrameHeight * 4];
            short[] pcm = new short[1470];

            await host.OnFrameTickAsync(0, new HostInputFrame(0L, HostGamepadButtons.None), fb, CartEngineConstants.FrameWidth, CartEngineConstants.FrameHeight, CancellationToken.None);
            int idleSamples = host.ReadAudioSamples(pcm);
            Assert.True(idleSamples > 0);
            Assert.Equal(0, pcm.Take(idleSamples).Max(s => Math.Abs((int)s)));

            await host.OnFrameTickAsync(1, new HostInputFrame(1L, HostGamepadButtons.South), fb, CartEngineConstants.FrameWidth, CartEngineConstants.FrameHeight, CancellationToken.None);
            int activeSamples = host.ReadAudioSamples(pcm);
            Assert.True(activeSamples > 0);
            int activeMaxAbs = pcm.Take(activeSamples).Max(s => Math.Abs((int)s));
            Assert.InRange(activeMaxAbs, 1, 1000);

            await host.OnFrameTickAsync(2, new HostInputFrame(2L, HostGamepadButtons.None), fb, CartEngineConstants.FrameWidth, CartEngineConstants.FrameHeight, CancellationToken.None);
            int postIdleSamples = host.ReadAudioSamples(pcm);
            Assert.Equal(0, pcm.Take(postIdleSamples).Max(s => Math.Abs((int)s)));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Theory]
    [InlineData("nativePar", HostAspectRatioMode.NativePar)]
    [InlineData("fourByThree", HostAspectRatioMode.FourByThree)]
    [InlineData("squareInteger", HostAspectRatioMode.SquareInteger)]
    [InlineData("nativeFit", HostAspectRatioMode.NativeFit)]
    [InlineData("horPlus16By9", HostAspectRatioMode.HorPlus16By9)]
    public void PlayerCliOptions_ParsesAllFiveHostAspectModes(string rawAspect, HostAspectRatioMode expected)
    {
        string temp = TestPaths.CreateTempDir("recart_aspect_cli");
        try
        {
            PlayerCliOptions opts = PlayerCliOptions.Parse(
                ["--aspect-mode", rawAspect, "--data-dir", Path.Combine(temp, "data"), "--log-dir", Path.Combine(temp, "logs")]);
            Assert.Equal(expected, opts.AspectRatioMode);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task Program_AutomationGateAndVersionHelpAndBackendFailure()
    {
        string temp = TestPaths.CreateTempDir("recart_player_gate");
        try
        {
            string[] dataArgs = ["--data-dir", Path.Combine(temp, "data"), "--log-dir", Path.Combine(temp, "logs")];
            Assert.Equal(0, await Program.RunMainAsync(["--version", .. dataArgs]));
            Assert.Equal(0, await Program.RunMainAsync(["--help", .. dataArgs]));
            Assert.Equal(0, await Program.RunMainAsync(["--dry-run", .. dataArgs]));
            Assert.Equal(0, await Program.RunMainAsync(["--doctor", "--backend", "software", .. dataArgs]));
            Assert.Equal(6, await Program.RunMainAsync(["--verify", "--backend", "metal", "--require-backend", .. dataArgs]));

#if !RECART_ENABLE_AUTOMATION
            Assert.Equal(2, await Program.RunMainAsync(["--automation-port", "5199", .. dataArgs]));
#else
            Assert.Equal(0, await Program.RunMainAsync(["--verify", .. dataArgs]));
#endif
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void ReCartSessionHost_QuickSaveAndInputMappingWorkAcross4PortsAndTurbo()
    {
        string temp = TestPaths.CreateTempDir("recart_player_save");
        try
        {
            PlayerCliOptions opts = PlayerCliOptions.Parse(
                ["--demo-cart", "--data-dir", Path.Combine(temp, "data"), "--log-dir", Path.Combine(temp, "logs"), "--save-dir", Path.Combine(temp, "saves")]);
            var host = new ReCartSessionHost(opts);
            host.OnControlOptionChanged("fourScore", "On");
            host.OnControlOptionChanged("turboRate", "30 Hz");

            var p2Pad = new HostGamepadPortSnapshot(1, true, HostGamepadButtons.South | HostGamepadButtons.DPadRight, 0f, 0f, 0f, 0f, 0f, 0f, 0u);
            var p3Pad = new HostGamepadPortSnapshot(2, true, HostGamepadButtons.DPadUp, 0f, 0f, 0f, 0f, 0f, 0f, 0u);
            var input = new HostInputFrame(
                0L,
                HostGamepadButtons.DPadLeft | HostGamepadButtons.North,
                padPorts: [new HostGamepadPortSnapshot(0, true, HostGamepadButtons.None, 0f, 0f, 0f, 0f, 0f, 0f, 0u), p2Pad, p3Pad]);
            uint[] frame0 = host.MapHostInputToNesPorts(input, 0);
            uint[] frame1 = host.MapHostInputToNesPorts(input, 1);

            Assert.Equal(0x41u, frame0[0]); // Left + A (turbo pulse ON)
            Assert.Equal(0x40u, frame1[0]); // Left only (turbo pulse OFF on odd frame)
            Assert.Equal(0x81u, frame0[1]); // Port 1 A + Right
            Assert.Equal(0x10u, frame0[2]); // Port 2 Up via FourScore

            host.OnQuickSaveRequested();
            host.OnQuickSaveRequested();
            Assert.Equal(2, host.QuickSaveCount);
            Assert.True(File.Exists(host.LastQuickSavePath));
            Assert.True(File.Exists(host.LastQuickSavePath + ".bak"));
            host.OnQuickLoadRequested();
            Assert.Equal(1, host.QuickLoadCount);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task Player_DemoCartHeadless_RunsRealColdAotProducingNonBlankPngAndAudibleWav()
    {
        string temp = TestPaths.CreateTempDir("recart_player_demo");
        try
        {
            string dataDir = Path.Combine(temp, "data");
            string logDir = Path.Combine(temp, "logs");
            string pngPath = Path.Combine(temp, "out", "demo.png");
            string wavPath = Path.Combine(temp, "out", "demo.wav");
            Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);

            int exit = await Program.RunMainAsync(
            [
                "--demo-cart", "--headless", "--backend", "software", "--frames", "10",
                "--dump-screenshot", pngPath, "--dump-audio", wavPath,
                "--data-dir", dataDir, "--log-dir", logDir, "--save-dir", Path.Combine(temp, "saves")
            ]);
            Assert.Equal(0, exit);
            Assert.True(File.Exists(pngPath) && new FileInfo(pngPath).Length > 500, "Expected non-trivial rendered PNG.");
            Assert.Single(Directory.EnumerateFiles(Path.Combine(dataDir, "cache"), "*.Generated.dll", SearchOption.AllDirectories));

            byte[] wav = File.ReadAllBytes(wavPath);
            Assert.True(wav.Length > 44);
            int peak = 0;
            for (int i = 44; i + 1 < wav.Length; i += 2)
                peak = Math.Max(peak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(i, 2))));
            Assert.True(peak > 100, $"Expected audible APU WAV amplitude > 100, peak={peak}");
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task Player_VerifyRejectsNonCleanRoomRecompAndAcceptsCleanRoom()
    {
        string temp = TestPaths.CreateTempDir("recart_player_verify");
        try
        {
            string okZip = Path.Combine(temp, "clean.recomp");
            SyntheticCleanRoomCartBuilder.CreateRecompPackage(okZip);
            string[] baseArgs = ["--data-dir", Path.Combine(temp, "data"), "--log-dir", Path.Combine(temp, "logs")];
            Assert.Equal(0, await Program.RunMainAsync(["--verify", "--open", okZip, .. baseArgs]));

            string badZip = Path.Combine(temp, "bad.recomp");
            File.Copy(okZip, badZip);
            using (ZipArchive zip = ZipFile.Open(badZip, ZipArchiveMode.Update))
                zip.CreateEntry("lifted/extra.Generated.dll");
            Assert.NotEqual(0, await Program.RunMainAsync(["--verify", "--open", badZip, .. baseArgs]));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task CartMachineHost_StaticAotExecutesCyclePacedAcrossScanlinesAndVBlank()
    {
        string temp = TestPaths.CreateTempDir("recart_aot_cyclepaced");
        try
        {
            string dataDir = Path.Combine(temp, "data");
            string recompPath = Path.Combine(temp, "demo.recomp");
            SyntheticCleanRoomCartPackage pkg = SyntheticCleanRoomCartBuilder.CreateRecompPackage(recompPath);
            ICartRecompGameModule mod = await new ColdAotCacheCompiler().CompileAndLoadAsync(recompPath, pkg.RomSha256Hex, dataDir);

            using CartMachineHost host = CartMachineFactory.CreateFromRomBytes(pkg.RomBytes, CartVideoRegionStandard.Ntsc, CartExecutionTierMode.StaticAotOnly);
            host.MountStaticModule(mod, pkg.Manifest, Path.Combine(dataDir, "rom"), Path.Combine(temp, "saves"));
            Assert.NotEqual(0, host.Cpu.PC);

            for (int f = 0; f < 4; f++) host.StepFrame();
            Assert.True(host.Diagnostics.StaticDispatchCount > 100, $"Expected cycle-paced StaticDispatchCount > 100 across 4 frames, got {host.Diagnostics.StaticDispatchCount}");

            int nonBlack = 0;
            ReadOnlySpan<byte> rgba = host.Ppu.FrameBufferRgba;
            for (int i = 0; i < rgba.Length; i += 4)
                if (rgba[i] > 8 || rgba[i + 1] > 8 || rgba[i + 2] > 8) nonBlack++;
            Assert.True(nonBlack > 1000, $"Expected non-blank PPU frame pixels, got {nonBlack}");
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void LoadCartInputs_ResolvesDataRomInsideInstallDirWhenOpeningAppSubfolderPackage()
    {
        string temp = TestPaths.CreateTempDir("recart_app_preload");
        try
        {
            string appDir = Path.Combine(temp, "app");
            string dataDir = Path.Combine(temp, "data");
            string romDir = Path.Combine(dataDir, "rom");
            Directory.CreateDirectory(appDir);
            Directory.CreateDirectory(romDir);

            string recompPath = Path.Combine(appDir, "ironframe-outerfortress-cart-01.recomp");
            SyntheticCleanRoomCartPackage pkg = SyntheticCleanRoomCartBuilder.CreateRecompPackage(recompPath);
            string placedRom = Path.Combine(romDir, "NES-ME-USA.nes");
            File.WriteAllBytes(placedRom, pkg.RomBytes);

            PlayerCliOptions opts = PlayerCliOptions.Parse(
            [
                "--open", recompPath, "--data-dir", dataDir,
                "--config", Path.Combine(dataDir, "config", "settings.json"),
                "--log-dir", Path.Combine(temp, "logs")
            ]);
            ReCartSessionHost.LoadedCartSessionInput loaded = ReCartSessionHost.LoadCartInputs(opts);

            Assert.Equal(Path.GetFullPath(romDir), Path.GetFullPath(loaded.PreloadRomDirectory));
            Assert.Equal(Path.GetFullPath(placedRom), Path.GetFullPath(loaded.RomFilePath!));
            Assert.NotNull(loaded.RomBytes);
            Assert.Equal(pkg.RomSha256Hex, loaded.RomSha256Hex);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void MediaGate_DefaultsVisibleWhenRomMissingAndValidatesDroppedChecksum()
    {
        string temp = TestPaths.CreateTempDir("recart_gate_drop");
        try
        {
            string appDir = Path.Combine(temp, "app");
            string dataDir = Path.Combine(temp, "data");
            Directory.CreateDirectory(appDir);
            Directory.CreateDirectory(Path.Combine(dataDir, "rom"));

            SyntheticCleanRoomCartPackage pkg = SyntheticCleanRoomCartBuilder.BuildPackage();
            CartPackageManifest strictManifest = new()
            {
                SchemaVersion = 1,
                ModuleId = pkg.Manifest.ModuleId,
                Title = pkg.Manifest.Title,
                PlatformId = pkg.Manifest.PlatformId,
                Version = pkg.Manifest.Version,
                EngineId = pkg.Manifest.EngineId,
                EngineVersion = CartEngineConstants.Version,
                CompatibleEngineTrack = CartEngineConstants.CompatibleEngineTrack,
                SupportedEngineTracks = [.. CartEngineConstants.SupportedEngineTracks],
                MapperId = pkg.Manifest.MapperId,
                RequiredDiscChecksums = [new DiscChecksumEntry("data/rom/NES-ME-USA.nes", pkg.RomSha256Hex, pkg.RomBytes.Length, IsRequired: true)]
            };
            string recompPath = Path.Combine(appDir, "ironframe-outerfortress-cart-01.recomp");
            CleanRoomRecompPackager.PackageCleanRoomRecomp(pkg.RomBytes, strictManifest, recompPath);

            PlayerCliOptions opts = PlayerCliOptions.Parse(
            [
                "--open", recompPath, "--data-dir", dataDir,
                "--config", Path.Combine(dataDir, "config", "settings.json"),
                "--log-dir", Path.Combine(temp, "logs")
            ]);
            ReCartSessionHost.LoadedCartSessionInput missing = ReCartSessionHost.LoadCartInputs(opts);
            Assert.Null(missing.RomBytes);
            Assert.True(ReCartSessionHost.ShouldShowMediaGateInitially(opts, missing));

            string wrongRom = Path.Combine(temp, "wrong.nes");
            byte[] bogus = (byte[])pkg.RomBytes.Clone();
            bogus[32] ^= 0xFF;
            File.WriteAllBytes(wrongRom, bogus);

            var session = new ReCartSessionHost(
                opts,
                runtimeFactory: (r, t, bytes) => bytes is not null ? CartMachineFactory.CreateFromRomBytes(bytes, r, t) : null,
                compilerPipeline: new ColdAotCacheCompiler());

            string? rejectMsg = session.HandleMediaFileDropped(wrongRom);
            Assert.NotNull(rejectMsg);
            Assert.Contains("did not match", rejectMsg, StringComparison.OrdinalIgnoreCase);

            string validRom = Path.Combine(temp, "valid.nes");
            File.WriteAllBytes(validRom, pkg.RomBytes);
            Assert.Null(session.HandleMediaFileDropped(validRom));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }
}
