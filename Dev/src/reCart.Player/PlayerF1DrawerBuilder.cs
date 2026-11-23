// @module: reCart.Player.PlayerF1DrawerBuilder
// @purpose: Builds the reEngine.Host v0.6.1 HostDrawerDescriptor with NES mapper/region/tier/overscan/fast-forward/cutscene ExtraRows and ControlOptions.
// @depends-on: System, System.Collections.Generic, reCart.Contracts, reEngine.Host.Contracts
// @exports: PlayerF1DrawerBuilder

using reCart.Contracts;
using reEngine.Host.Contracts;

namespace reCart.Player;

internal static class PlayerF1DrawerBuilder
{
    public static HostDrawerDescriptor Build(
        string moduleTitle,
        string mediaLabel,
        string backendId,
        int resolutionScale,
        ushort mapperId,
        byte submapperId,
        CartRegionTimingProfile timingProfile,
        CartExecutionTierMode executionTier,
        HostOverscanCrop overscanCrop,
        bool isFastForwardEnabled,
        string controllerDpadMode = "D-Pad + Stick",
        bool isFourScoreEnabled = false,
        string turboRate = "Off")
    {
        ArgumentNullException.ThrowIfNull(timingProfile);

        string safeTitle = string.IsNullOrWhiteSpace(moduleTitle) ? $"{CartEngineConstants.DisplayName} Cartridge" : moduleTitle.Trim();
        string safeMedia = string.IsNullOrWhiteSpace(mediaLabel) ? "BYO .nes / .fds (data\\rom\\)" : mediaLabel.Trim();
        string tierLabel = CartExecutionTierModeCodec.ToDisplayLabel(executionTier);
        string mapperText = FormatMapperLabel(mapperId, submapperId);
        string overscanText = FormatOverscan(overscanCrop);

        IReadOnlyList<HostDrawerRowDescriptor> extraRows =
        [
            new("Mapper", HostDrawerTabId.System, "Mapper", mapperText, "Active iNES / NES 2.0 cartridge board", IsInteractive: false),
            new("Region", HostDrawerTabId.System, "Region", $"{timingProfile.Name} ({timingProfile.FrameRateHz:F2} Hz)", "Active RP2C02/RP2C07 region timing profile", IsInteractive: true),
            new("ExecutionTier", HostDrawerTabId.System, "Execution Tier", tierLabel, "Standard 24 AOT-primary execution tier", IsInteractive: false),
            new("OverscanCrop", HostDrawerTabId.Video, "Overscan Crop", overscanText, "Cycle NES overscan edge crop", IsInteractive: true),
            new("FastForward", HostDrawerTabId.Video, "Fast-Forward", isFastForwardEnabled ? "On (2x)" : "Off", "Toggle F6 / Grave fast-forward cadence", IsInteractive: true),
            new("CutsceneSkip", HostDrawerTabId.System, "Cutscene Skip", "Ready (F7)", "Request F7 cutscene fast-skip burst", IsInteractive: true)
        ];

        IReadOnlyList<HostControlOptionDescriptor> controlOptions =
        [
            new("controllerDpadMode", "D-Pad / Stick", ["D-Pad + Stick", "D-Pad Only", "Stick Only"], controllerDpadMode, "Free-Roam movement source (Menus always keep both active)"),
            new("fourScore", "Four Score (4P)", ["Off", "On"], isFourScoreEnabled ? "On" : "Off", "Enable 4-player multi-tap port routing"),
            new("turboRate", "Turbo Pulse", ["Off", "15 Hz", "30 Hz"], turboRate, "Optional rapid-fire pulse on North / RB")
        ];

        return new HostDrawerDescriptor(
            ModuleTitle: safeTitle,
            EngineDisplayName: $"{CartEngineConstants.DisplayName} v{CartEngineConstants.Version}",
            ExecutionTierLabel: tierLabel,
            BackendLabel: string.IsNullOrWhiteSpace(backendId) ? CartRenderBackendIds.OpenGl : backendId,
            ResolutionScale: Math.Clamp(resolutionScale, 1, 8),
            ActiveSchema: "B",
            MediaLabel: safeMedia,
            ExtraRows: extraRows,
            ControlOptions: controlOptions);
    }

    public static string FormatMapperLabel(ushort mapperId, byte submapperId)
    {
        string board = mapperId switch
        {
            0 => "NROM",
            1 => "MMC1",
            2 => "UxROM",
            3 => "CNROM",
            4 => "MMC3",
            7 => "AxROM",
            9 => "MMC2",
            10 => "MMC4",
            _ => "Board"
        };
        return submapperId > 0 ? $"{board} (#{mapperId}.{submapperId})" : $"{board} (#{mapperId})";
    }

    public static string FormatOverscan(HostOverscanCrop crop) =>
        crop.Top == 0 && crop.Bottom == 0 && crop.Left == 0 && crop.Right == 0
            ? "0 px"
            : $"{crop.Top},{crop.Bottom},{crop.Left},{crop.Right} px";
}
