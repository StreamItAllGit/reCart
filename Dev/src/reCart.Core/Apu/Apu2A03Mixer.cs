// @module: reCart.Core.Apu.Apu2A03Mixer
// @purpose: Non-linear RP2A03 Pulse/TND resistor-ladder LUT mixer and DC-blocking stereo PCM converter.
// @depends-on: System, System.IO
// @exports: Apu2A03Mixer

namespace reCart.Core.Apu;

/// <summary>
/// Implements hardware-calibrated RP2A03 non-linear Pulse + TND lookup tables and
/// 1-pole high-pass DC blocking to produce clean stereo interleaved 16-bit PCM frames.
/// </summary>
internal sealed class Apu2A03Mixer
{
    private static readonly float[] PulseLut = BuildPulseLut();
    private static readonly float[] TndLut = BuildTndLut();

    private float _dcPrevIn;
    private float _dcPrevOut;

    private static float[] BuildPulseLut()
    {
        float[] lut = new float[31];
        for (int i = 1; i < lut.Length; i++)
        {
            lut[i] = 95.52f / ((8128.0f / i) + 100.0f);
        }

        return lut;
    }

    private static float[] BuildTndLut()
    {
        float[] lut = new float[203];
        for (int i = 1; i < lut.Length; i++)
        {
            lut[i] = 163.67f / ((24329.0f / i) + 100.0f);
        }

        return lut;
    }

    public void Reset()
    {
        _dcPrevIn = 0f;
        _dcPrevOut = 0f;
    }

    public float MixNormalizedMono(int pulse1, int pulse2, int triangle, int noise, int dmc)
    {
        int pulseSum = Math.Clamp(pulse1 + pulse2, 0, 30);
        int tndSum = Math.Clamp((3 * triangle) + (2 * noise) + dmc, 0, 202);
        float raw = PulseLut[pulseSum] + TndLut[tndSum];

        float filtered = raw - _dcPrevIn + (0.996f * _dcPrevOut);
        _dcPrevIn = raw;
        _dcPrevOut = filtered;
        return Math.Clamp(filtered * 1.4f, -1.0f, 1.0f);
    }

    public void MixStereoFloat(int pulse1, int pulse2, int triangle, int noise, int dmc, Span<float> stereoPair)
    {
        float mono = MixNormalizedMono(pulse1, pulse2, triangle, noise, dmc);
        stereoPair[0] = mono;
        stereoPair[1] = mono;
    }

    public void WriteState(BinaryWriter writer)
    {
        writer.Write(_dcPrevIn);
        writer.Write(_dcPrevOut);
    }

    public void ReadState(BinaryReader reader)
    {
        _dcPrevIn = reader.ReadSingle();
        _dcPrevOut = reader.ReadSingle();
    }
}
