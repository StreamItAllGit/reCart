// @module: reCart.Core.Runtime.CartMachineHost
// @purpose: Coordinated reCart machine runtime uniting CPU, Bus, Dot PPU, APU, Mapper, Input, AOT dispatch, and QuickSave snapshots.
// @depends-on: System, System.Diagnostics, Foundation.Retro, reCart.Contracts, reCart.Core.Apu
// @exports: CartMachineHost

using System.Diagnostics;
using Foundation.Retro;
using reCart.Contracts;
using reCart.Core.Apu;

namespace reCart.Core.Runtime;

/// <summary>
/// Coordinates cycle-locked <c>CPU + PPU + APU + Mapper</c> execution, static AOT module dispatch,
/// input port latching, and SHA-256 validated <c>F5/F9</c> QuickSave snapshots.
/// </summary>
public sealed class CartMachineHost : ICartMachineRuntime, IReCartHost, ICartQuickSaveProvider
{
    private const int TelemetryWindow = 120;

    private readonly Action<int, uint>? _setControllerButtons;
    private readonly Func<int>? _oracleCpuStepInstruction;
    private readonly double[] _frameDurationsMs = new double[TelemetryWindow];

    private ICartRecompGameModule? _staticModule;
    private ICartModuleManifest _manifest;
    private string _preloadRomDirectory;
    private string _saveDirectory;
    private RecompHostOptions _hostOptions;
    private IRecompRenderBackend _renderBackend;

    private bool _resetDispatched;
    private bool _pendingAotNmi;
    private bool _pendingAotIrq;
    private int _ppuDotDebt;
    private int _cpuCycleCredit;
    private int _telemetryCursor;
    private long _lastTimestampTicks;
    private bool _disposed;

    /// <summary>Initializes a <see cref="CartMachineHost"/> with the canonical 8 subsystem parameters.</summary>
    public CartMachineHost(
        ICartCpuContext cpu,
        ICartMemoryBus bus,
        ICartPpuController ppu,
        ICartApuSink apu,
        ICartMapper mapper,
        Action<int, uint> setControllerButtons,
        CartRegionTimingProfile timingProfile,
        CartExecutionTierMode executionTier)
        : this(cpu, bus, ppu, apu, mapper, setControllerButtons, timingProfile, executionTier, null, null, null, null, null)
    {
    }

    /// <summary>Initializes a <see cref="CartMachineHost"/> with optional oracle step callback, manifest, backend, and paths.</summary>
    public CartMachineHost(
        ICartCpuContext cpu,
        ICartMemoryBus bus,
        ICartPpuController ppu,
        ICartApuSink apu,
        ICartMapper mapper,
        Action<int, uint> setControllerButtons,
        CartRegionTimingProfile timingProfile,
        CartExecutionTierMode executionTier,
        Func<int>? oracleCpuStepInstruction,
        ICartModuleManifest? manifest = null,
        IRecompRenderBackend? renderBackend = null,
        string? preloadRomDirectory = null,
        string? saveDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(cpu);
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(ppu);
        ArgumentNullException.ThrowIfNull(apu);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(setControllerButtons);
        ArgumentNullException.ThrowIfNull(timingProfile);

        Cpu = cpu;
        Bus = bus;
        Ppu = ppu;
        Apu = apu;
        Mapper = mapper;
        _setControllerButtons = setControllerButtons;
        TimingProfile = timingProfile;
        ExecutionTier = executionTier;
        _oracleCpuStepInstruction = oracleCpuStepInstruction;
        _manifest = manifest ?? new CartPackageManifest { ModuleId = "recart-runtime", Title = CartEngineConstants.DisplayName };
        _renderBackend = renderBackend ?? new NullCartRenderBackend();
        _preloadRomDirectory = preloadRomDirectory ?? string.Empty;
        _saveDirectory = saveDirectory ?? string.Empty;
        _hostOptions = new RecompHostOptions
        {
            TargetFps = timingProfile.FrameRateHz,
            AudioSampleRate = apu.SampleRateHz,
            ExecutionTier = executionTier == CartExecutionTierMode.JitBringup
                ? RecompExecutionTierMode.JitBringup
                : RecompExecutionTierMode.AotPrimary,
            RenderBackendId = _renderBackend.BackendId
        };
        _lastTimestampTicks = Stopwatch.GetTimestamp();
    }

    /// <inheritdoc />
    public ICartMemoryBus Bus { get; }

    /// <inheritdoc />
    public ICartCpuContext Cpu { get; }

    /// <inheritdoc />
    public ICartPpuController Ppu { get; }

    /// <inheritdoc />
    public ICartApuSink Apu { get; }

    /// <inheritdoc />
    public ICartMapper Mapper { get; }

    /// <inheritdoc />
    public ICartModuleManifest Manifest => _manifest;

    /// <inheritdoc />
    public CartRegionTimingProfile TimingProfile { get; }

    /// <inheritdoc />
    public CartExecutionDiagnostics Diagnostics { get; } = new();

    /// <inheritdoc />
    public CartExecutionTierMode ExecutionTier { get; }

    /// <inheritdoc />
    public string PreloadRomDirectory => _preloadRomDirectory;

    /// <inheritdoc />
    public string SaveDirectory => _saveDirectory;

    /// <inheritdoc />
    public RecompHostOptions Options => _hostOptions;

    /// <inheritdoc />
    public IRecompRenderBackend RenderBackend => _renderBackend;

    /// <inheritdoc />
    public void SetControllerButtons(int portIndex, uint nesButtonsMask)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _setControllerButtons?.Invoke(portIndex, nesButtonsMask);
    }

    /// <inheritdoc />
    public void MountStaticModule(ICartRecompGameModule module, ICartModuleManifest manifest, string preloadRomDir, string saveDir)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(manifest);

        _staticModule = module;
        _manifest = manifest;
        _preloadRomDirectory = preloadRomDir ?? string.Empty;
        _saveDirectory = saveDir ?? string.Empty;
        _resetDispatched = false;
        _pendingAotNmi = false;
        _pendingAotIrq = false;
        module.BindHost(this, Bus, Cpu, Ppu, Apu);
    }

    /// <inheritdoc />
    public void StepFrame(bool fastForward = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool isOracle = ExecutionTier == CartExecutionTierMode.InterpreterOracle;
        if (!isOracle && _staticModule != null && !_resetDispatched)
        {
            _resetDispatched = true;
            if (Cpu.PC == 0)
            {
                _staticModule.ExecuteResetVector();
                Diagnostics.StaticDispatchCount++;
            }
        }

        int dotsPerBatch = TimingProfile.Region == CartVideoRegionStandard.Pal ? 16 : 3;
        int cpuPerBatch = TimingProfile.Region == CartVideoRegionStandard.Pal ? 5 : 1;

        bool frameComplete;
        do
        {
            frameComplete = Ppu.StepDot();
            _ppuDotDebt += cpuPerBatch;
            while (_ppuDotDebt >= dotsPerBatch)
            {
                _ppuDotDebt -= dotsPerBatch;
                StepCpuAndApuCycle(isOracle);
            }
        }
        while (!frameComplete);

        if (isOracle && _oracleCpuStepInstruction == null)
        {
            Diagnostics.InterpreterDispatchCount++;
        }

        _renderBackend.BeginFrame(CartEngineConstants.FrameWidth, CartEngineConstants.FrameHeight);
        _renderBackend.PresentRgba(CartEngineConstants.FrameWidth, CartEngineConstants.FrameHeight, Ppu.FrameBufferRgba);
        Diagnostics.AudioDroppedSamples = Apu.DroppedSampleCount;
        Diagnostics.FramesRendered++;

        if (fastForward != _hostOptions.UnlockFps)
        {
            _hostOptions = _hostOptions with { UnlockFps = fastForward };
        }

        RecordFrameTiming();
    }

    /// <inheritdoc />
    public byte[] CaptureQuickSave()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return CartQuickSaveSerializer.Capture(
            Cpu, Bus, Ppu, Apu, Mapper, TimingProfile, Diagnostics,
            _resetDispatched, _ppuDotDebt, _cpuCycleCredit);
    }

    /// <inheritdoc />
    public bool TryRestoreQuickSave(ReadOnlySpan<byte> snapshotBytes, out string diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return CartQuickSaveSerializer.TryRestore(
            snapshotBytes, Cpu, Bus, Ppu, Apu, Mapper, Diagnostics,
            out _resetDispatched, out _ppuDotDebt, out _cpuCycleCredit, out diagnostic);
    }

    /// <inheritdoc />
    public RecompFrameStats GetFrameStats()
    {
        int count = (int)Math.Min(Diagnostics.FramesRendered, TelemetryWindow);
        double avgMs = 0.0;
        double p99Ms = 0.0;
        if (count > 0)
        {
            Span<double> sorted = stackalloc double[count];
            for (int i = 0; i < count; i++)
            {
                double val = _frameDurationsMs[i];
                avgMs += val;
                sorted[i] = val;
            }

            avgMs /= count;
            sorted.Sort();
            int idx = Math.Clamp((int)Math.Ceiling(0.99 * count) - 1, 0, count - 1);
            p99Ms = sorted[idx];
        }

        double currentFps = avgMs > 0.0001 ? 1000.0 / avgMs : 0.0;
        double fillRatio = Apu is Apu2A03Core apuCore ? apuCore.RingBuffer.FillRatio : 0.0;
        long underruns = Apu is Apu2A03Core apuCoreU ? apuCoreU.RingBuffer.UnderrunCount : Apu.DroppedSampleCount;
        return new RecompFrameStats(Diagnostics.FramesRendered, avgMs, currentFps, p99Ms, fillRatio, underruns);
    }

    /// <inheritdoc />
    public void SetTargetFps(double targetFps, bool unlock = false) =>
        _hostOptions = _hostOptions with { TargetFps = targetFps, UnlockFps = unlock };

    /// <inheritdoc />
    public void Dispose() => _disposed = true;

    private void StepCpuAndApuCycle(bool isOracle)
    {
        Apu.ClockCycle();
        int stall = Apu.TakePendingDmcStallCycles();
        if (stall > 0)
        {
            Cpu.ApplyDmcStallCycles(stall);
            if (isOracle) _cpuCycleCredit -= stall;
        }

        if (Ppu.TakeNmiPending())
        {
            Cpu.RequestNmi();
            if (!isOracle && _staticModule != null)
            {
                _pendingAotNmi = true;
            }
        }

        if ((Cpu.P & 0x04) == 0 && (Apu.IsIrqPending || Mapper.IsIrqPending))
        {
            Cpu.RequestIrq();
            if (!isOracle && _staticModule != null)
            {
                _pendingAotIrq = true;
            }
        }

        if (isOracle && _oracleCpuStepInstruction != null)
        {
            while (_cpuCycleCredit >= 0)
            {
                int cycles = Math.Max(1, _oracleCpuStepInstruction());
                _cpuCycleCredit -= cycles;
                Diagnostics.InterpreterDispatchCount++;
            }

            _cpuCycleCredit++;
        }
        else if (!isOracle && _staticModule != null && Cpu.PC != 0)
        {
            while (_cpuCycleCredit >= 0)
            {
                if (_pendingAotNmi)
                {
                    _pendingAotNmi = false;
                    DispatchAotInterrupt(0xFFFA);
                    _cpuCycleCredit -= 7;
                    Diagnostics.StaticDispatchCount++;
                    continue;
                }

                if (_pendingAotIrq && (Cpu.P & 0x04) == 0)
                {
                    _pendingAotIrq = false;
                    DispatchAotInterrupt(0xFFFE);
                    _cpuCycleCredit -= 7;
                    Diagnostics.StaticDispatchCount++;
                    continue;
                }

                ulong cyclesBefore = Cpu.CycleCount;
                if (!TryDispatchStaticBlock())
                {
                    _cpuCycleCredit = 0;
                    break;
                }

                int cycles = (int)Math.Clamp(Cpu.CycleCount - cyclesBefore, 1UL, 10000UL);
                _cpuCycleCredit -= cycles;
            }

            _cpuCycleCredit++;
        }
        else
        {
            Cpu.CycleCount++;
        }
    }

    private void DispatchAotInterrupt(ushort vectorAddress)
    {
        ushort pc = Cpu.PC;
        PushStackByte((byte)(pc >> 8));
        PushStackByte((byte)(pc & 0xFF));
        PushStackByte((byte)((Cpu.P & 0xEF) | 0x20));
        Cpu.P = (byte)(Cpu.P | 0x04);
        byte lo = Bus.Read(vectorAddress);
        byte hi = Bus.Read((ushort)(vectorAddress + 1));
        Cpu.PC = (ushort)(lo | (hi << 8));
        Cpu.CycleCount += 7;
    }

    private void PushStackByte(byte value)
    {
        Bus.Write((ushort)(0x0100 | Cpu.SP), value);
        Cpu.SP--;
    }

    private bool TryDispatchStaticBlock()
    {
        try
        {
            if (_staticModule!.TryDispatch(Bus.ActivePrgBank, Cpu.PC))
            {
                Diagnostics.StaticDispatchCount++;
                return true;
            }

            return false;
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("Unlifted", StringComparison.Ordinal))
        {
            Diagnostics.UnliftedTrapCount++;
            throw;
        }
    }

    private void RecordFrameTiming()
    {
        long now = Stopwatch.GetTimestamp();
        double elapsedMs = Math.Max(0.0, (now - _lastTimestampTicks) * 1000.0 / Stopwatch.Frequency);
        _lastTimestampTicks = now;
        _frameDurationsMs[_telemetryCursor] = elapsedMs;
        _telemetryCursor = (_telemetryCursor + 1) % TelemetryWindow;
    }

    private sealed class NullCartRenderBackend : ICartRenderBackend
    {
        public string BackendId => CartRenderBackendIds.Software;
        public string DisplayName => "Software (Null)";
        public bool IsHardwareAccelerated => false;
        public int ScaleFactor { get; set; } = 1;
        public void BeginFrame(int width, int height) { }
        public void PresentRgba(int width, int height, ReadOnlySpan<byte> rgba) { }
    }
}
