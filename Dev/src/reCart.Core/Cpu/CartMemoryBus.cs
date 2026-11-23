// @module: reCart.Core.Cpu.CartMemoryBus
// @purpose: Clean-room 2A03 16-bit CPU memory bus routing RAM, PPU, APU, OAMDMA, $4016/$4017 input, and mappers.
// @depends-on: System, System.Buffers.Binary, reCart.Contracts, reCart.Core.Input, reCart.Core.Mappers
// @exports: CartMemoryBus

using System.Buffers.Binary;
using reCart.Contracts;
using reCart.Core.Input;
using reCart.Core.Mappers;

namespace reCart.Core.Cpu;

/// <summary>
/// Routes <c>2A03</c> CPU memory reads and writes across internal WRAM, <c>2C02</c> PPU, <c>2A03</c> APU,
/// <c>$4014</c> OAM DMA, <c>$4016/$4017</c> controller ports, and cartridge mapper space (<c>$4020-$FFFF</c>).
/// </summary>
public sealed class CartMemoryBus : ICartMemoryBus
{
    private const int InternalRamSize = 2048;

    private readonly byte[] _ram = new byte[InternalRamSize];
    private readonly ICartPpuController _ppu;
    private readonly ICartApuSink _apu;
    private readonly ICartMapper _mapper;
    private readonly CartInputPorts _inputPorts;

    private ICartCpuContext? _cpu;
    private byte _openBus;

    /// <summary>Initializes a clean-room <c>2A03</c> memory bus connecting PPU, APU, mapper, and input ports.</summary>
    public CartMemoryBus(
        ICartPpuController ppu,
        ICartApuSink apu,
        ICartMapper mapper,
        CartInputPorts? inputPorts = null)
    {
        ArgumentNullException.ThrowIfNull(ppu);
        ArgumentNullException.ThrowIfNull(apu);
        ArgumentNullException.ThrowIfNull(mapper);

        _ppu = ppu;
        _apu = apu;
        _mapper = mapper;
        _inputPorts = inputPorts ?? new CartInputPorts();
    }

    /// <summary>Controller input ports bound to <c>$4016/$4017</c>.</summary>
    public CartInputPorts InputPorts => _inputPorts;

    /// <inheritdoc />
    public uint ActivePrgBank => _mapper.ActivePrgBank;

    /// <inheritdoc />
    public uint ActiveChrBank => _mapper.ActiveChrBank;

    internal ICartPpuController Ppu => _ppu;

    internal ICartApuSink Apu => _apu;

    internal ICartMapper Mapper => _mapper;

    internal ReadOnlySpan<byte> InternalRam => _ram;

    internal void AttachCpu(ICartCpuContext cpu)
    {
        ArgumentNullException.ThrowIfNull(cpu);
        _cpu = cpu;

        if (_mapper is Mmc1Mapper mmc1)
        {
            mmc1.AttachCycleProvider(() => cpu.CycleCount);
        }
    }

    /// <inheritdoc />
    public byte Read(ushort address)
    {
        if (address < 0x2000)
        {
            return _openBus = _ram[address & 0x07FF];
        }

        if (address < 0x4000)
        {
            return _openBus = _ppu.ReadRegister((ushort)(0x2000 | (address & 0x0007)));
        }

        if (address == 0x4015)
        {
            return _apu.ReadStatus();
        }

        if (address is 0x4016 or 0x4017)
        {
            return _openBus = _inputPorts.ReadRegister(address);
        }

        if (address < 0x4020)
        {
            return _openBus;
        }

        return _openBus = _mapper.CpuRead(address);
    }

    /// <inheritdoc />
    public void Write(ushort address, byte value)
    {
        _openBus = value;

        if (address < 0x2000)
        {
            _ram[address & 0x07FF] = value;
            return;
        }

        if (address < 0x4000)
        {
            _ppu.WriteRegister((ushort)(0x2000 | (address & 0x0007)), value);
            return;
        }

        if (address == 0x4014)
        {
            if (_cpu is not null)
            {
                _cpu.TriggerOamDma(value);
            }
            else
            {
                CopyOamPageDirect(value);
            }
            return;
        }

        if (address == 0x4016)
        {
            _inputPorts.WriteRegister(address, value);
            return;
        }

        if (address <= 0x4013 || address is 0x4015 or 0x4017)
        {
            _apu.WriteRegister(address, value);
            return;
        }

        if (address >= 0x4020)
        {
            _mapper.CpuWrite(address, value);
        }
    }

    /// <inheritdoc />
    public void SwitchPrgBank(uint bank)
    {
        if (_mapper is ICartDirectBankControl control)
        {
            control.SetPrgBank(bank);
        }
    }

    /// <inheritdoc />
    public void SwitchChrBank(uint bank)
    {
        if (_mapper is ICartDirectBankControl control)
        {
            control.SetChrBank(bank);
        }
    }

    /// <summary>Clears internal 2 KB WRAM and resets controller strobe state.</summary>
    public void Reset(bool clearRam = true)
    {
        if (clearRam)
        {
            Array.Clear(_ram);
        }

        _openBus = 0;
        _inputPorts.Reset();
    }

    /// <summary>Captures deterministic 2 KB WRAM and input port snapshot bytes.</summary>
    public byte[] CaptureState()
    {
        byte[] inputState = _inputPorts.CaptureState();
        byte[] state = new byte[12 + InternalRamSize + inputState.Length];
        state[0] = (byte)'B';
        state[1] = (byte)'U';
        state[2] = (byte)'S';
        state[3] = 1;
        state[4] = _openBus;
        BinaryPrimitives.WriteInt32LittleEndian(state.AsSpan(8, 4), inputState.Length);
        _ram.CopyTo(state, 12);
        inputState.CopyTo(state, 12 + InternalRamSize);
        return state;
    }

    /// <summary>Restores deterministic 2 KB WRAM and input port state from a snapshot.</summary>
    public void RestoreState(ReadOnlySpan<byte> state)
    {
        if (state.Length < 12 + InternalRamSize || state[0] != 'B' || state[1] != 'U' || state[2] != 'S' || state[3] != 1)
        {
            throw new InvalidDataException("Invalid CartMemoryBus snapshot payload.");
        }

        _openBus = state[4];
        int inputLen = BinaryPrimitives.ReadInt32LittleEndian(state.Slice(8, 4));
        if (inputLen < 0 || state.Length < 12 + InternalRamSize + inputLen)
        {
            throw new InvalidDataException("Truncated CartMemoryBus snapshot payload.");
        }

        state.Slice(12, InternalRamSize).CopyTo(_ram);
        if (inputLen > 0)
        {
            _inputPorts.RestoreState(state.Slice(12 + InternalRamSize, inputLen));
        }
    }

    private void CopyOamPageDirect(byte page)
    {
        ushort baseAddr = (ushort)(page << 8);
        for (int i = 0; i < 256; i++)
        {
            _ppu.WriteOamByte((byte)i, Read((ushort)(baseAddr + i)));
        }
    }
}
