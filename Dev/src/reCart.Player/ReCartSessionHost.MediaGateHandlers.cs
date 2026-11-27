// @module: reCart.Player.ReCartSessionHost.MediaGateHandlers
// @purpose: Wires v0.6.2 FirstRunMediaGateView callbacks, .recomp module selection, raw-ROM clean-room guidance, settings hydration, and background AOT activation.
// @depends-on: System, System.IO, System.Linq, System.Threading, System.Threading.Tasks, reCart.Compiler, reCart.Contracts, reEngine.Host.Contracts, reEngine.Host.Core
// @exports: ReCartSessionHost (partial)

using Foundation.Core;
using reCart.Compiler;
using reCart.Contracts;
using reEngine.Core.Bootstrap;
using reEngine.Host.Contracts;
using reEngine.Host.Core;

namespace reCart.Player;

internal sealed partial class ReCartSessionHost
{
    private LoadedCartSessionInput LoadCartInputsForSession(PlayerCliOptions options, out string? initialLoadError)
    {
        initialLoadError = null;
        try
        {
            return LoadCartInputs(options);
        }
        catch (InvalidDataException ex) when (options.Frames is null && !options.Headless)
        {
            initialLoadError = ex.Message;
            PlayerCliOptions noRomOpts = options with { RomPath = null, PreloadMediaPath = null };
            string? openPath = noRomOpts.OpenPath;
            CartPackageManifest? mf = null;
            if (!string.IsNullOrWhiteSpace(openPath) && File.Exists(openPath))
            {
                try { mf = openPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? ReadAndValidateManifestJson(File.ReadAllText(openPath)) : ReadAndValidateRecompZip(openPath); }
                catch (Exception readEx) { System.Diagnostics.Trace.WriteLine(readEx.Message); }
            }
            string modId = mf?.ModuleId ?? CartEngineConstants.AppId;
            string modTitle = mf?.DisplayName ?? $"{CartEngineConstants.DisplayName} v{CartEngineConstants.Version}";
            return new LoadedCartSessionInput(mf, openPath, null, null, null, null, modId, modTitle, "BYO .nes / .prg / .zip / .7z (data\\rom\\)", Path.Combine(options.Environment.DataDirectory, "rom"));
        }
    }

    internal HostSessionOptions BuildHostSessionOptions(
        LoadedCartSessionInput loaded,
        CartRegionTimingProfile timing,
        HostDrawerDescriptor descriptor,
        string backendId,
        string? initialError = null,
        HostAotCompileState? initialAotState = null)
    {
        _ = backendId;
        string expectedSha = loaded.RomSha256Hex
            ?? loaded.Manifest?.RequiredDiscChecksums.FirstOrDefault(c => c.IsRequired)?.ExpectedSha256
            ?? loaded.Manifest?.RequiredDiscChecksums.FirstOrDefault()?.ExpectedSha256
            ?? "BYO-SHA256";

        return new HostSessionOptions
        {
            ModuleId = _moduleId,
            WindowTitle = $"{loaded.ModuleTitle} — {CartEngineConstants.DisplayName} v{CartEngineConstants.Version}",
            WindowWidth = 1280,
            WindowHeight = 720,
            ContentWidth = CartEngineConstants.FrameWidth,
            ContentHeight = CartEngineConstants.FrameHeight,
            TargetFps = _options.TargetFps ?? timing.FrameRateHz,
            HeadlessFrameCount = _options.Frames,
            DumpFramePath = _options.DumpScreenshotPath,
            DumpAudioPath = _options.DumpAudioPath,
            AutomationPort = _options.AutomationPort,
            AutomationToken = _options.AutomationToken,
            AllowedSnapshotRoot = _options.Environment.Sandbox.IsSandboxActive ? _options.Environment.Sandbox.SandboxRoot : _options.Environment.DataDirectory,
            RequireVisibleWindow = !_options.Headless && !_options.TrayOnly,
            ShowMediaGateInitially = ShouldShowMediaGateInitially(_options, loaded),
            ExpectedMediaSha256 = expectedSha,
            InitialMediaGateError = initialError,
            InitialAotCompileState = initialAotState,
            SettingsDataDirectory = _options.Environment.DataDirectory,
            SettingsConfigPathOverride = _options.Environment.ConfigFilePath,
            PreferExplicitBackend = _options.HasExplicitBackend,
            PreferExplicitScale = _options.HasExplicitScale,
            PreferExplicitAspectRatio = _options.HasExplicitAspectMode,
            IsDemoCartMode = _options.DemoCart,
            FrameRateMode = _options.FrameRateMode,
            IsVsyncEnabled = _options.IsVsyncEnabled ?? false,
            OpenSettingsInitially = _options.OpenSettingsInitially,
            ShowFpsInitially = _options.ShowFpsInitially,
            ShouldEnableRuntimeLogs = _options.ShouldEnableRuntimeLogs,
            ControlsConfigPath = Path.Combine(_options.Environment.DataDirectory, "config", "controls.json"),
            PreferredBackend = PlayerCliOptions.ToHostBackendKind(backendId),
            RequireBackend = _options.RequireBackend,
            IsFullscreen = _options.Fullscreen,
            AudioDriverOverride = _options.AudioDriverOverride,
            HdPacksDirectory = Path.Combine(_options.Environment.DataDirectory, "hdpacks"),
            IsHdTexturePackEnabled = _options.EnableHdPacks,
            AspectRatioMode = _aspectRatioMode,
            OverscanCropPx = _overscanCrop,
            IsFastForwardEnabled = _isFastForward,
            ContentPixelAspectRatio = timing.DefaultPixelAspectRatio,
            DrawerDescriptor = descriptor,
            OnBrowseMediaRequested = () => HostNativeFileDialog.ShowSelectMediaDialog(),
            OnBrowseModuleRequested = () => HostNativeFileDialog.ShowSelectModuleDialog(),
            OnFileDropped = HandleMediaFileDropped,
            OnDemoCartRequested = HandleDemoCartRequested,
#if RECART_ENABLE_AUTOMATION
            AutomationBridgeFactory = () => new PlayerAutomationBridge(1280, 720),
#endif
            SessionEventCallbacks = this
        };
    }

    internal static bool ShouldShowMediaGateInitially(PlayerCliOptions options, LoadedCartSessionInput loaded)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loaded);
        return loaded.RomBytes is null && !options.DemoCart;
    }

    internal static bool RequiresRecompModuleForRom(LoadedCartSessionInput loaded, PlayerCliOptions options, CartExecutionTierMode tier) =>
        loaded.RomBytes is not null &&
        loaded.Manifest is null &&
        string.IsNullOrWhiteSpace(loaded.RecompPackagePath) &&
        !options.DemoCart &&
        tier != CartExecutionTierMode.InterpreterOracle;

    internal static string BuildMissingRecompGuidance(string mediaName)
    {
        string file = Path.GetFileName(mediaName);
        return $"No .recomp module loaded for '{file}'. Static AOT requires a matching .recomp package — click 'Select .recomp Module...' (or drop a .recomp file), or click 'Launch Demo / Test Cart'. (Tier 3 JIT brings up raw ROMs in reCart v0.2.0.)";
    }

    internal string? HandleMediaFileDropped(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "No media file selected.";
        string fullPath = Path.GetFullPath(path.Trim());
        if (!File.Exists(fullPath)) return $"Selected file does not exist: '{fullPath}'.";

        if (fullPath.EndsWith(".recomp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(fullPath), "module.recomp.json", StringComparison.OrdinalIgnoreCase))
        {
            return HandleModuleSelectedOrDropped(fullPath);
        }

        LoadedCartSessionInput current = _loaded ??= LoadCartInputsForSession(_options with { RomPath = null, PreloadMediaPath = null }, out _);
        if (current.Manifest is null && string.IsNullOrWhiteSpace(current.RecompPackagePath) && !_options.DemoCart && _executionTier != CartExecutionTierMode.InterpreterOracle)
        {
            _pendingExternalRomPath = fullPath;
            string guidance = BuildMissingRecompGuidance(fullPath);
            Log("WARN", guidance);
            return guidance;
        }

        return LoadAndActivateMediaForModule(fullPath, current);
    }

    private string? HandleModuleSelectedOrDropped(string fullModulePath)
    {
        try
        {
            CartPackageManifest mf = fullModulePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? ReadAndValidateManifestJson(File.ReadAllText(fullModulePath))
                : ReadAndValidateRecompZip(fullModulePath);

            string? modRoot = ResolveModuleRootFromPackageFile(fullModulePath);
            string dataRomDir = Path.Combine(_options.Environment.DataDirectory, "rom");
            string? matchedRom = null;

            if (!string.IsNullOrWhiteSpace(_pendingExternalRomPath) && File.Exists(_pendingExternalRomPath))
            {
                try
                {
                    _ = LoadAndVerifyRomFile(_pendingExternalRomPath, mf);
                    matchedRom = _pendingExternalRomPath;
                }
                catch { matchedRom = null; }
            }

            matchedRom ??= DiscoverRomCandidate(_options, mf, modRoot, dataRomDir);
            _moduleId = SanitizeSlug(mf.ModuleId);
            _mapperId = mf.MapperId;
            _submapperId = mf.SubmapperId;

            var baseLoaded = new LoadedCartSessionInput(
                mf, fullModulePath, null, null, null, null,
                mf.ModuleId, mf.DisplayName,
                "BYO .nes / .prg / .zip / .7z (data\\rom\\)", dataRomDir);
            _loaded = baseLoaded;

            CartRegionTimingProfile timing = CartRegionTimingProfile.ForRegion(mf.VideoRegionStandard);
            HostDrawerDescriptor updatedDesc = PlayerF1DrawerBuilder.Build(
                mf.DisplayName, baseLoaded.MediaLabel, _options.BackendId, _options.ResolutionScale,
                _mapperId, _submapperId, timing, _executionTier, _overscanCrop, _isFastForward,
                _controllerDpadMode, _isFourScoreEnabled, FormatTurboRate(_turboFramesInterval));
            _runner?.UpdateDrawerDescriptor(updatedDesc);

            if (!string.IsNullOrWhiteSpace(matchedRom))
            {
                _pendingExternalRomPath = null;
                return LoadAndActivateMediaForModule(matchedRom, baseLoaded);
            }

            string expectedSha = mf.RequiredDiscChecksums.FirstOrDefault(c => c.IsRequired)?.ExpectedSha256
                ?? mf.RequiredDiscChecksums.FirstOrDefault()?.ExpectedSha256
                ?? "BYO-SHA256";
            _runner?.SetMediaGateState(true, expectedSha, $"Loaded module '{mf.DisplayName}' — drop or select your verified ROM.", null);
            LastStatusMessage = $"Loaded .recomp module '{mf.DisplayName}'; awaiting verified ROM.";
            Log("INFO", LastStatusMessage);
            return null;
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or UnauthorizedAccessException or IOException)
        {
            Log("WARN", $"Module package validation failed for '{fullModulePath}': {ex.Message}");
            return ex.Message;
        }
    }

    private string? LoadAndActivateMediaForModule(string fullPath, LoadedCartSessionInput current)
    {
        try
        {
            if (_moduleId == CartEngineConstants.AppId && !string.IsNullOrWhiteSpace(current.ModuleId))
                _moduleId = SanitizeSlug(current.ModuleId);

            (byte[] romBytes, string romSha, CartRomHeader header) = LoadAndVerifyRomFile(fullPath, current.Manifest);
            string stagedPath = CartMediaArchiveLoader.StageVerifiedMediaIfExternal(fullPath, current.PreloadRomDirectory) ?? fullPath;
            var updated = new LoadedCartSessionInput(
                current.Manifest, current.RecompPackagePath, stagedPath,
                romBytes, romSha, header, current.ModuleId, current.ModuleTitle,
                Path.GetFileName(stagedPath), current.PreloadRomDirectory);

            _loaded = updated;
            _demoCartActivated = false;
            _mapperId = header.MapperId;
            _submapperId = header.SubmapperId;
            _activeRegion = ResolveEffectiveRegion(_options, updated);
            _runner?.SetMediaGateState(false, romSha, $"Verified: {Path.GetFileName(stagedPath)}", null);
            TriggerRomActivation(updated);
            LastStatusMessage = $"Loaded BYO ROM '{Path.GetFileName(stagedPath)}' ({romSha[..8]}).";
            Log("INFO", LastStatusMessage);
            return null;
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or UnauthorizedAccessException or IOException)
        {
            string reason = ex.Message;
            Log("WARN", $"Media validation failed for '{fullPath}': {reason}");
            return reason;
        }
    }

    private void HandleDemoCartRequested()
    {
        try
        {
            PlayerCliOptions demoOpts = _options with { DemoCart = true, OpenPath = null, RomPath = null, PreloadMediaPath = null };
            LoadedCartSessionInput demoLoaded = LoadCartInputs(demoOpts);
            _loaded = demoLoaded;
            _moduleId = SanitizeSlug(demoLoaded.ModuleId);
            _mapperId = demoLoaded.RomHeader?.MapperId ?? demoLoaded.Manifest?.MapperId ?? 0;
            _submapperId = demoLoaded.RomHeader?.SubmapperId ?? demoLoaded.Manifest?.SubmapperId ?? 0;
            _demoCartActivated = true;
            TriggerRomActivation(demoLoaded);
            LastStatusMessage = "Clean-Room Demo Cart active.";
            Log("INFO", LastStatusMessage);
        }
        catch (Exception ex)
        {
            _demoCartActivated = true;
            Log("WARN", $"Demo Cart AOT compile fell back to synthetic test frame: {ex.Message}");
        }
    }

    public void OnChangeMediaRequested(string? selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath)) return;
        string? err = HandleMediaFileDropped(selectedPath);
        if (!string.IsNullOrWhiteSpace(err))
        {
            LastStatusMessage = err;
            _runner?.SetMediaGateState(true, _loaded?.RomSha256Hex, "Media selection failed.", err);
        }
    }

    private void TriggerRomActivation(LoadedCartSessionInput loaded)
    {
        if (_runner is not null && !_options.Headless && !_options.Frames.HasValue)
        {
            StartBackgroundRomActivation(loaded, _sessionCt);
        }
        else
        {
            ActivateLoadedRomAsync(loaded, null, _sessionCt).GetAwaiter().GetResult();
        }
    }

    private void StartBackgroundRomActivation(LoadedCartSessionInput loaded, CancellationToken sessionCt)
    {
        _activationCts?.Cancel();
        _activationCts?.Dispose();
        _activationCts = CancellationTokenSource.CreateLinkedTokenSource(sessionCt);
        CancellationToken token = _activationCts.Token;

        _runner?.SetAotCompileState(new HostAotCompileState(true, "Preparing Native Module...", 0.12, $"Checking AOT cache for '{loaded.ModuleTitle}'..."));
        _activationTask = Task.Run(async () =>
        {
            try
            {
                await ActivateLoadedRomAsync(loaded, state => _runner?.SetAotCompileState(state), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) { System.Diagnostics.Trace.WriteLine($"Background AOT activation canceled: {ex.Message}"); }
            catch (Exception ex)
            {
                Log("ERROR", $"Background AOT activation failed: {ex.Message}");
                _runner?.SetMediaGateState(true, loaded.RomSha256Hex, "Clean-room AOT compile failed.", ex.Message);
            }
            finally
            {
                _runner?.SetAotCompileState(new HostAotCompileState(false, "Ready", 1.0));
            }
        }, token);
    }

    private async Task CancelAndWaitBackgroundActivationAsync()
    {
        try { _activationCts?.Cancel(); } catch (ObjectDisposedException ex) { System.Diagnostics.Trace.WriteLine($"Background AOT CTS already disposed: {ex.Message}"); }
        if (_activationTask is { } task)
        {
            try { await task.ConfigureAwait(false); } catch (OperationCanceledException ex) { System.Diagnostics.Trace.WriteLine($"Background AOT wait canceled: {ex.Message}"); }
        }
        _activationCts?.Dispose();
        _activationCts = null;
        _activationTask = null;
    }

    private async Task ActivateLoadedRomAsync(LoadedCartSessionInput loaded, Action<HostAotCompileState>? progressSink, CancellationToken ct)
    {
        ICartMachineRuntime? nextRuntime = _runtimeFactory?.Invoke(_activeRegion, _executionTier, loaded.RomBytes);

        if (nextRuntime is not null && _compilerPipeline is not null && loaded.RomBytes is not null && loaded.RomSha256Hex is not null && _executionTier != CartExecutionTierMode.InterpreterOracle)
        {
            string? packageToCompile = loaded.RecompPackagePath;
            CartPackageManifest? effectiveManifest = loaded.Manifest;
            if (!string.IsNullOrWhiteSpace(packageToCompile) && File.Exists(packageToCompile) && effectiveManifest is not null)
            {
                progressSink?.Invoke(new HostAotCompileState(true, "Lifting & Compiling Module...", 0.30, "Translating clean-room recipe into local AOT cache..."));
                double ratio = 0.35;
                void ReportCompileLine(string msg)
                {
                    Log("AOT", msg);
                    if (progressSink is null) return;
                    if (msg.Contains("Cache hit", StringComparison.OrdinalIgnoreCase)) ratio = 0.90;
                    else if (msg.Contains("Ready", StringComparison.OrdinalIgnoreCase)) ratio = 0.95;
                    else ratio = Math.Min(0.85, ratio + 0.10);
                    progressSink(new HostAotCompileState(true, "Preparing Native Module...", ratio, ReEngineTelemetryFormatter.StripHeartbeatPrefix(msg)));
                }

                Task<ICartRecompGameModule> compileTask = _compilerPipeline is ColdAotCacheCompiler coldCompiler
                    ? coldCompiler.CompileAndLoadAsync(packageToCompile, loaded.RomSha256Hex, _options.Environment.DataDirectory, loaded.RomBytes, ReportCompileLine, ct)
                    : _compilerPipeline.CompileAndLoadAsync(packageToCompile, loaded.RomSha256Hex, _options.Environment.DataDirectory, ReportCompileLine, ct);

                ICartRecompGameModule module = await AwaitWithResponsivePumpAsync(
                    compileTask,
                    msg => Log("HEARTBEAT", msg),
                    ct).ConfigureAwait(false);

                nextRuntime.MountStaticModule(module, effectiveManifest, loaded.PreloadRomDirectory, _saveDir);
            }
        }

        lock (_runtimeSync)
        {
            _runtime?.Dispose();
            _runtime = nextRuntime;
        }
    }
}
