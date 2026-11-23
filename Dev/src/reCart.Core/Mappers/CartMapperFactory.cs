// @module: reCart.Core.Mappers.CartMapperFactory
// @purpose: Mapper factory (Mappers 0-3), internal direct bank-control seam, and shared mapper base memory/state helpers.
// @depends-on: System, System.Buffers.Binary, reCart.Contracts, reCart.Core.Rom
// @exports: CartMapperFactory

using System.Buffers.Binary;
using reCart.Contracts;
using reCart.Core.Rom;

namespace reCart.Core.Mappers;

internal interface ICartDirectBankControl
{
    void SetPrgBank(uint bank);
    void SetChrBank(uint bank);
}

/// <summary>
/// Shared clean-room base for NES cartridge mappers providing PRG/CHR/SRAM storage and deterministic state framing.
/// </summary>
public abstract class CartMapperBase : ICartMapper, ICartDirectBankControl
{
    private const int StateHeaderSize = 20;

    protected readonly byte[] PrgRom;
    protected readonly byte[] ChrMemory;
    protected readonly byte[] PrgRam;
    protected readonly bool HasChrRam;

    protected CartMapperBase(InesRomImage rom, int minPrgRamBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(rom);
        MapperId = rom.Header.MapperId;
        SubmapperId = rom.Header.SubmapperId;
        InitialMirroring = rom.Header.Mirroring;
        Mirroring = rom.Header.Mirroring;

        PrgRom = rom.CopyPrgArray();
        HasChrRam = rom.UsesChrRam;
        ChrMemory = HasChrRam
            ? new byte[Math.Max(8192, rom.Header.ChrRamSizeBytes)]
            : rom.CopyChrArray();

        int ramBytes = Math.Max(rom.Header.PrgRamSizeBytes, rom.Header.HasBatteryBackedRam ? Math.Max(8192, minPrgRamBytes) : minPrgRamBytes);
        PrgRam = ramBytes > 0 ? new byte[ramBytes] : Array.Empty<byte>();
    }

    /// <inheritdoc />
    public ushort MapperId { get; }

    /// <inheritdoc />
    public byte SubmapperId { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public CartMirroringMode Mirroring { get; protected set; }

    /// <inheritdoc />
    public uint ActivePrgBank { get; protected set; }

    /// <inheritdoc />
    public uint ActiveChrBank { get; protected set; }

    /// <inheritdoc />
    public virtual bool IsIrqPending => false;

    /// <summary>Initial mirroring mode decoded from the cartridge header.</summary>
    protected CartMirroringMode InitialMirroring { get; }

    /// <summary>Read-only view of cartridge PRG-RAM / battery SRAM.</summary>
    public ReadOnlySpan<byte> BatteryRam => PrgRam;

    /// <inheritdoc />
    public abstract byte CpuRead(ushort address);

    /// <inheritdoc />
    public abstract void CpuWrite(ushort address, byte value);

    /// <inheritdoc />
    public virtual byte PpuRead(ushort address) => ChrMemory[(address & 0x1FFF) % ChrMemory.Length];

    /// <inheritdoc />
    public virtual void PpuWrite(ushort address, byte value)
    {
        if (HasChrRam)
        {
            ChrMemory[(address & 0x1FFF) % ChrMemory.Length] = value;
        }
    }

    /// <inheritdoc />
    public virtual void OnPpuA12Edge(bool a12High, int scanline, int dot)
    {
    }

    /// <inheritdoc />
    public abstract void Reset();

    /// <inheritdoc />
    public abstract byte[] CaptureState();

    /// <inheritdoc />
    public abstract void RestoreState(ReadOnlySpan<byte> state);

    void ICartDirectBankControl.SetPrgBank(uint bank) => OnDirectPrgBankSwitch(bank);

    void ICartDirectBankControl.SetChrBank(uint bank) => OnDirectChrBankSwitch(bank);

    protected virtual void OnDirectPrgBankSwitch(uint bank)
    {
    }

    protected virtual void OnDirectChrBankSwitch(uint bank)
    {
    }

    protected byte ReadPrgRam(ushort address) => PrgRam.Length > 0
        ? PrgRam[(address - 0x6000) % PrgRam.Length]
        : OpenBus(address);

    protected void WritePrgRam(ushort address, byte value)
    {
        if (PrgRam.Length > 0)
        {
            PrgRam[(address - 0x6000) % PrgRam.Length] = value;
        }
    }

    protected static byte OpenBus(ushort address) => (byte)(address >> 8);

    protected byte[] EncodeState(ReadOnlySpan<byte> registerState)
    {
        int chrLen = HasChrRam ? ChrMemory.Length : 0;
        byte[] payload = new byte[StateHeaderSize + registerState.Length + 8 + PrgRam.Length + chrLen];
        payload[0] = (byte)'M';
        payload[1] = (byte)'P';
        payload[2] = (byte)'R';
        payload[3] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), MapperId);
        payload[6] = SubmapperId;
        payload[7] = (byte)Mirroring;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), ActivePrgBank);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), ActiveChrBank);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16, 4), registerState.Length);

        int pos = StateHeaderSize;
        registerState.CopyTo(payload.AsSpan(pos, registerState.Length));
        pos += registerState.Length;

        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(pos, 4), PrgRam.Length);
        pos += 4;
        PrgRam.CopyTo(payload, pos);
        pos += PrgRam.Length;

        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(pos, 4), chrLen);
        pos += 4;
        if (chrLen > 0)
        {
            ChrMemory.CopyTo(payload, pos);
        }

        return payload;
    }

    protected ReadOnlySpan<byte> DecodeState(ReadOnlySpan<byte> payload, int expectedRegisterLength)
    {
        if (payload.Length < StateHeaderSize + expectedRegisterLength + 8
            || payload[0] != 'M' || payload[1] != 'P' || payload[2] != 'R' || payload[3] != 1)
        {
            throw new InvalidDataException($"Invalid state snapshot header for mapper {MapperId}.");
        }

        ushort storedMapperId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
        if (storedMapperId != MapperId)
        {
            throw new InvalidDataException($"Mapper state mismatch: expected {MapperId}, got {storedMapperId}.");
        }

        int regLen = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(16, 4));
        if (regLen != expectedRegisterLength)
        {
            throw new InvalidDataException($"Mapper {MapperId} register state length mismatch: expected {expectedRegisterLength}, got {regLen}.");
        }

        Mirroring = (CartMirroringMode)payload[7];
        ActivePrgBank = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(8, 4));
        ActiveChrBank = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(12, 4));

        int pos = StateHeaderSize;
        ReadOnlySpan<byte> regs = payload.Slice(pos, regLen);
        pos += regLen;

        int ramLen = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(pos, 4));
        pos += 4;
        if (ramLen != PrgRam.Length || payload.Length < pos + ramLen + 4)
        {
            throw new InvalidDataException($"Mapper {MapperId} PRG-RAM snapshot size mismatch.");
        }

        if (ramLen > 0)
        {
            payload.Slice(pos, ramLen).CopyTo(PrgRam);
            pos += ramLen;
        }

        int chrLen = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(pos, 4));
        pos += 4;
        int expectedChrLen = HasChrRam ? ChrMemory.Length : 0;
        if (chrLen != expectedChrLen || payload.Length < pos + chrLen)
        {
            throw new InvalidDataException($"Mapper {MapperId} CHR-RAM snapshot size mismatch.");
        }

        if (chrLen > 0)
        {
            payload.Slice(pos, chrLen).CopyTo(ChrMemory);
        }

        return regs;
    }
}

/// <summary>
/// Instantiates clean-room <see cref="ICartMapper"/> implementations for Mappers <c>0–3</c>.
/// </summary>
public static class CartMapperFactory
{
    /// <summary>Returns whether <paramref name="mapperId"/> is supported in <c>reCart v0.1.0</c>.</summary>
    public static bool IsSupported(ushort mapperId) => mapperId <= 3;

    /// <summary>Creates the appropriate <see cref="ICartMapper"/> instance for <paramref name="rom"/>.</summary>
    public static ICartMapper Create(InesRomImage rom)
    {
        ArgumentNullException.ThrowIfNull(rom);
        return rom.Header.MapperId switch
        {
            0 => new NromMapper(rom),
            1 => new Mmc1Mapper(rom),
            2 => new UxRomMapper(rom),
            3 => new CnRomMapper(rom),
            _ => throw new NotSupportedException(
                $"Mapper {rom.Header.MapperId} (submapper {rom.Header.SubmapperId}) is not supported in reCart v0.1.0.")
        };
    }
}
