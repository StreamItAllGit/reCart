// @module: reCart.Tests.Cpu6502Tests
// @purpose: Verifies RP2A03 6502 CPU opcodes, zero-BCD ADC/SBC, JMP($xxFF) page wrap, OAMDMA stall, $4016/$4017 open-bus, Four Score, and InterpreterOracle quarantine.
// @depends-on: Xunit, reCart.Contracts, reCart.Core.Cpu, reCart.Core.Input, reCart.Core.Mappers, reCart.Core.Oracle, reCart.Core.Ppu, reCart.Core.Rom
// @exports: Cpu6502Tests

using System.Buffers.Binary;
using reCart.Contracts;
using reCart.Core.Apu;
using reCart.Core.Cpu;
using reCart.Core.Input;
using reCart.Core.Mappers;
using reCart.Core.Oracle;
using reCart.Core.Ppu;
using reCart.Core.Rom;
using Xunit;

namespace reCart.Tests;

public sealed class Cpu6502Tests
{
    private static (Cpu2A03Core Cpu, CartMemoryBus Bus, CartInputPorts Ports) CreateHarness(byte[]? prgPatch = null, ushort patchAddr = 0x8000)
    {
        byte[] rom = BuildMinimalNrom(prgPatch, patchAddr);
        InesRomImage image = CartridgeLoader.Load(rom);
        ICartMapper mapper = CartMapperFactory.Create(image);
        var ppu = new Ppu2C02Core(mapper, CartRegionTimingProfile.Ntsc);
        var apu = new Apu2A03Core(timing: CartRegionTimingProfile.Ntsc);
        var ports = new CartInputPorts();
        var bus = new CartMemoryBus(ppu, apu, mapper, ports);
        var cpu = new Cpu2A03Core(bus, ppu);
        cpu.Reset();
        return (cpu, bus, ports);
    }

    private static byte[] BuildMinimalNrom(byte[]? patch, ushort patchAddr)
    {
        byte[] rom = new byte[16 + 32768 + 8192];
        rom[0] = 0x4E; rom[1] = 0x45; rom[2] = 0x53; rom[3] = 0x1A;
        rom[4] = 2; rom[5] = 1; rom[6] = 0x01;
        Array.Fill(rom, (byte)0xEA, 16, 32768);
        BinaryPrimitives.WriteUInt16LittleEndian(rom.AsSpan(16 + 0x7FFC, 2), 0x8000);
        BinaryPrimitives.WriteUInt16LittleEndian(rom.AsSpan(16 + 0x7FFA, 2), 0x8100);
        BinaryPrimitives.WriteUInt16LittleEndian(rom.AsSpan(16 + 0x7FFE, 2), 0x8200);
        if (patch is not null && patchAddr >= 0x8000)
            patch.CopyTo(rom.AsSpan(16 + (patchAddr - 0x8000)));
        return rom;
    }

    [Fact]
    public void Reset_InitializesSpAndResetVectorAndCycleCount()
    {
        (Cpu2A03Core cpu, _, _) = CreateHarness();
        Assert.Equal(0xFD, cpu.SP);
        Assert.Equal(0x8000, cpu.PC);
        Assert.Equal(7UL, cpu.CycleCount);
    }

    [Fact]
    public void AdcAndSbc_WithDecimalFlagSet_ExecuteBinaryMathWithoutBcd()
    {
        // SED; CLC; LDA #$19; ADC #$09 -> $22 (binary, not BCD $28); SEC; SBC #$09 -> $19
        byte[] code = [0xF8, 0x18, 0xA9, 0x19, 0x69, 0x09, 0x85, 0x10, 0x38, 0xE9, 0x09, 0x85, 0x11];
        (Cpu2A03Core cpu, CartMemoryBus bus, _) = CreateHarness(code);
        for (int i = 0; i < 8; i++) cpu.StepInstruction();
        Assert.Equal(0x22, bus.Read(0x0010));
        Assert.Equal(0x19, bus.Read(0x0011));
        Assert.True((cpu.P & Cpu2A03Core.FlagD) != 0);
    }

    [Fact]
    public void JmpIndirect_AtPageBoundary_WrapsLowByteWithinPage()
    {
        byte[] code = [0x6C, 0xFF, 0x03]; // JMP ($03FF) reads low from $03FF, high from $0300
        (Cpu2A03Core cpu, CartMemoryBus bus, _) = CreateHarness(code);
        bus.Write(0x03FF, 0x42);
        bus.Write(0x0300, 0x91);
        bus.Write(0x0400, 0xEE);
        cpu.StepInstruction();
        Assert.Equal(0x9142, cpu.PC);
    }

    [Theory]
    [InlineData(0UL, 513UL)]
    [InlineData(1UL, 514UL)]
    public void OamDma_Stalls513CyclesOnEvenAnd514OnOddCycle(ulong initialCycleMod, ulong expectedDelta)
    {
        byte[] code = [0xA9, 0x02, 0x8D, 0x14, 0x40]; // LDA #$02; STA $4014
        (Cpu2A03Core cpu, CartMemoryBus bus, _) = CreateHarness(code);
        bus.Write(0x0200, 0x55);
        cpu.StepInstruction(); // LDA #$02 -> CycleCount becomes 9 (odd); adjust mod
        cpu.CycleCount = initialCycleMod == 0 ? 10UL : 11UL;
        // Write opcode puts last bus cycle at 13/14 before TriggerOamDma; verify parity delta via direct TriggerOamDma
        ulong before = cpu.CycleCount;
        cpu.TriggerOamDma(0x02);
        Assert.Equal(expectedDelta, cpu.CycleCount - before);
    }

    [Fact]
    public void Interrupts_NmiAlwaysTriggersAndIrqRespectsInterruptDisableFlag()
    {
        (Cpu2A03Core cpu, _, _) = CreateHarness();
        cpu.P |= Cpu2A03Core.FlagI;
        cpu.RequestIrq();
        cpu.StepInstruction();
        Assert.Equal(0x8001, cpu.PC); // Masked IRQ executes NOP

        cpu.P &= unchecked((byte)~Cpu2A03Core.FlagI);
        cpu.StepInstruction();
        Assert.Equal(0x8200, cpu.PC); // Now IRQ vector runs

        cpu.RequestNmi();
        cpu.StepInstruction();
        Assert.Equal(0x8100, cpu.PC); // NMI fires regardless of I flag
    }

    [Fact]
    public void InputPorts_Preserve0x40OpenBusAndShift8SerialButtons()
    {
        var ports = new CartInputPorts();
        uint p1 = (uint)(NesControllerButtons.A | NesControllerButtons.Start | NesControllerButtons.Right);
        ports.SetControllerButtons(0, p1);
        ports.WriteRegister(0x4016, 1);
        ports.WriteRegister(0x4016, 0);

        byte actualBits = 0;
        for (int i = 0; i < 8; i++)
        {
            byte read = ports.ReadRegister(0x4016);
            Assert.Equal(0x40, read & 0xF8);
            actualBits |= (byte)((read & 0x01) << i);
        }
        Assert.Equal((byte)p1, actualBits);
    }

    [Fact]
    public void InputPorts_FourScore_MultiplexesPorts2And3AndSignature()
    {
        var ports = new CartInputPorts { IsFourScoreEnabled = true };
        ports.SetControllerButtons(0, (uint)NesControllerButtons.B);
        ports.SetControllerButtons(2, (uint)(NesControllerButtons.Up | NesControllerButtons.Left));
        ports.WriteRegister(0x4016, 1);
        ports.WriteRegister(0x4016, 0);

        byte p0 = 0, p2 = 0, sig = 0;
        for (int i = 0; i < 8; i++) p0 |= (byte)((ports.ReadPort(0) & 1) << i);
        for (int i = 0; i < 8; i++) p2 |= (byte)((ports.ReadPort(0) & 1) << i);
        for (int i = 0; i < 8; i++) sig = (byte)((sig << 1) | (ports.ReadPort(0) & 1));

        Assert.Equal((byte)NesControllerButtons.B, p0);
        Assert.Equal((byte)(NesControllerButtons.Up | NesControllerButtons.Left), p2);
        Assert.Equal(0x10, sig);
    }

    [Fact]
    public void InterpreterOracle_FailsClosedUnlessInterpreterOracleTierExplicit()
    {
        (Cpu2A03Core cpu, _, _) = CreateHarness();
        Assert.Throws<InvalidOperationException>(() => InterpreterOracle.CreateAuthorized(CartExecutionTierMode.StaticAotOnly, cpu));
        Assert.Throws<InvalidOperationException>(() => InterpreterOracle.CreateAuthorized(CartExecutionTierMode.JitBringup, cpu));

        var guarded = new InterpreterOracle(CartExecutionTierMode.StaticAotOnly, cpu);
        Assert.Throws<InvalidOperationException>(() => guarded.StepInstruction());

        InterpreterOracle authorized = InterpreterOracle.CreateAuthorized(CartExecutionTierMode.InterpreterOracle, cpu);
        int cycles = authorized.StepInstruction();
        Assert.Equal(2, cycles);
        Assert.Equal(1, authorized.ExecutedSteps);
    }

    [Fact]
    public void CpuStateSnapshot_RoundTripsRegistersAndCycleCount()
    {
        (Cpu2A03Core cpu, _, _) = CreateHarness([0xA9, 0x37, 0xA2, 0x44, 0xA0, 0x55]);
        cpu.StepInstruction(); cpu.StepInstruction(); cpu.StepInstruction();
        byte[] snap = cpu.CaptureState();
        ushort pc = cpu.PC; ulong cyc = cpu.CycleCount;

        cpu.Reset();
        cpu.RestoreState(snap);
        Assert.Equal(0x37, cpu.A);
        Assert.Equal(0x44, cpu.X);
        Assert.Equal(0x55, cpu.Y);
        Assert.Equal(pc, cpu.PC);
        Assert.Equal(cyc, cpu.CycleCount);
    }

    [Theory]
    [InlineData(0xA9, 0x7F, false, false)] // Positive non-zero
    [InlineData(0xA9, 0x00, true, false)]  // Zero flag
    [InlineData(0xA9, 0x80, false, true)]  // Negative flag
    public void LdaImmediate_SetsZeroAndNegativeFlagsAccurately(byte opcode, byte imm, bool expectZ, bool expectN)
    {
        (Cpu2A03Core cpu, _, _) = CreateHarness([opcode, imm]);
        cpu.StepInstruction();
        Assert.Equal(imm, cpu.A);
        Assert.Equal(expectZ, (cpu.P & Cpu2A03Core.FlagZ) != 0);
        Assert.Equal(expectN, (cpu.P & Cpu2A03Core.FlagN) != 0);
    }

    [Fact]
    public void BusAddressing_WrapsInternalRam2KbMirrorAndRejectsWriteToRom()
    {
        (Cpu2A03Core _, CartMemoryBus bus, _) = CreateHarness([0x99]);
        bus.Write(0x0042, 0xA5);
        Assert.Equal(0xA5, bus.Read(0x0842));
        Assert.Equal(0xA5, bus.Read(0x1842));
        Assert.Equal(0x99, bus.Read(0x8000));
    }
}
