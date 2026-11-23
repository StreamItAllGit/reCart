// @module: reCart.Core.Apu.Apu2A03Channels
// @purpose: RP2A03 Pulse, Triangle, Noise, and DMC channel units with shared envelope and hardware tables.
// @depends-on: System, System.IO
// @exports: ApuEnvelope, ApuPulseChannel, ApuTriangleChannel, ApuNoiseChannel, ApuDmcChannel, Apu2A03Tables

namespace reCart.Core.Apu;

internal static class Apu2A03Tables
{
    public static readonly byte[] LengthTable =
    [
        10, 254, 20, 2, 40, 4, 80, 6, 160, 8, 60, 10, 14, 12, 26, 14,
        12, 16, 24, 18, 48, 20, 96, 22, 192, 24, 72, 26, 16, 28, 32, 30
    ];

    public static readonly byte[][] PulseDuty =
    [
        [0, 1, 0, 0, 0, 0, 0, 0],
        [0, 1, 1, 0, 0, 0, 0, 0],
        [0, 1, 1, 1, 1, 0, 0, 0],
        [1, 0, 0, 1, 1, 1, 1, 1]
    ];

    public static readonly byte[] TriangleWave =
    [
        15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0,
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15
    ];

    public static readonly ushort[] NoisePeriodNtsc = [4, 8, 16, 32, 64, 96, 128, 160, 202, 254, 380, 508, 762, 1016, 2034, 4068];
    public static readonly ushort[] NoisePeriodPal = [4, 8, 14, 30, 60, 88, 118, 148, 188, 236, 354, 472, 708, 944, 1890, 3778];
    public static readonly ushort[] DmcRateNtsc = [428, 380, 340, 320, 286, 254, 226, 214, 190, 160, 142, 128, 106, 84, 72, 54];
    public static readonly ushort[] DmcRatePal = [398, 354, 316, 298, 276, 236, 210, 198, 176, 148, 132, 118, 98, 78, 66, 50];
}

internal struct ApuEnvelope
{
    public bool LoopOrHalt;
    public bool ConstantVolume;
    public byte VolumeOrPeriod;
    public bool StartFlag;
    public byte Divider;
    public byte DecayLevel;

    public void WriteControl(byte val)
    {
        LoopOrHalt = (val & 0x20) != 0;
        ConstantVolume = (val & 0x10) != 0;
        VolumeOrPeriod = (byte)(val & 0x0F);
    }

    public void Clock()
    {
        if (StartFlag)
        {
            StartFlag = false;
            DecayLevel = 15;
            Divider = VolumeOrPeriod;
            return;
        }
        if (Divider > 0) { Divider--; return; }
        Divider = VolumeOrPeriod;
        if (DecayLevel > 0) DecayLevel--;
        else if (LoopOrHalt) DecayLevel = 15;
    }

    public readonly int Volume => ConstantVolume ? VolumeOrPeriod : DecayLevel;
    public readonly void WriteState(BinaryWriter w) { w.Write(LoopOrHalt); w.Write(ConstantVolume); w.Write(VolumeOrPeriod); w.Write(StartFlag); w.Write(Divider); w.Write(DecayLevel); }
    public void ReadState(BinaryReader r) { LoopOrHalt = r.ReadBoolean(); ConstantVolume = r.ReadBoolean(); VolumeOrPeriod = r.ReadByte(); StartFlag = r.ReadBoolean(); Divider = r.ReadByte(); DecayLevel = r.ReadByte(); }
}

internal sealed class ApuPulseChannel(bool isPulse1)
{
    private readonly bool _isPulse1 = isPulse1;
    public bool Enabled;
    public byte LengthCounter;
    public ApuEnvelope Envelope;
    public byte DutyMode;
    public byte DutyStep;
    public ushort TimerPeriod;
    public ushort TimerCounter;
    public bool SweepEnabled;
    public byte SweepPeriod;
    public bool SweepNegate;
    public byte SweepShift;
    public bool SweepReload;
    public byte SweepDivider;

    public void Reset() { Enabled = false; LengthCounter = 0; Envelope = default; DutyMode = 0; DutyStep = 0; TimerPeriod = 0; TimerCounter = 0; SweepEnabled = false; SweepPeriod = 0; SweepNegate = false; SweepShift = 0; SweepReload = false; SweepDivider = 0; }
    public void WriteRegister(int offset, byte val)
    {
        switch (offset)
        {
            case 0: DutyMode = (byte)(val >> 6); Envelope.WriteControl(val); break;
            case 1: SweepEnabled = (val & 0x80) != 0; SweepPeriod = (byte)((val >> 4) & 0x07); SweepNegate = (val & 0x08) != 0; SweepShift = (byte)(val & 0x07); SweepReload = true; break;
            case 2: TimerPeriod = (ushort)((TimerPeriod & 0x0700) | val); break;
            case 3:
                TimerPeriod = (ushort)((TimerPeriod & 0x00FF) | ((val & 0x07) << 8));
                if (Enabled) LengthCounter = Apu2A03Tables.LengthTable[val >> 3];
                DutyStep = 0;
                Envelope.StartFlag = true;
                break;
        }
    }

    public void ClockTimer()
    {
        if (TimerCounter == 0) { TimerCounter = TimerPeriod; DutyStep = (byte)((DutyStep + 1) & 7); }
        else TimerCounter--;
    }

    public void ClockHalfFrame()
    {
        if (!Envelope.LoopOrHalt && LengthCounter > 0) LengthCounter--;
        int target = ComputeSweepTarget();
        bool muted = TimerPeriod < 8 || (!SweepNegate && target > 0x7FF);
        if (SweepDivider == 0 && SweepEnabled && SweepShift > 0 && !muted) TimerPeriod = (ushort)Math.Max(0, target);
        if (SweepDivider == 0 || SweepReload) { SweepDivider = SweepPeriod; SweepReload = false; }
        else SweepDivider--;
    }

    private int ComputeSweepTarget()
    {
        int delta = TimerPeriod >> SweepShift;
        return SweepNegate ? TimerPeriod - delta - (_isPulse1 ? 1 : 0) : TimerPeriod + delta;
    }

    public int Output() => (!Enabled || LengthCounter == 0 || TimerPeriod < 8 || (!SweepNegate && ComputeSweepTarget() > 0x7FF) || Apu2A03Tables.PulseDuty[DutyMode][DutyStep] == 0) ? 0 : Envelope.Volume;

    public void WriteState(BinaryWriter w) { w.Write(Enabled); w.Write(LengthCounter); Envelope.WriteState(w); w.Write(DutyMode); w.Write(DutyStep); w.Write(TimerPeriod); w.Write(TimerCounter); w.Write(SweepEnabled); w.Write(SweepPeriod); w.Write(SweepNegate); w.Write(SweepShift); w.Write(SweepReload); w.Write(SweepDivider); }
    public void ReadState(BinaryReader r) { Enabled = r.ReadBoolean(); LengthCounter = r.ReadByte(); Envelope.ReadState(r); DutyMode = r.ReadByte(); DutyStep = r.ReadByte(); TimerPeriod = r.ReadUInt16(); TimerCounter = r.ReadUInt16(); SweepEnabled = r.ReadBoolean(); SweepPeriod = r.ReadByte(); SweepNegate = r.ReadBoolean(); SweepShift = r.ReadByte(); SweepReload = r.ReadBoolean(); SweepDivider = r.ReadByte(); }
}

internal sealed class ApuTriangleChannel
{
    public bool Enabled;
    public byte LengthCounter;
    public bool ControlFlag;
    public byte LinearReloadValue;
    public byte LinearCounter;
    public bool LinearReloadFlag;
    public ushort TimerPeriod;
    public ushort TimerCounter;
    public byte StepIndex;

    public void Reset() { Enabled = false; LengthCounter = 0; ControlFlag = false; LinearReloadValue = 0; LinearCounter = 0; LinearReloadFlag = false; TimerPeriod = 0; TimerCounter = 0; StepIndex = 0; }
    public void WriteRegister(int offset, byte val)
    {
        switch (offset)
        {
            case 0: ControlFlag = (val & 0x80) != 0; LinearReloadValue = (byte)(val & 0x7F); break;
            case 2: TimerPeriod = (ushort)((TimerPeriod & 0x0700) | val); break;
            case 3:
                TimerPeriod = (ushort)((TimerPeriod & 0x00FF) | ((val & 0x07) << 8));
                if (Enabled) LengthCounter = Apu2A03Tables.LengthTable[val >> 3];
                LinearReloadFlag = true;
                break;
        }
    }

    public void ClockTimer()
    {
        if (TimerCounter == 0) { TimerCounter = TimerPeriod; if (LengthCounter > 0 && LinearCounter > 0) StepIndex = (byte)((StepIndex + 1) & 31); }
        else TimerCounter--;
    }

    public void ClockQuarterFrame() { if (LinearReloadFlag) LinearCounter = LinearReloadValue; else if (LinearCounter > 0) LinearCounter--; if (!ControlFlag) LinearReloadFlag = false; }
    public void ClockHalfFrame() { if (!ControlFlag && LengthCounter > 0) LengthCounter--; }
    public int Output() => (TimerPeriod < 2 || LengthCounter == 0 || LinearCounter == 0) ? 0 : Apu2A03Tables.TriangleWave[StepIndex];
    public void WriteState(BinaryWriter w) { w.Write(Enabled); w.Write(LengthCounter); w.Write(ControlFlag); w.Write(LinearReloadValue); w.Write(LinearCounter); w.Write(LinearReloadFlag); w.Write(TimerPeriod); w.Write(TimerCounter); w.Write(StepIndex); }
    public void ReadState(BinaryReader r) { Enabled = r.ReadBoolean(); LengthCounter = r.ReadByte(); ControlFlag = r.ReadBoolean(); LinearReloadValue = r.ReadByte(); LinearCounter = r.ReadByte(); LinearReloadFlag = r.ReadBoolean(); TimerPeriod = r.ReadUInt16(); TimerCounter = r.ReadUInt16(); StepIndex = r.ReadByte(); }
}

internal sealed class ApuNoiseChannel
{
    public bool Enabled;
    public byte LengthCounter;
    public ApuEnvelope Envelope;
    public bool ModeFlag;
    public byte PeriodIndex;
    public ushort TimerCounter;
    public ushort Lfsr = 1;

    public void Reset() { Enabled = false; LengthCounter = 0; Envelope = default; ModeFlag = false; PeriodIndex = 0; TimerCounter = 0; Lfsr = 1; }
    public void WriteRegister(int offset, byte val)
    {
        switch (offset)
        {
            case 0: Envelope.WriteControl(val); break;
            case 2: ModeFlag = (val & 0x80) != 0; PeriodIndex = (byte)(val & 0x0F); break;
            case 3: if (Enabled) LengthCounter = Apu2A03Tables.LengthTable[val >> 3]; Envelope.StartFlag = true; break;
        }
    }

    public void ClockTimer(ReadOnlySpan<ushort> periodTable)
    {
        if (TimerCounter > 0) { TimerCounter--; return; }
        TimerCounter = (ushort)Math.Max(0, periodTable[PeriodIndex] - 1);
        int tap = ModeFlag ? 6 : 1;
        int fb = (Lfsr ^ (Lfsr >> tap)) & 1;
        Lfsr = (ushort)((Lfsr >> 1) | (fb << 14));
    }

    public void ClockHalfFrame() { if (!Envelope.LoopOrHalt && LengthCounter > 0) LengthCounter--; }
    public int Output() => (!Enabled || LengthCounter == 0 || (Lfsr & 1) != 0) ? 0 : Envelope.Volume;
    public void WriteState(BinaryWriter w) { w.Write(Enabled); w.Write(LengthCounter); Envelope.WriteState(w); w.Write(ModeFlag); w.Write(PeriodIndex); w.Write(TimerCounter); w.Write(Lfsr); }
    public void ReadState(BinaryReader r) { Enabled = r.ReadBoolean(); LengthCounter = r.ReadByte(); Envelope.ReadState(r); ModeFlag = r.ReadBoolean(); PeriodIndex = r.ReadByte(); TimerCounter = r.ReadUInt16(); Lfsr = r.ReadUInt16(); }
}

internal sealed class ApuDmcChannel
{
    public bool IrqEnabled, Loop, HasSampleBuffer, SilenceFlag = true, IrqPending;
    public byte RateIndex, SampleAddressReg, SampleLengthReg, SampleBuffer, ShiftRegister, OutputLevel;
    public byte BitsRemaining = 8;
    public ushort CurrentAddress = 0xC000, BytesRemaining, TimerCounter;
    public int PendingStallCycles;

    public void Reset() { IrqEnabled = false; Loop = false; HasSampleBuffer = false; SilenceFlag = true; IrqPending = false; RateIndex = 0; SampleAddressReg = 0; SampleLengthReg = 0; SampleBuffer = 0; ShiftRegister = 0; OutputLevel = 0; BitsRemaining = 8; CurrentAddress = 0xC000; BytesRemaining = 0; TimerCounter = 0; PendingStallCycles = 0; }
    public void WriteRegister(int offset, byte val)
    {
        switch (offset)
        {
            case 0: IrqEnabled = (val & 0x80) != 0; Loop = (val & 0x40) != 0; RateIndex = (byte)(val & 0x0F); if (!IrqEnabled) IrqPending = false; break;
            case 1: OutputLevel = (byte)(val & 0x7F); break;
            case 2: SampleAddressReg = val; break;
            case 3: SampleLengthReg = val; break;
        }
    }

    public void SetEnabled(bool enable, Func<ushort, byte>? memoryRead)
    {
        IrqPending = false;
        if (!enable) { BytesRemaining = 0; return; }
        if (BytesRemaining == 0) { RestartSample(); TryFetchSample(memoryRead); }
    }

    public void ClockCycle(ReadOnlySpan<ushort> rateTable, Func<ushort, byte>? memoryRead)
    {
        if (BytesRemaining > 0 && !HasSampleBuffer) TryFetchSample(memoryRead);
        if (TimerCounter > 0) { TimerCounter--; return; }
        TimerCounter = (ushort)Math.Max(0, rateTable[RateIndex] - 1);
        if (!SilenceFlag)
        {
            if ((ShiftRegister & 1) != 0 && OutputLevel <= 125) OutputLevel += 2;
            else if ((ShiftRegister & 1) == 0 && OutputLevel >= 2) OutputLevel -= 2;
        }
        ShiftRegister >>= 1;
        if (--BitsRemaining == 0)
        {
            BitsRemaining = 8;
            if (HasSampleBuffer) { ShiftRegister = SampleBuffer; HasSampleBuffer = false; SilenceFlag = false; }
            else SilenceFlag = true;
        }
    }

    private void RestartSample() { CurrentAddress = (ushort)(0xC000 + (SampleAddressReg << 6)); BytesRemaining = (ushort)((SampleLengthReg << 4) + 1); }

    private void TryFetchSample(Func<ushort, byte>? memoryRead)
    {
        SampleBuffer = memoryRead?.Invoke(CurrentAddress) ?? 0;
        HasSampleBuffer = true;
        PendingStallCycles += 4;
        CurrentAddress = CurrentAddress == 0xFFFF ? (ushort)0x8000 : (ushort)(CurrentAddress + 1);
        if (--BytesRemaining == 0)
        {
            if (Loop) RestartSample();
            else if (IrqEnabled) IrqPending = true;
        }
    }

    public int Output() => OutputLevel;

    public void WriteState(BinaryWriter w) { w.Write(IrqEnabled); w.Write(Loop); w.Write(HasSampleBuffer); w.Write(SilenceFlag); w.Write(IrqPending); w.Write(RateIndex); w.Write(SampleAddressReg); w.Write(SampleLengthReg); w.Write(SampleBuffer); w.Write(ShiftRegister); w.Write(OutputLevel); w.Write(BitsRemaining); w.Write(CurrentAddress); w.Write(BytesRemaining); w.Write(TimerCounter); w.Write(PendingStallCycles); }
    public void ReadState(BinaryReader r) { IrqEnabled = r.ReadBoolean(); Loop = r.ReadBoolean(); HasSampleBuffer = r.ReadBoolean(); SilenceFlag = r.ReadBoolean(); IrqPending = r.ReadBoolean(); RateIndex = r.ReadByte(); SampleAddressReg = r.ReadByte(); SampleLengthReg = r.ReadByte(); SampleBuffer = r.ReadByte(); ShiftRegister = r.ReadByte(); OutputLevel = r.ReadByte(); BitsRemaining = r.ReadByte(); CurrentAddress = r.ReadUInt16(); BytesRemaining = r.ReadUInt16(); TimerCounter = r.ReadUInt16(); PendingStallCycles = r.ReadInt32(); }
}
