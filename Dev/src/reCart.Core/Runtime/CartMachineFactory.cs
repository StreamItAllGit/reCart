// @module: reCart.Core.Runtime.CartMachineFactory
// @purpose: Constructs a fully wired CartMachineHost (CPU, Bus, Mapper, Dot PPU, APU, Input Ports, Optional Oracle) from iNES ROM bytes.
// @depends-on: System, reCart.Contracts, reCart.Core.Apu, reCart.Core.Cpu, reCart.Core.Input, reCart.Core.Mappers, reCart.Core.Oracle, reCart.Core.Ppu, reCart.Core.Rom
// @exports: CartMachineFactory

using reCart.Contracts;
using reCart.Core.Apu;
using reCart.Core.Cpu;
using reCart.Core.Input;
using reCart.Core.Mappers;
using reCart.Core.Oracle;
using reCart.Core.Ppu;
using reCart.Core.Rom;

namespace reCart.Core.Runtime;

/// <summary>
/// Wires a complete <see cref="CartMachineHost"/> instance from raw <c>.nes</c> cartridge container bytes.
/// </summary>
public static class CartMachineFactory
{
    /// <summary>
    /// Creates and resets a <see cref="CartMachineHost"/> bound to <paramref name="romBytes"/>,
    /// <paramref name="region"/> timing, and <paramref name="executionTier"/> enforcement.
    /// </summary>
    /// <param name="romBytes">Complete iNES 1.0 or NES 2.0 cartridge bytes.</param>
    /// <param name="region">Effective NTSC, PAL, or Dendy hardware timing region.</param>
    /// <param name="executionTier">AOT-primary or quarantined <c>InterpreterOracle</c> execution tier.</param>
    /// <param name="manifest">Optional clean-room module manifest metadata.</param>
    /// <param name="preloadRomDir">Optional cartridge ROM preload directory.</param>
    /// <param name="saveDir">Optional save directory for SRAM/QuickSaves.</param>
    /// <returns>Initialized and reset <see cref="CartMachineHost"/> ready to mount static modules or step frames.</returns>
    public static CartMachineHost CreateFromRomBytes(
        byte[] romBytes,
        CartVideoRegionStandard region = CartVideoRegionStandard.Ntsc,
        CartExecutionTierMode executionTier = CartExecutionTierMode.StaticAotOnly,
        ICartModuleManifest? manifest = null,
        string? preloadRomDir = null,
        string? saveDir = null)
    {
        ArgumentNullException.ThrowIfNull(romBytes);

        InesRomImage rom = CartridgeLoader.Load(romBytes);
        ICartMapper mapper = CartMapperFactory.Create(rom);
        mapper.Reset();

        CartVideoRegionStandard resolvedRegion = region == CartVideoRegionStandard.Auto
            ? (rom.Header.VideoRegionStandard == CartVideoRegionStandard.Auto
                ? CartVideoRegionStandard.Ntsc
                : rom.Header.VideoRegionStandard)
            : region;
        CartRegionTimingProfile timing = CartRegionTimingProfile.ForRegion(resolvedRegion);

        var ppu = new Ppu2C02Core(mapper, timing);
        var inputPorts = new CartInputPorts();
        CartMemoryBus? busRef = null;
        var apu = new Apu2A03Core(addr => busRef?.Read(addr) ?? 0x00, timing);
        busRef = new CartMemoryBus(ppu, apu, mapper, inputPorts);
        var cpu = new Cpu2A03Core(busRef, ppu);

        Func<int>? oracleStep = null;
        if (executionTier == CartExecutionTierMode.InterpreterOracle)
        {
            InterpreterOracle oracle = InterpreterOracle.CreateAuthorized(executionTier, cpu);
            oracleStep = () => oracle.StepInstruction();
        }

        cpu.Reset();
        ppu.Reset();
        apu.Reset();

        return new CartMachineHost(
            cpu,
            busRef,
            ppu,
            apu,
            mapper,
            inputPorts.SetControllerButtons,
            timing,
            executionTier,
            oracleStep,
            manifest,
            renderBackend: null,
            preloadRomDir,
            saveDir);
    }
}
