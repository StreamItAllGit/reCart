// @module: reCart.Core.Cpu.Cpu2A03Core
// @purpose: RP2A03 6502 CPU core registers, interrupts, OAMDMA 513/514 stall, DMC stalls, and state snapshots.
// @depends-on: System, System.Buffers.Binary, reCart.Contracts
// @exports: Cpu2A03Core

using System.Buffers.Binary;
using reCart.Contracts;

namespace reCart.Core.Cpu;

/// <summary>
/// Clean-room <c>RP2A03</c> 6502 CPU core (zero BCD decimal mode, cycle-accurate bus ticks, <c>OAMDMA</c> <c>513/514</c> stall).
/// </summary>
public sealed partial class Cpu2A03Core : ICartCpuContext
{
    internal const byte FlagC = 0x01;
    internal const byte FlagZ = 0x02;
    internal const byte FlagI = 0x04;
    internal const byte FlagD = 0x08;
    internal const byte FlagB = 0x10;
    internal const byte FlagU = 0x20;
    internal const byte FlagV = 0x40;
    internal const byte FlagN = 0x80;

    private const int SnapshotSizeBytes = 24;
    private const ushort NmiVector = 0xFFFA;
    private const ushort ResetVector = 0xFFFC;
    private const ushort IrqVector = 0xFFFE;

    private readonly ICartMemoryBus _bus;
    private readonly ICartPpuController? _ppu;
    private bool _nmiPending;
    private bool _irqPending;

    /// <summary>Initializes an <c>RP2A03</c> CPU bound to <paramref name="bus"/> and optional <paramref name="ppu"/>.</summary>
    public Cpu2A03Core(ICartMemoryBus bus, ICartPpuController? ppu = null)
    {
        ArgumentNullException.ThrowIfNull(bus);
        _bus = bus;
        _ppu = ppu ?? (bus as CartMemoryBus)?.Ppu;
        SP = 0xFD;
        P = FlagI | FlagU;

        if (bus is CartMemoryBus cartBus)
        {
            cartBus.AttachCpu(this);
        }
    }

    /// <inheritdoc />
    public byte A { get; set; }

    /// <inheritdoc />
    public byte X { get; set; }

    /// <inheritdoc />
    public byte Y { get; set; }

    /// <inheritdoc />
    public byte SP { get; set; }

    /// <inheritdoc />
    public ushort PC { get; set; }

    /// <inheritdoc />
    public byte P { get; set; }

    /// <inheritdoc />
    public ulong CycleCount { get; set; }

    /// <inheritdoc />
    public void Reset()
    {
        A = 0;
        X = 0;
        Y = 0;
        SP = 0xFD;
        P = FlagI | FlagU;
        _nmiPending = false;
        _irqPending = false;
        PC = Read16Unclocked(ResetVector);
        CycleCount = 7;
    }

    /// <inheritdoc />
    public void RequestNmi() => _nmiPending = true;

    /// <inheritdoc />
    public void RequestIrq() => _irqPending = true;

    /// <summary>Clears any pending maskable IRQ edge/latch.</summary>
    public void ClearIrq() => _irqPending = false;

    /// <inheritdoc />
    public void TriggerOamDma(byte page)
    {
        int stallCycles = 513 + ((CycleCount & 1) != 0 ? 1 : 0);
        ushort baseAddr = (ushort)(page << 8);

        for (int i = 0; i < 256; i++)
        {
            byte value = _bus.Read((ushort)(baseAddr + i));
            _ppu?.WriteOamByte((byte)i, value);
        }

        CycleCount += (ulong)stallCycles;
    }

    /// <inheritdoc />
    public void ApplyDmcStallCycles(int stallCycles)
    {
        if (stallCycles < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stallCycles), stallCycles, "DMC stall cycles cannot be negative.");
        }

        if (stallCycles > 0)
        {
            CycleCount += (ulong)stallCycles;
        }
    }

    /// <summary>Executes one pending interrupt or one 6502 instruction and returns elapsed CPU cycles.</summary>
    public int StepInstruction()
    {
        ulong startCycle = CycleCount;

        if (_nmiPending)
        {
            _nmiPending = false;
            ServiceInterrupt(NmiVector);
            return (int)(CycleCount - startCycle);
        }

        if (_irqPending && (P & FlagI) == 0)
        {
            _irqPending = false;
            ServiceInterrupt(IrqVector);
            return (int)(CycleCount - startCycle);
        }

        byte opcode = FetchByte();
        ExecuteOpcode(opcode);
        return (int)(CycleCount - startCycle);
    }

    /// <inheritdoc />
    public byte[] CaptureState()
    {
        byte[] state = new byte[SnapshotSizeBytes];
        state[0] = (byte)'C';
        state[1] = (byte)'P';
        state[2] = (byte)'U';
        state[3] = 1;
        state[4] = A;
        state[5] = X;
        state[6] = Y;
        state[7] = SP;
        state[8] = P;
        state[9] = _nmiPending ? (byte)1 : (byte)0;
        state[10] = _irqPending ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt16LittleEndian(state.AsSpan(12, 2), PC);
        BinaryPrimitives.WriteUInt64LittleEndian(state.AsSpan(16, 8), CycleCount);
        return state;
    }

    /// <inheritdoc />
    public void RestoreState(ReadOnlySpan<byte> state)
    {
        if (state.Length < SnapshotSizeBytes || state[0] != 'C' || state[1] != 'P' || state[2] != 'U' || state[3] != 1)
        {
            throw new InvalidDataException("Invalid Cpu2A03Core snapshot payload.");
        }

        A = state[4];
        X = state[5];
        Y = state[6];
        SP = state[7];
        P = state[8];
        _nmiPending = state[9] != 0;
        _irqPending = state[10] != 0;
        PC = BinaryPrimitives.ReadUInt16LittleEndian(state.Slice(12, 2));
        CycleCount = BinaryPrimitives.ReadUInt64LittleEndian(state.Slice(16, 8));
    }

    private byte BusRead(ushort address)
    {
        CycleCount++;
        return _bus.Read(address);
    }

    private void BusWrite(ushort address, byte value)
    {
        CycleCount++;
        _bus.Write(address, value);
    }

    private void TickInternalCycle() => CycleCount++;

    private ushort Read16Unclocked(ushort address)
    {
        byte lo = _bus.Read(address);
        byte hi = _bus.Read((ushort)(address + 1));
        return (ushort)(lo | (hi << 8));
    }

    private void Push(byte value)
    {
        BusWrite((ushort)(0x0100 | SP), value);
        SP--;
    }

    private byte Pop()
    {
        SP++;
        return BusRead((ushort)(0x0100 | SP));
    }

    private void Push16(ushort value)
    {
        Push((byte)(value >> 8));
        Push((byte)(value & 0xFF));
    }

    private ushort Pop16()
    {
        byte lo = Pop();
        byte hi = Pop();
        return (ushort)(lo | (hi << 8));
    }

    private void ServiceInterrupt(ushort vectorAddress)
    {
        BusRead(PC);
        TickInternalCycle();
        Push16(PC);
        Push((byte)((P | FlagU) & ~FlagB));
        SetFlag(FlagI, true);
        byte lo = BusRead(vectorAddress);
        byte hi = BusRead((ushort)(vectorAddress + 1));
        PC = (ushort)(lo | (hi << 8));
    }

    private bool GetFlag(byte mask) => (P & mask) != 0;

    private void SetFlag(byte mask, bool enabled)
    {
        if (enabled)
        {
            P |= mask;
        }
        else
        {
            P &= (byte)~mask;
        }
    }

    private byte SetZeroNegative(byte value)
    {
        SetFlag(FlagZ, value == 0);
        SetFlag(FlagN, (value & 0x80) != 0);
        return value;
    }
}
