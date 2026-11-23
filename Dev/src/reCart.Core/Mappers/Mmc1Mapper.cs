// @module: reCart.Core.Mappers.Mmc1Mapper
// @purpose: Mapper 1 (MMC1 / SxROM) serial shift register, consecutive-write filter, 0x80 reset, banking, mirroring, and SRAM.
// @depends-on: System, System.Buffers.Binary, reCart.Contracts, reCart.Core.Rom
// @exports: Mmc1Mapper

using System.Buffers.Binary;
using reCart.Contracts;
using reCart.Core.Rom;

namespace reCart.Core.Mappers;

/// <summary>
/// Clean-room iNES Mapper <c>1</c> (<c>MMC1</c>) with consecutive-cycle RMW write filtering,
/// <c>0x80</c> shift reset, PRG/CHR bank switching, dynamic mirroring, and battery SRAM support.
/// </summary>
public sealed class Mmc1Mapper : CartMapperBase
{
    private const int RegisterSnapshotSize = 14;

    private readonly int _prgBankCount16K;
    private readonly int _chrBankCount4K;
    private Func<ulong>? _cycleProvider;

    private byte _shiftRegister;
    private byte _control;
    private byte _chrBank0;
    private byte _chrBank1;
    private byte _prgBank;
    private bool _hasPriorWrite;
    private ulong _lastWriteCycle;

    private int _prgSlot0;
    private int _prgSlot1;
    private int _chrSlot0;
    private int _chrSlot1;

    /// <summary>Initializes an <c>MMC1</c> mapper with optional CPU cycle provider for consecutive-write filtering.</summary>
    public Mmc1Mapper(InesRomImage rom, Func<ulong>? cpuCycleProvider = null)
        : base(rom, minPrgRamBytes: 8192)
    {
        _prgBankCount16K = Math.Max(1, PrgRom.Length / 16384);
        _chrBankCount4K = Math.Max(1, ChrMemory.Length / 4096);
        _cycleProvider = cpuCycleProvider;
        Reset();
    }

    /// <inheritdoc />
    public override string Name => "MMC1";

    internal void AttachCycleProvider(Func<ulong> cycleProvider)
    {
        _cycleProvider = cycleProvider ?? throw new ArgumentNullException(nameof(cycleProvider));
    }

    /// <inheritdoc />
    public override byte CpuRead(ushort address)
    {
        if (address >= 0xC000)
        {
            return PrgRom[(_prgSlot1 * 16384 + (address & 0x3FFF)) % PrgRom.Length];
        }

        if (address >= 0x8000)
        {
            return PrgRom[(_prgSlot0 * 16384 + (address & 0x3FFF)) % PrgRom.Length];
        }

        if (address >= 0x6000)
        {
            return IsSramEnabled() ? PrgRam[ResolveSramIndex(address)] : OpenBus(address);
        }

        return OpenBus(address);
    }

    /// <inheritdoc />
    public override void CpuWrite(ushort address, byte value)
    {
        if (address is >= 0x6000 and < 0x8000)
        {
            if (IsSramEnabled())
            {
                PrgRam[ResolveSramIndex(address)] = value;
            }
            return;
        }

        if (address < 0x8000)
        {
            return;
        }

        if (ShouldIgnoreConsecutiveWrite())
        {
            return;
        }

        if ((value & 0x80) != 0)
        {
            _shiftRegister = 0x10;
            _control |= 0x0C;
            ApplyBanksAndMirroring();
            return;
        }

        bool isFifthWrite = (_shiftRegister & 0x01) != 0;
        _shiftRegister = (byte)((_shiftRegister >> 1) | ((value & 0x01) << 4));

        if (isFifthWrite)
        {
            CommitRegister(address, (byte)(_shiftRegister & 0x1F));
            _shiftRegister = 0x10;
            ApplyBanksAndMirroring();
        }
    }

    /// <inheritdoc />
    public override byte PpuRead(ushort address)
    {
        int slot = (address & 0x1000) == 0 ? _chrSlot0 : _chrSlot1;
        return ChrMemory[(slot * 4096 + (address & 0x0FFF)) % ChrMemory.Length];
    }

    /// <inheritdoc />
    public override void PpuWrite(ushort address, byte value)
    {
        if (!HasChrRam)
        {
            return;
        }

        int slot = (address & 0x1000) == 0 ? _chrSlot0 : _chrSlot1;
        ChrMemory[(slot * 4096 + (address & 0x0FFF)) % ChrMemory.Length] = value;
    }

    /// <inheritdoc />
    public override void Reset()
    {
        _shiftRegister = 0x10;
        _control = (byte)(0x0C | ToControlMirroringBits(InitialMirroring));
        _chrBank0 = 0;
        _chrBank1 = 0;
        _prgBank = 0;
        _hasPriorWrite = false;
        _lastWriteCycle = 0;
        ApplyBanksAndMirroring();
    }

    /// <inheritdoc />
    public override byte[] CaptureState()
    {
        Span<byte> regs = stackalloc byte[RegisterSnapshotSize];
        regs[0] = _shiftRegister;
        regs[1] = _control;
        regs[2] = _chrBank0;
        regs[3] = _chrBank1;
        regs[4] = _prgBank;
        regs[5] = _hasPriorWrite ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt64LittleEndian(regs.Slice(6, 8), _lastWriteCycle);
        return EncodeState(regs);
    }

    /// <inheritdoc />
    public override void RestoreState(ReadOnlySpan<byte> state)
    {
        ReadOnlySpan<byte> regs = DecodeState(state, RegisterSnapshotSize);
        _shiftRegister = regs[0];
        _control = regs[1];
        _chrBank0 = regs[2];
        _chrBank1 = regs[3];
        _prgBank = regs[4];
        _hasPriorWrite = regs[5] != 0;
        _lastWriteCycle = BinaryPrimitives.ReadUInt64LittleEndian(regs.Slice(6, 8));
        ApplyBanksAndMirroring();
    }

    protected override void OnDirectPrgBankSwitch(uint bank)
    {
        _prgBank = (byte)((_prgBank & 0x10) | ((int)bank & 0x0F));
        ApplyBanksAndMirroring();
    }

    protected override void OnDirectChrBankSwitch(uint bank)
    {
        _chrBank0 = (byte)(bank & 0x1F);
        ApplyBanksAndMirroring();
    }

    private bool ShouldIgnoreConsecutiveWrite()
    {
        if (_cycleProvider is null)
        {
            return false;
        }

        ulong cycle = _cycleProvider();
        bool ignored = _hasPriorWrite && cycle >= _lastWriteCycle && (cycle - _lastWriteCycle) <= 1;
        _hasPriorWrite = true;
        _lastWriteCycle = cycle;
        return ignored;
    }

    private void CommitRegister(ushort address, byte data)
    {
        switch ((address >> 13) & 0x03)
        {
            case 0:
                _control = data;
                break;
            case 1:
                _chrBank0 = data;
                break;
            case 2:
                _chrBank1 = data;
                break;
            case 3:
                _prgBank = data;
                break;
        }
    }

    private void ApplyBanksAndMirroring()
    {
        Mirroring = (_control & 0x03) switch
        {
            0 => CartMirroringMode.SingleScreenLower,
            1 => CartMirroringMode.SingleScreenUpper,
            2 => CartMirroringMode.Vertical,
            _ => CartMirroringMode.Horizontal
        };

        int outer256KBase = PrgRom.Length > 262144 ? (_chrBank0 & 0x10) : 0;
        int localCount = PrgRom.Length > 262144 ? Math.Min(16, _prgBankCount16K) : _prgBankCount16K;
        int prgSelect = _prgBank & 0x0F;
        int prgMode = (_control >> 2) & 0x03;

        switch (prgMode)
        {
            case 0:
            case 1:
                int bank32 = (prgSelect & 0x0E) % localCount;
                _prgSlot0 = (outer256KBase + bank32) % _prgBankCount16K;
                _prgSlot1 = (outer256KBase + ((bank32 + 1) % localCount)) % _prgBankCount16K;
                ActivePrgBank = (uint)_prgSlot0;
                break;
            case 2:
                _prgSlot0 = outer256KBase % _prgBankCount16K;
                _prgSlot1 = (outer256KBase + (prgSelect % localCount)) % _prgBankCount16K;
                ActivePrgBank = (uint)_prgSlot1;
                break;
            default:
                _prgSlot0 = (outer256KBase + (prgSelect % localCount)) % _prgBankCount16K;
                _prgSlot1 = (outer256KBase + (localCount - 1)) % _prgBankCount16K;
                ActivePrgBank = (uint)_prgSlot0;
                break;
        }

        if ((_control & 0x10) == 0)
        {
            int chr8 = (_chrBank0 & 0x1E) % _chrBankCount4K;
            _chrSlot0 = chr8;
            _chrSlot1 = (chr8 + 1) % _chrBankCount4K;
            ActiveChrBank = (uint)(chr8 >> 1);
        }
        else
        {
            _chrSlot0 = _chrBank0 % _chrBankCount4K;
            _chrSlot1 = _chrBank1 % _chrBankCount4K;
            ActiveChrBank = (uint)_chrSlot0;
        }
    }

    private bool IsSramEnabled() => (_prgBank & 0x10) == 0 && PrgRam.Length > 0;

    private int ResolveSramIndex(ushort address)
    {
        int bank8K = PrgRam.Length >= 32768
            ? (_chrBank0 >> 2) & 0x03
            : (PrgRam.Length >= 16384 ? (_chrBank0 >> 3) & 0x01 : 0);
        return (bank8K * 8192 + (address - 0x6000)) % PrgRam.Length;
    }

    private static byte ToControlMirroringBits(CartMirroringMode mode) => mode switch
    {
        CartMirroringMode.SingleScreenLower => 0,
        CartMirroringMode.SingleScreenUpper => 1,
        CartMirroringMode.Vertical => 2,
        _ => 3
    };
}
