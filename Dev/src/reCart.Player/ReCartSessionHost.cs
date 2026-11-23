// @module: reCart.Player.ReCartSessionHost
// @purpose: Drives reEngine.Host v0.6.1 HostSessionRunner with aspect-locked backends, WASAPI audio, 4-port inputs, and F1/F3/F5/F9/F11 callbacks.
// @depends-on: System, System.IO, System.Threading, System.Threading.Tasks, Foundation.Core, reCart.Contracts, reEngine.Core.Bootstrap, reEngine.Host.Contracts, reEngine.Host.Core
// @exports: ReCartSessionHost

using Foundation.Core;
using reCart.Compiler;
using reCart.Contracts;
using reCart.Core.Cpu;
using reEngine.Core.Bootstrap;
using reEngine.Host.Contracts;
using reEngine.Host.Core;

namespace reCart.Player;

internal sealed partial class ReCartSessionHost : IHostSessionCallbacks, IHostSessionEventCallbacks
{
    private readonly PlayerCliOptions _options;
    private readonly Func<CartVideoRegionStandard, CartExecutionTierMode, byte[]?, ICartMachineRuntime?>? _runtimeFactory;
    private readonly ICartCompilerPipeline? _compilerPipeline;
    private readonly CappedSharedLogSink _logSink;
    private readonly object _runtimeSync = new();

    private HostSessionRunner? _runner;
    private LoadedCartSessionInput? _loaded;
    private ICartMachineRuntime? _runtime;
    private CancellationToken _sessionCt;
    private CancellationTokenSource? _activationCts;
    private Task? _activationTask;
    private string? _pendingExternalRomPath;
    private string _saveDir;
    private string _moduleId = CartEngineConstants.AppId;
    private ushort _mapperId;
    private byte _submapperId;
    private CartVideoRegionStandard _activeRegion = CartVideoRegionStandard.Ntsc;
    private CartExecutionTierMode _executionTier = CartExecutionTierMode.StaticAotOnly;
    private HostAspectRatioMode _aspectRatioMode = HostAspectRatioMode.NativePar;
    private HostOverscanCrop _overscanCrop;
    private bool _isFullscreen;
    private bool _isFastForward;
    private bool _cutsceneSkipPending;
    private bool _demoCartActivated;
    private string _controllerDpadMode = "D-Pad + Stick";
    private bool _isFourScoreEnabled;
    private int _turboFramesInterval;
    private int _demoX = 120;
    private int _demoY = 112;
    private double _audioPhase;

    public int QuickSaveCount { get; private set; }
    public int QuickLoadCount { get; private set; }
    public string? LastQuickSavePath { get; private set; }
    public string LastStatusMessage { get; private set; } = "Initialized";

    public ReCartSessionHost(
        PlayerCliOptions options,
        Func<CartVideoRegionStandard, CartExecutionTierMode, byte[]?, ICartMachineRuntime?>? runtimeFactory = null,
        ICartCompilerPipeline? compilerPipeline = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _runtimeFactory = runtimeFactory;
        _compilerPipeline = compilerPipeline;
        _saveDir = options.SaveDirectory;
        _aspectRatioMode = options.AspectRatioMode;
        _overscanCrop = options.OverscanCrop;
        _isFullscreen = options.Fullscreen;
        _executionTier = options.ExecutionTier;
        _demoCartActivated = options.DemoCart;
        _logSink = new CappedSharedLogSink(Path.Combine(options.Environment.LogDirectory, $"{CartEngineConstants.AppId}.player.log"));
    }

    public async Task<HostSessionResult> RunAsync(CancellationToken ct = default)
    {
        _sessionCt = ct;
        LoadedCartSessionInput loaded = LoadCartInputsForSession(_options, out string? initialLoadError);
        _loaded = loaded;
        _moduleId = SanitizeSlug(loaded.ModuleId);
        _mapperId = loaded.RomHeader?.MapperId ?? loaded.Manifest?.MapperId ?? 0;
        _submapperId = loaded.RomHeader?.SubmapperId ?? loaded.Manifest?.SubmapperId ?? 0;
        _activeRegion = ResolveEffectiveRegion(_options, loaded);
        _executionTier = ResolveEffectiveExecutionTier(_options, loaded);
        string backendId = _options.HasExplicitBackend ? _options.BackendId : (loaded.Manifest?.BackendId ?? _options.BackendId);
        _aspectRatioMode = _options.HasExplicitAspectMode ? _options.AspectRatioMode
            : (!string.IsNullOrWhiteSpace(loaded.Manifest?.AspectRatioMode) ? HostAspectRatioModeCodec.Parse(loaded.Manifest.AspectRatioMode) : _options.AspectRatioMode);

        CartRegionTimingProfile timing = CartRegionTimingProfile.ForRegion(_activeRegion);
        bool interactive = !_options.Headless && !_options.Frames.HasValue;

        if (RequiresRecompModuleForRom(loaded, _options, _executionTier))
        {
            string missingMsg = BuildMissingRecompGuidance(loaded.RomFilePath ?? loaded.ModuleId);
            if (!interactive)
                throw new InvalidOperationException(missingMsg);

            _pendingExternalRomPath = loaded.RomFilePath;
            initialLoadError = missingMsg;
            Log("WARN", $"Raw-ROM launch blocked until .recomp selected: {missingMsg}");
            loaded = new LoadedCartSessionInput(
                null, null, null, null, null, null,
                CartEngineConstants.AppId,
                $"{CartEngineConstants.DisplayName} Session",
                "Select .recomp Module + Verified ROM",
                loaded.PreloadRomDirectory);
            _loaded = loaded;
            _moduleId = CartEngineConstants.AppId;
        }
        else if (!string.IsNullOrWhiteSpace(initialLoadError))
        {
            Log("WARN", $"Media preload rejected: {initialLoadError}");
        }

        try
        {
            bool hasInitialRom = loaded.RomBytes is not null && loaded.RomSha256Hex is not null;
            if (!interactive && hasInitialRom)
            {
                await ActivateLoadedRomAsync(loaded, null, ct).ConfigureAwait(false);
            }

            HostDrawerDescriptor descriptor = PlayerF1DrawerBuilder.Build(
                loaded.ModuleTitle, loaded.MediaLabel, backendId, _options.ResolutionScale,
                _mapperId, _submapperId, timing, _executionTier, _overscanCrop, _isFastForward,
                _controllerDpadMode, _isFourScoreEnabled, FormatTurboRate(_turboFramesInterval));

            _runner = new HostSessionRunner(1280, 720, descriptor);
            HostAotCompileState? initialAotState = interactive && hasInitialRom && _compilerPipeline is not null && _executionTier != CartExecutionTierMode.InterpreterOracle
                ? new HostAotCompileState(true, "Preparing Native Module...", 0.08, $"Starting AOT cache for '{loaded.ModuleTitle}'...")
                : null;
            var hostOpts = BuildHostSessionOptions(loaded, timing, descriptor, backendId, initialLoadError, initialAotState);

            if (interactive && hasInitialRom)
            {
                StartBackgroundRomActivation(loaded, ct);
            }

            Log("INFO", $"Starting HostSessionRunner (module={_moduleId}, backend={hostOpts.PreferredBackend}, aspect={_aspectRatioMode}, region={timing.Name}, tier={_executionTier}, hasRom={hasInitialRom}, mediaGate={hostOpts.ShowMediaGateInitially}).");
            HostSessionResult result = await _runner.RunAsync(hostOpts, this, msg => Log("HEARTBEAT", msg), ct).ConfigureAwait(false);
            LastStatusMessage = result.Summary;
            Log(result.ExitCode == 0 ? "INFO" : "WARN", $"HostSessionRunner exited {result.ExitCode}: {result.Summary}");
            return result;
        }
        finally
        {
            await CancelAndWaitBackgroundActivationAsync().ConfigureAwait(false);
            lock (_runtimeSync)
            {
                _runtime?.Dispose();
                _runtime = null;
            }
            _runner = null;
        }
    }

    public ValueTask OnFrameTickAsync(long frameNumber, HostInputFrame input, Span<byte> rgbaFramebuffer, int width, int height, CancellationToken ct)
    {
        uint[] ports = MapHostInputToNesPorts(input, frameNumber);
        bool fastForward = _isFastForward || input.IsFastForwardHeld;
        if (input.IsCutsceneSkipPressed || _cutsceneSkipPending)
        {
            _cutsceneSkipPending = false;
            Log("INFO", $"Cutscene skip pulse processed at frame {frameNumber}.");
        }

        lock (_runtimeSync)
        {
            if (_runtime is not null)
            {
                try
                {
                    for (int p = 0; p < 4; p++) _runtime.SetControllerButtons(p, ports[p]);
                    _runtime.StepFrame(fastForward);
                    ReadOnlySpan<byte> ppuRgba = _runtime.Ppu.FrameBufferRgba;
                    int required = checked(width * height * 4);
                    if (ppuRgba.Length >= required) ppuRgba[..required].CopyTo(rgbaFramebuffer);
                }
                catch (UnliftedPcTrapException ex) when (!_options.Headless && !_options.Frames.HasValue)
                {
                    Log("ERROR", $"Static AOT runtime halted on unlifted PC: {ex.Message}");
                    _runtime.Dispose();
                    _runtime = null;
                    _runner?.SetMediaGateState(true, _loaded?.RomSha256Hex, "Execution paused: unlifted code block.", ex.Message);
                    RenderIdleBackdropFrame(rgbaFramebuffer, width, height);
                }
            }
            else if (_demoCartActivated)
            {
                RenderDemoFrame(ports[0], fastForward, rgbaFramebuffer, width, height);
            }
            else
            {
                RenderIdleBackdropFrame(rgbaFramebuffer, width, height);
            }
        }

        return ValueTask.CompletedTask;
    }

    public int ReadAudioSamples(Span<short> interleavedStereoPcm)
    {
        lock (_runtimeSync)
        {
            if (_runtime is not null) return _runtime.Apu.ReadSamples(interleavedStereoPcm);
        }
        if (_demoCartActivated) return SynthesizeDemoAudio(interleavedStereoPcm);
        interleavedStereoPcm.Clear();
        return 0;
    }

    public void OnAspectRatioChanged(HostAspectRatioMode mode) { _aspectRatioMode = mode; Log("INFO", $"Aspect mode -> {mode}"); }
    public void OnOverscanCropChanged(HostOverscanCrop crop) { _overscanCrop = crop; Log("INFO", $"Overscan -> {PlayerF1DrawerBuilder.FormatOverscan(crop)}"); }
    public void OnFullscreenChanged(bool isFullscreen) { _isFullscreen = isFullscreen; Log("INFO", $"Fullscreen -> {isFullscreen}"); }
    public void OnFastForwardChanged(bool isEnabled) { _isFastForward = isEnabled; Log("INFO", $"FastForward -> {isEnabled}"); }
    public void OnCutsceneSkipRequested() { _cutsceneSkipPending = true; Log("INFO", "Cutscene skip requested."); }

    public void OnQuickSaveRequested()
    {
        _runner?.ActiveGraphicsContext?.FlushAndSyncForSnapshot();
        byte[] payload;
        lock (_runtimeSync)
        {
            payload = _runtime is not null ? _runtime.CaptureQuickSave() : CaptureDemoQuickSaveBytes();
        }
        Directory.CreateDirectory(_saveDir);
        string targetPath = ResolveQuickSavePath();
        string tmpPath = targetPath + ".tmp";
        File.WriteAllBytes(tmpPath, payload);
        if (File.Exists(targetPath)) File.Copy(targetPath, targetPath + ".bak", overwrite: true);
        File.Move(tmpPath, targetPath, overwrite: true);
        QuickSaveCount++;
        LastQuickSavePath = targetPath;
        LastStatusMessage = $"QuickSave saved ({payload.Length} B) -> {Path.GetFileName(targetPath)}";
        Log("INFO", LastStatusMessage);
    }

    public void OnQuickLoadRequested()
    {
        string targetPath = ResolveQuickSavePath();
        if (!File.Exists(targetPath)) { LastStatusMessage = "QuickLoad skipped: no save state found."; Log("WARN", LastStatusMessage); return; }
        byte[] payload = File.ReadAllBytes(targetPath);
        lock (_runtimeSync)
        {
            if (_runtime is not null)
            {
                bool ok = _runtime.TryRestoreQuickSave(payload, out string diag);
                if (ok) QuickLoadCount++;
                LastStatusMessage = ok ? $"QuickLoad restored ({diag})" : $"QuickLoad failed ({diag})";
                Log(ok ? "INFO" : "WARN", LastStatusMessage);
                return;
            }
        }
        if (TryRestoreDemoQuickSaveBytes(payload))
        {
            QuickLoadCount++;
            LastStatusMessage = "QuickLoad demo state restored.";
            Log("INFO", LastStatusMessage);
        }
    }

    public void OnExtraRowActivated(string rowId, bool isSecondaryAction)
    {
        if (string.Equals(rowId, "Region", StringComparison.OrdinalIgnoreCase))
        {
            _activeRegion = _activeRegion switch
            {
                CartVideoRegionStandard.Ntsc => CartVideoRegionStandard.Pal,
                CartVideoRegionStandard.Pal => CartVideoRegionStandard.Dendy,
                _ => CartVideoRegionStandard.Ntsc
            };
            Log("INFO", $"ExtraRow Region cycled -> {_activeRegion}");
        }
        else if (string.Equals(rowId, "OverscanCrop", StringComparison.OrdinalIgnoreCase))
        {
            _overscanCrop = _overscanCrop.Top == 0 ? new HostOverscanCrop(8, 8, 8, 8) : HostOverscanCrop.None;
            Log("INFO", $"ExtraRow Overscan -> {PlayerF1DrawerBuilder.FormatOverscan(_overscanCrop)}");
        }
        else if (string.Equals(rowId, "FastForward", StringComparison.OrdinalIgnoreCase))
        {
            OnFastForwardChanged(!_isFastForward);
        }
        else if (string.Equals(rowId, "CutsceneSkip", StringComparison.OrdinalIgnoreCase))
        {
            OnCutsceneSkipRequested();
        }
    }

    public void OnControlOptionChanged(string optionId, string selectedChoice)
    {
        if (string.Equals(optionId, "controllerDpadMode", StringComparison.OrdinalIgnoreCase))
            _controllerDpadMode = selectedChoice;
        else if (string.Equals(optionId, "fourScore", StringComparison.OrdinalIgnoreCase))
        {
            _isFourScoreEnabled = string.Equals(selectedChoice, "On", StringComparison.OrdinalIgnoreCase);
            lock (_runtimeSync)
            {
                if (_runtime?.Bus is CartMemoryBus cartBus)
                    cartBus.InputPorts.IsFourScoreEnabled = _isFourScoreEnabled;
            }
        }
        else if (string.Equals(optionId, "turboRate", StringComparison.OrdinalIgnoreCase))
            _turboFramesInterval = selectedChoice switch { "15 Hz" => 2, "30 Hz" => 1, _ => 0 };
        Log("INFO", $"ControlOption '{optionId}' -> '{selectedChoice}'");
    }

    private string ResolveQuickSavePath() =>
        AppEnvironment.AssertPathContained(Path.Combine(_saveDir, $"{_moduleId}.slot0.savstate"), _saveDir);

    private static string FormatTurboRate(int interval) => interval switch { 2 => "15 Hz", 1 => "30 Hz", _ => "Off" };

    private void Log(string level, string message) =>
        _logSink.AppendLine(ReEngineTelemetryFormatter.FormatLogLine(level, ReEngineTelemetryFormatter.StripHeartbeatPrefix(message)));
}
