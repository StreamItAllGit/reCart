// @module: reCart.Player.ReCartSessionHost.VerifyAndPump
// @purpose: Async AOT SDL event pump helper and --verify package/ROM/backend diagnostic verifier for ReCartSessionHost.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Runtime.InteropServices, System.Threading, System.Threading.Tasks, Foundation.Core, reCart.Contracts, reEngine.Host.Contracts, reEngine.Host.Core
// @exports: ReCartSessionHost (partial)

using System.Runtime.InteropServices;
using Foundation.Core;
using reCart.Contracts;
using reEngine.Host.Contracts;
using reEngine.Host.Core;

namespace reCart.Player;

internal sealed partial class ReCartSessionHost
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint SdlWasInitDelegate(uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlPumpEventsDelegate();

    internal static async Task<T> AwaitWithResponsivePumpAsync<T>(
        Task<T> pendingTask,
        Action<string>? heartbeatSink = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pendingTask);
        var hb = new HeartbeatLogger("reCart AOT Compile", heartbeatSink);
        long tick = 0;
        while (!pendingTask.IsCompleted)
        {
            ct.ThrowIfCancellationRequested();
            PumpPendingSdlEventsSafe();
            if (tick % 60 == 0) hb.ReportProgress(tick / 60, 0, "Compiling local AOT module");
            await Task.WhenAny(pendingTask, Task.Delay(16, ct)).ConfigureAwait(false);
            tick++;
        }
        T result = await pendingTask.ConfigureAwait(false);
        hb.Complete(1, "AOT compilation completed");
        return result;
    }

    internal static void PumpPendingSdlEventsSafe()
    {
        try
        {
            string localSdl = Path.Combine(AppContext.BaseDirectory, "SDL3.dll");
            if (!NativeLibrary.TryLoad(File.Exists(localSdl) ? localSdl : "SDL3", out IntPtr sdlHandle)) return;
            try
            {
                if (NativeLibrary.TryGetExport(sdlHandle, "SDL_WasInit", out IntPtr wasInitPtr))
                {
                    uint videoInit = Marshal.GetDelegateForFunctionPointer<SdlWasInitDelegate>(wasInitPtr)(0x20u);
                    if (videoInit == 0) return;
                }
                if (NativeLibrary.TryGetExport(sdlHandle, "SDL_PumpEvents", out IntPtr pumpPtr))
                    Marshal.GetDelegateForFunctionPointer<SdlPumpEventsDelegate>(pumpPtr)();
            }
            finally { NativeLibrary.Free(sdlHandle); }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { }
    }

    internal static int VerifyTarget(PlayerCliOptions options, TextWriter output, TextWriter error)
    {
        IReadOnlyList<HostBackendCapability> backends = HostBackendProbe.ProbeAll();
        string audioDriver = NativeAudioDriverSelector.SelectBestDriver(options.AudioDriverOverride);
        if (options.RequireBackend && !backends.Any(b => b.Kind == options.PreferredBackend && b.IsAvailable))
        {
            error.WriteLine($"Verify failed: required backend '{options.BackendId}' is unavailable.");
            return 6;
        }
        if (!string.IsNullOrWhiteSpace(options.OpenPath) || !string.IsNullOrWhiteSpace(options.RomPath))
        {
            LoadedCartSessionInput loaded = LoadCartInputs(options);
            ushort mapperId = loaded.RomHeader?.MapperId ?? loaded.Manifest?.MapperId ?? 0;
            output.WriteLine($"Verify OK: module={loaded.ModuleId}, mapper={mapperId}, sha256={loaded.RomSha256Hex ?? "none"}, audio={audioDriver}, allowAutoStart={CartEngineConstants.AllowAutoStart}");
            return 0;
        }
        output.WriteLine($"Verify OK: {CartEngineConstants.EngineId} v{CartEngineConstants.Version}, protocol={CartEngineConstants.HostProtocolVersion}, backends={backends.Count(b => b.IsAvailable)}/{backends.Count}, audio={audioDriver}, allowAutoStart={CartEngineConstants.AllowAutoStart}");
        return 0;
    }

    private byte[] CaptureDemoQuickSaveBytes()
    {
        byte[] buf = new byte[16];
        "RCDEMO01"u8.CopyTo(buf);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(8, 4), _demoX);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(12, 4), _demoY);
        return buf;
    }

    private bool TryRestoreDemoQuickSaveBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || !data[..8].SequenceEqual("RCDEMO01"u8)) return false;
        _demoX = Math.Clamp(System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data.Slice(8, 4)), 8, CartEngineConstants.FrameWidth - 24);
        _demoY = Math.Clamp(System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data.Slice(12, 4)), 20, CartEngineConstants.FrameHeight - 24);
        return true;
    }

    private static CartVideoRegionStandard ResolveEffectiveRegion(PlayerCliOptions options, LoadedCartSessionInput loaded)
    {
        if (options.HasExplicitRegion && options.Region != CartVideoRegionStandard.Auto) return options.Region;
        if (loaded.RomHeader is { } hdr && hdr.VideoRegionStandard != CartVideoRegionStandard.Auto) return hdr.VideoRegionStandard;
        if (loaded.Manifest is { } mf && mf.VideoRegionStandard != CartVideoRegionStandard.Auto) return mf.VideoRegionStandard;
        return options.Region == CartVideoRegionStandard.Auto ? CartVideoRegionStandard.Ntsc : options.Region;
    }

    private static CartExecutionTierMode ResolveEffectiveExecutionTier(PlayerCliOptions options, LoadedCartSessionInput loaded) =>
        options.HasExplicitExecutionTier
            ? options.ExecutionTier
            : (loaded.Manifest is not null ? CartExecutionTierModeCodec.Parse(loaded.Manifest.ExecutionTierMode) : options.ExecutionTier);
}
