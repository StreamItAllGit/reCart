// @module: reCart.Core.Input.CartInputPorts
// @purpose: NES $4016/$4017 4-port controller strobe, serial shift registers, and 0x40 open-bus preservation.
// @depends-on: System, System.Buffers.Binary
// @exports: NesControllerButtons, CartInputPorts

using System.Buffers.Binary;

namespace reCart.Core.Input;

/// <summary>
/// Canonical NES controller button bitmask ordered by serial shift read position (<c>A..Right</c>).
/// </summary>
[Flags]
public enum NesControllerButtons : uint
{
    /// <summary>No buttons pressed.</summary>
    None = 0,
    /// <summary>A button (shift read 0).</summary>
    A = 1u << 0,
    /// <summary>B button (shift read 1).</summary>
    B = 1u << 1,
    /// <summary>Select button (shift read 2).</summary>
    Select = 1u << 2,
    /// <summary>Start button (shift read 3).</summary>
    Start = 1u << 3,
    /// <summary>D-Pad Up (shift read 4).</summary>
    Up = 1u << 4,
    /// <summary>D-Pad Down (shift read 5).</summary>
    Down = 1u << 5,
    /// <summary>D-Pad Left (shift read 6).</summary>
    Left = 1u << 6,
    /// <summary>D-Pad Right (shift read 7).</summary>
    Right = 1u << 7
}

/// <summary>
/// Implements NES <c>$4016</c> strobe and <c>$4016/$4017</c> shift-register reads across 4 controller ports.
/// </summary>
public sealed class CartInputPorts
{
    /// <summary>Total supported controller ports (Ports 0-1 standard, Ports 2-3 via Four Score reads 9-16).</summary>
    public const int PortCount = 4;

    private const byte OpenBusHighBits = 0x40;
    private const byte FourScoreSigPort1 = 0x10;
    private const byte FourScoreSigPort2 = 0x20;
    private const int SnapshotSizeBytes = 32;

    private readonly uint[] _liveButtons = new uint[PortCount];
    private readonly byte[] _latched = new byte[PortCount];
    private readonly int[] _shiftIndex = new int[2];
    private bool _strobe;
    private bool _fourScoreAutoDetected;
    private bool _isFourScoreEnabled;

    /// <summary>Gets whether the <c>$4016</c> bit-0 controller strobe line is currently asserted high.</summary>
    public bool Strobe => _strobe;

    /// <summary>Gets or sets whether 4-player Four Score multiplexing (<c>reads 9..24</c>) is explicitly active.</summary>
    public bool IsFourScoreEnabled
    {
        get => _isFourScoreEnabled || _fourScoreAutoDetected;
        set => _isFourScoreEnabled = value;
    }

    /// <summary>Updates the live button bitmask (<c>A..Right</c>) for controller port <paramref name="portIndex"/> (<c>0..3</c>).</summary>
    /// <param name="portIndex">Zero-based port index (<c>0..3</c>).</param>
    /// <param name="mask">Bitmask of pressed <see cref="NesControllerButtons"/>.</param>
    public void SetControllerButtons(int portIndex, uint mask)
    {
        if ((uint)portIndex >= PortCount)
        {
            throw new ArgumentOutOfRangeException(nameof(portIndex), portIndex, "Controller port index must be 0..3.");
        }

        uint clamped = mask & 0xFFu;
        Volatile.Write(ref _liveButtons[portIndex], clamped);

        if (portIndex >= 2 && clamped != 0)
        {
            _fourScoreAutoDetected = true;
        }
    }

    /// <summary>Returns the current live button bitmask for <paramref name="portIndex"/> (<c>0..3</c>).</summary>
    public uint GetControllerButtons(int portIndex)
    {
        if ((uint)portIndex >= PortCount)
        {
            throw new ArgumentOutOfRangeException(nameof(portIndex), portIndex, "Controller port index must be 0..3.");
        }

        return Volatile.Read(ref _liveButtons[portIndex]);
    }

    /// <summary>Handles writes to <c>$4016</c> strobe bit 0.</summary>
    public void WriteStrobe(byte value)
    {
        bool nextStrobe = (value & 0x01) != 0;
        if (nextStrobe)
        {
            _strobe = true;
            LatchShiftRegisters();
        }
        else if (_strobe)
        {
            _strobe = false;
            LatchShiftRegisters();
        }
    }

    /// <summary>Reads the next serial bit from controller port <c>0</c> (<c>$4016</c>) or <c>1</c> (<c>$4017</c>).</summary>
    public byte ReadPort(int portIndex)
    {
        if ((uint)portIndex >= 2)
        {
            throw new ArgumentOutOfRangeException(nameof(portIndex), portIndex, "Serial read port must be 0 ($4016) or 1 ($4017).");
        }

        if (_strobe)
        {
            uint liveA = Volatile.Read(ref _liveButtons[portIndex]) & 0x01u;
            return (byte)(OpenBusHighBits | liveA);
        }

        int bitPos = _shiftIndex[portIndex];
        _shiftIndex[portIndex] = Math.Min(bitPos + 1, 32);

        byte shiftBit = ReadShiftedBit(portIndex, bitPos);
        return (byte)(OpenBusHighBits | shiftBit);
    }

    /// <summary>Routes CPU read accesses for <c>$4016</c> and <c>$4017</c>.</summary>
    public byte ReadRegister(ushort address) => address switch
    {
        0x4016 => ReadPort(0),
        0x4017 => ReadPort(1),
        _ => OpenBusHighBits
    };

    /// <summary>Routes CPU write accesses for <c>$4016</c>.</summary>
    public void WriteRegister(ushort address, byte value)
    {
        if (address == 0x4016)
        {
            WriteStrobe(value);
        }
    }

    /// <summary>Resets strobe and shift-register state while preserving configured live button state.</summary>
    public void Reset()
    {
        _strobe = false;
        _fourScoreAutoDetected = false;
        Array.Clear(_latched);
        Array.Clear(_shiftIndex);
    }

    /// <summary>Captures deterministic controller port state for QuickSave snapshots.</summary>
    public byte[] CaptureState()
    {
        byte[] state = new byte[SnapshotSizeBytes];
        state[0] = (byte)'I';
        state[1] = (byte)'N';
        state[2] = (byte)'P';
        state[3] = 1;
        state[4] = _strobe ? (byte)1 : (byte)0;
        state[5] = _fourScoreAutoDetected ? (byte)1 : (byte)0;
        state[6] = _isFourScoreEnabled ? (byte)1 : (byte)0;
        for (int i = 0; i < PortCount; i++)
        {
            state[8 + i] = _latched[i];
        }

        BinaryPrimitives.WriteInt32LittleEndian(state.AsSpan(12, 4), _shiftIndex[0]);
        BinaryPrimitives.WriteInt32LittleEndian(state.AsSpan(16, 4), _shiftIndex[1]);
        return state;
    }

    /// <summary>Restores controller port state from a QuickSave snapshot.</summary>
    public void RestoreState(ReadOnlySpan<byte> state)
    {
        if (state.Length < SnapshotSizeBytes || state[0] != 'I' || state[1] != 'N' || state[2] != 'P' || state[3] != 1)
        {
            throw new InvalidDataException("Invalid CartInputPorts snapshot payload.");
        }

        _strobe = state[4] != 0;
        _fourScoreAutoDetected = state[5] != 0;
        _isFourScoreEnabled = state[6] != 0;
        for (int i = 0; i < PortCount; i++)
        {
            _latched[i] = state[8 + i];
        }

        _shiftIndex[0] = Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(state.Slice(12, 4)), 0, 32);
        _shiftIndex[1] = Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(state.Slice(16, 4)), 0, 32);
    }

    private void LatchShiftRegisters()
    {
        for (int i = 0; i < PortCount; i++)
        {
            _latched[i] = (byte)(Volatile.Read(ref _liveButtons[i]) & 0xFFu);
        }

        _shiftIndex[0] = 0;
        _shiftIndex[1] = 0;
    }

    private byte ReadShiftedBit(int portIndex, int bitPos)
    {
        if (bitPos < 8)
        {
            return (byte)((_latched[portIndex] >> bitPos) & 0x01);
        }

        if (!IsFourScoreEnabled)
        {
            return 1;
        }

        if (bitPos < 16)
        {
            return (byte)((_latched[portIndex + 2] >> (bitPos - 8)) & 0x01);
        }

        if (bitPos < 24)
        {
            byte sig = portIndex == 0 ? FourScoreSigPort1 : FourScoreSigPort2;
            return (byte)((sig >> (23 - bitPos)) & 0x01);
        }

        return 0;
    }
}
