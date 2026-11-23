// @module: reCart.Core.Runtime.CartQuickSaveSerializer
// @purpose: Deterministic binary F5/F9 QuickSave encoder/decoder with magic header and SHA-256 integrity validation.
// @depends-on: System, System.IO, System.Security.Cryptography, System.Text, reCart.Contracts
// @exports: CartQuickSaveSerializer

using System.Security.Cryptography;
using System.Text;
using reCart.Contracts;

namespace reCart.Core.Runtime;

/// <summary>
/// Serializes and validates deterministic machine snapshots with an <c>RCARTQS1</c> magic header
/// and trailing 32-byte <c>SHA-256</c> integrity digest.
/// </summary>
internal static class CartQuickSaveSerializer
{
    private static readonly byte[] MagicHeader = "RCARTQS1"u8.ToArray();
    private const int FormatVersion = 1;
    private const int WramSizeBytes = 2048;
    private const int Sha256SizeBytes = 32;

    public static byte[] Capture(
        ICartCpuContext cpu,
        ICartMemoryBus bus,
        ICartPpuController ppu,
        ICartApuSink apu,
        ICartMapper mapper,
        CartRegionTimingProfile timing,
        CartExecutionDiagnostics diag,
        bool resetDispatched,
        int ppuDotDebt,
        int cpuCycleCredit)
    {
        byte[] wram = new byte[WramSizeBytes];
        for (int i = 0; i < WramSizeBytes; i++)
        {
            wram[i] = bus.Read((ushort)i);
        }

        byte[] cpuState = cpu.CaptureState();
        byte[] ppuState = ppu.CaptureState();
        byte[] apuState = apu.CaptureState();
        byte[] mapperState = mapper.CaptureState();

        using MemoryStream ms = new();
        using BinaryWriter w = new(ms, Encoding.UTF8, leaveOpen: true);
        w.Write(MagicHeader);
        w.Write(FormatVersion);
        w.Write((int)timing.Region);
        w.Write(mapper.MapperId);
        w.Write(mapper.SubmapperId);
        w.Write(diag.FramesRendered);
        w.Write(diag.StaticDispatchCount);
        w.Write(diag.InterpreterDispatchCount);
        w.Write(diag.UnliftedTrapCount);
        w.Write(diag.AudioDroppedSamples);
        w.Write(resetDispatched);
        w.Write(ppuDotDebt);
        w.Write(cpuCycleCredit);
        w.Write(wram);
        WriteChunk(w, cpuState);
        WriteChunk(w, ppuState);
        WriteChunk(w, apuState);
        WriteChunk(w, mapperState);
        w.Flush();

        byte[] payload = ms.ToArray();
        byte[] digest = SHA256.HashData(payload);
        byte[] signed = new byte[payload.Length + Sha256SizeBytes];
        payload.CopyTo(signed, 0);
        digest.CopyTo(signed, payload.Length);
        return signed;
    }

    public static bool TryRestore(
        ReadOnlySpan<byte> snapshotBytes,
        ICartCpuContext cpu,
        ICartMemoryBus bus,
        ICartPpuController ppu,
        ICartApuSink apu,
        ICartMapper mapper,
        CartExecutionDiagnostics diag,
        out bool resetDispatched,
        out int ppuDotDebt,
        out int cpuCycleCredit,
        out string diagnostic)
    {
        resetDispatched = false;
        ppuDotDebt = 0;
        cpuCycleCredit = 0;

        if (snapshotBytes.Length < MagicHeader.Length + sizeof(int) + Sha256SizeBytes)
        {
            diagnostic = "QuickSave payload is truncated.";
            return false;
        }

        if (!snapshotBytes[..MagicHeader.Length].SequenceEqual(MagicHeader))
        {
            diagnostic = "QuickSave magic header mismatch.";
            return false;
        }

        ReadOnlySpan<byte> payload = snapshotBytes[..^Sha256SizeBytes];
        ReadOnlySpan<byte> expectedHash = snapshotBytes[^Sha256SizeBytes..];
        Span<byte> actualHash = stackalloc byte[Sha256SizeBytes];
        SHA256.HashData(payload, actualHash);
        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
        {
            diagnostic = "QuickSave SHA-256 integrity check failed.";
            return false;
        }

        try
        {
            using MemoryStream ms = new(payload.ToArray());
            using BinaryReader r = new(ms, Encoding.UTF8, leaveOpen: true);
            r.BaseStream.Position = MagicHeader.Length;

            int version = r.ReadInt32();
            if (version != FormatVersion)
            {
                diagnostic = $"Unsupported QuickSave version {version}.";
                return false;
            }

            _ = r.ReadInt32(); // region metadata
            ushort mapperId = r.ReadUInt16();
            byte submapperId = r.ReadByte();
            if (mapperId != mapper.MapperId || submapperId != mapper.SubmapperId)
            {
                diagnostic = $"QuickSave mapper {mapperId}.{submapperId} does not match active mapper {mapper.MapperId}.{mapper.SubmapperId}.";
                return false;
            }

            long frames = r.ReadInt64();
            long staticCount = r.ReadInt64();
            long interpCount = r.ReadInt64();
            long trapCount = r.ReadInt64();
            long dropped = r.ReadInt64();
            bool restoredReset = r.ReadBoolean();
            int restoredDotDebt = r.ReadInt32();
            int restoredCycleCredit = r.ReadInt32();

            byte[] wram = r.ReadBytes(WramSizeBytes);
            if (wram.Length != WramSizeBytes)
            {
                diagnostic = "QuickSave WRAM block is truncated.";
                return false;
            }

            byte[] cpuState = ReadChunk(r);
            byte[] ppuState = ReadChunk(r);
            byte[] apuState = ReadChunk(r);
            byte[] mapperState = ReadChunk(r);

            cpu.RestoreState(cpuState);
            for (int i = 0; i < WramSizeBytes; i++)
            {
                bus.Write((ushort)i, wram[i]);
            }

            ppu.RestoreState(ppuState);
            apu.RestoreState(apuState);
            mapper.RestoreState(mapperState);

            diag.FramesRendered = frames;
            diag.StaticDispatchCount = staticCount;
            diag.InterpreterDispatchCount = interpCount;
            diag.UnliftedTrapCount = trapCount;
            diag.AudioDroppedSamples = dropped;
            resetDispatched = restoredReset;
            ppuDotDebt = restoredDotDebt;
            cpuCycleCredit = restoredCycleCredit;

            diagnostic = "QuickSave restored cleanly.";
            return true;
        }
        catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException or ArgumentException)
        {
            diagnostic = $"QuickSave payload is malformed: {ex.Message}";
            return false;
        }
    }

    private static void WriteChunk(BinaryWriter writer, byte[] data)
    {
        writer.Write(data.Length);
        writer.Write(data);
    }

    private static byte[] ReadChunk(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
        if (length < 0 || length > remaining)
        {
            throw new InvalidDataException($"Invalid QuickSave chunk length {length}.");
        }

        return reader.ReadBytes(length);
    }
}
