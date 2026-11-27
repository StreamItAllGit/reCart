// @module: reCart.Core.Apu.Apu2A03Core
// @purpose: RP2A03 APU implementing ICartApuSink with 4/5-step frame counter, DMC callbacks, and Foundation AudioRingBuffer output.
// @depends-on: System, System.IO, System.Text, Foundation.Core, reCart.Contracts
// @exports: Apu2A03Core

using System.Text;
using Foundation.Core;
using reCart.Contracts;

namespace reCart.Core.Apu;

/// <summary>
/// Cycle-clocked <c>RP2A03</c> APU driving 5 audio channels, 4/5-step frame sequencer,
/// DMC memory reads/stalls, non-linear mixing, and stereo PCM output via <see cref="AudioRingBuffer"/>.
/// </summary>
public sealed class Apu2A03Core : ICartApuSink
{
    private static readonly byte[] StateMagic = "APU2A03\0"u8.ToArray();

    private readonly Func<ushort, byte>? _memoryRead;
    private readonly CartRegionTimingProfile _timing;
    private readonly bool _isPal;
    private readonly ushort[] _noisePeriodTable;
    private readonly ushort[] _dmcRateTable;
    private readonly long _fcStep1;
    private readonly long _fcStep2;
    private readonly long _fcStep3;
    private readonly long _fcStep4;
    private readonly long _fcStep5;
    private readonly AudioRingBuffer _ringBuffer;
    private readonly ApuPulseChannel _pulse1 = new(isPulse1: true);
    private readonly ApuPulseChannel _pulse2 = new(isPulse1: false);
    private readonly ApuTriangleChannel _triangle = new();
    private readonly ApuNoiseChannel _noise = new();
    private readonly ApuDmcChannel _dmc = new();
    private readonly Apu2A03Mixer _mixer = new();

    private readonly double _cyclesPerSample;
    private double _sampleAccumulator;
    private long _cpuCycle;
    private long _frameCounterCycle;
    private bool _fiveStepMode;
    private bool _irqInhibit;
    private bool _frameIrqPending;
    private long _droppedSampleCount;

    /// <summary>Initializes an <see cref="Apu2A03Core"/> bound to optional DMC memory reader, timing profile, and sample rate.</summary>
    public Apu2A03Core(
        Func<ushort, byte>? memoryRead = null,
        CartRegionTimingProfile? timing = null,
        int sampleRateHz = CartEngineConstants.DefaultAudioSampleRateHz,
        int ringBufferCapacitySamples = 65536)
    {
        if (sampleRateHz <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        _memoryRead = memoryRead;
        _timing = timing ?? CartRegionTimingProfile.Ntsc;
        _isPal = _timing.Region == CartVideoRegionStandard.Pal;
        _noisePeriodTable = _isPal ? Apu2A03Tables.NoisePeriodPal : Apu2A03Tables.NoisePeriodNtsc;
        _dmcRateTable = _isPal ? Apu2A03Tables.DmcRatePal : Apu2A03Tables.DmcRateNtsc;
        _fcStep1 = _isPal ? 8313 : 7457;
        _fcStep2 = _isPal ? 16627 : 14913;
        _fcStep3 = _isPal ? 24939 : 22371;
        _fcStep4 = _isPal ? 33253 : 29829;
        _fcStep5 = _isPal ? 41565 : 37281;
        SampleRateHz = sampleRateHz;
        _cyclesPerSample = _timing.CpuClockHz / sampleRateHz;
        _ringBuffer = new AudioRingBuffer(Math.Max(2048, ringBufferCapacitySamples), channels: 2);
    }

    /// <inheritdoc />
    public int SampleRateHz { get; }

    /// <inheritdoc />
    public bool IsIrqPending => _frameIrqPending || _dmc.IrqPending;

    /// <inheritdoc />
    public long DroppedSampleCount => _droppedSampleCount;

    /// <summary>Gets the underlying SPSC stereo audio ring buffer.</summary>
    internal AudioRingBuffer RingBuffer => _ringBuffer;

    /// <summary>Resets all APU channels, frame counter, IRQ flags, and clears buffered audio.</summary>
    public void Reset()
    {
        _pulse1.Reset();
        _pulse2.Reset();
        _triangle.Reset();
        _noise.Reset();
        _dmc.Reset();
        _mixer.Reset();
        _ringBuffer.Clear();
        _sampleAccumulator = 0;
        _cpuCycle = 0;
        _frameCounterCycle = 0;
        _fiveStepMode = false;
        _irqInhibit = false;
        _frameIrqPending = false;
        _droppedSampleCount = 0;
    }

    /// <inheritdoc />
    public byte ReadStatus()
    {
        byte status = 0;
        if (_pulse1.LengthCounter > 0) status |= 0x01;
        if (_pulse2.LengthCounter > 0) status |= 0x02;
        if (_triangle.LengthCounter > 0) status |= 0x04;
        if (_noise.LengthCounter > 0) status |= 0x08;
        if (_dmc.BytesRemaining > 0) status |= 0x10;
        if (_frameIrqPending) status |= 0x40;
        if (_dmc.IrqPending) status |= 0x80;
        _frameIrqPending = false;
        return status;
    }

    /// <inheritdoc />
    public void WriteRegister(ushort address, byte value)
    {
        if (address <= 0x4003) { _pulse1.WriteRegister(address & 3, value); return; }
        if (address <= 0x4007) { _pulse2.WriteRegister(address & 3, value); return; }
        if (address <= 0x400B) { _triangle.WriteRegister(address & 3, value); return; }
        if (address <= 0x400F) { _noise.WriteRegister(address & 3, value); return; }
        if (address <= 0x4013) { _dmc.WriteRegister(address & 3, value); return; }

        if (address == 0x4015)
        {
            WriteControlStatus(value);
        }
        else if (address == 0x4017)
        {
            WriteFrameCounter(value);
        }
    }

    /// <inheritdoc />
    public void ClockCycle()
    {
        _cpuCycle++;
        _triangle.ClockTimer();

        _noise.ClockTimer(_noisePeriodTable);
        _dmc.ClockCycle(_dmcRateTable, _memoryRead);

        if ((_cpuCycle & 1) == 0)
        {
            _pulse1.ClockTimer();
            _pulse2.ClockTimer();
        }

        AdvanceFrameCounter();

        _sampleAccumulator += 1.0;
        if (_sampleAccumulator >= _cyclesPerSample)
        {
            _sampleAccumulator -= _cyclesPerSample;
            EmitStereoSample();
        }
    }

    /// <inheritdoc />
    public int TakePendingDmcStallCycles()
    {
        int stall = _dmc.PendingStallCycles;
        _dmc.PendingStallCycles = 0;
        return stall;
    }

    /// <inheritdoc />
    public int ReadSamples(Span<short> interleavedStereoPcm)
    {
        int toRead = Math.Min(interleavedStereoPcm.Length, _ringBuffer.BufferedSamples) & ~1;
        return toRead == 0 ? 0 : _ringBuffer.Read(interleavedStereoPcm[..toRead]);
    }

    /// <inheritdoc />
    public byte[] CaptureState()
    {
        using MemoryStream ms = new();
        using BinaryWriter w = new(ms, Encoding.UTF8, leaveOpen: true);
        w.Write(StateMagic);
        w.Write(1);
        w.Write(_cpuCycle);
        w.Write(_frameCounterCycle);
        w.Write(_sampleAccumulator);
        w.Write(_fiveStepMode);
        w.Write(_irqInhibit);
        w.Write(_frameIrqPending);
        w.Write(_droppedSampleCount);
        _pulse1.WriteState(w);
        _pulse2.WriteState(w);
        _triangle.WriteState(w);
        _noise.WriteState(w);
        _dmc.WriteState(w);
        _mixer.WriteState(w);
        w.Flush();
        return ms.ToArray();
    }

    /// <inheritdoc />
    public void RestoreState(ReadOnlySpan<byte> state)
    {
        if (state.Length < StateMagic.Length + 8 || !state[..StateMagic.Length].SequenceEqual(StateMagic))
        {
            throw new InvalidDataException("Invalid Apu2A03Core snapshot header.");
        }

        using MemoryStream ms = new(state.ToArray());
        using BinaryReader r = new(ms, Encoding.UTF8, leaveOpen: true);
        r.BaseStream.Position = StateMagic.Length;
        int version = r.ReadInt32();
        if (version != 1) throw new InvalidDataException($"Unsupported Apu2A03Core snapshot version {version}.");

        _cpuCycle = r.ReadInt64();
        _frameCounterCycle = r.ReadInt64();
        _sampleAccumulator = r.ReadDouble();
        _fiveStepMode = r.ReadBoolean();
        _irqInhibit = r.ReadBoolean();
        _frameIrqPending = r.ReadBoolean();
        _droppedSampleCount = r.ReadInt64();
        _pulse1.ReadState(r);
        _pulse2.ReadState(r);
        _triangle.ReadState(r);
        _noise.ReadState(r);
        _dmc.ReadState(r);
        _mixer.ReadState(r);
        _ringBuffer.Clear();
    }

    private void WriteControlStatus(byte value)
    {
        _pulse1.Enabled = (value & 0x01) != 0; if (!_pulse1.Enabled) _pulse1.LengthCounter = 0;
        _pulse2.Enabled = (value & 0x02) != 0; if (!_pulse2.Enabled) _pulse2.LengthCounter = 0;
        _triangle.Enabled = (value & 0x04) != 0; if (!_triangle.Enabled) _triangle.LengthCounter = 0;
        _noise.Enabled = (value & 0x08) != 0; if (!_noise.Enabled) _noise.LengthCounter = 0;
        _dmc.SetEnabled((value & 0x10) != 0, _memoryRead);
    }

    private void WriteFrameCounter(byte value)
    {
        _fiveStepMode = (value & 0x80) != 0;
        _irqInhibit = (value & 0x40) != 0;
        if (_irqInhibit) _frameIrqPending = false;
        _frameCounterCycle = 0;
        if (_fiveStepMode)
        {
            ClockQuarterFrame();
            ClockHalfFrame();
        }
    }

    private void AdvanceFrameCounter()
    {
        _frameCounterCycle++;
        if (_frameCounterCycle == _fcStep1 || _frameCounterCycle == _fcStep3) ClockQuarterFrame();
        else if (_frameCounterCycle == _fcStep2) { ClockQuarterFrame(); ClockHalfFrame(); }
        else if (!_fiveStepMode && _frameCounterCycle == _fcStep4)
        {
            ClockQuarterFrame();
            ClockHalfFrame();
            if (!_irqInhibit) _frameIrqPending = true;
            _frameCounterCycle = 0;
        }
        else if (_fiveStepMode && _frameCounterCycle == _fcStep5)
        {
            ClockQuarterFrame();
            ClockHalfFrame();
            _frameCounterCycle = 0;
        }
    }

    private void ClockQuarterFrame()
    {
        _pulse1.Envelope.Clock();
        _pulse2.Envelope.Clock();
        _triangle.ClockQuarterFrame();
        _noise.Envelope.Clock();
    }

    private void ClockHalfFrame()
    {
        _pulse1.ClockHalfFrame();
        _pulse2.ClockHalfFrame();
        _triangle.ClockHalfFrame();
        _noise.ClockHalfFrame();
    }

    private void EmitStereoSample()
    {
        Span<float> stereo = stackalloc float[2];
        _mixer.MixStereoFloat(_pulse1.Output(), _pulse2.Output(), _triangle.Output(), _noise.Output(), _dmc.Output(), stereo);
        if (_ringBuffer.AvailableWriteSamples < 2)
        {
            _droppedSampleCount++;
            return;
        }

        _ringBuffer.Write(stereo);
    }
}
