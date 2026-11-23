// @module: reCart.Compiler.PrgBankLayout
// @purpose: Bank-aware PRG-ROM address mapping for NROM, MMC1, UxROM, CNROM, and general NES mappers.
// @depends-on: System, reCart.Contracts
// @exports: PrgBankLayout

using reCart.Contracts;

namespace reCart.Compiler;

internal sealed class PrgBankLayout
{
    public const ushort PrgBaseAddress = 0x8000;
    public const int BankSize16K = 16384;
    public const int BankSize32K = 32768;

    public ushort MapperId { get; }
    public int PrgSizeBytes { get; }
    public int BankCount { get; }
    public uint FixedBankIndex { get; }
    public bool UsesSwitchableLowWindow { get; }

    public PrgBankLayout(CartRomHeader header)
    {
        MapperId = header.MapperId;
        PrgSizeBytes = header.PrgRomSizeBytes;

        if (PrgSizeBytes <= BankSize16K)
        {
            BankCount = 1;
            FixedBankIndex = 0;
            UsesSwitchableLowWindow = false;
        }
        else if (PrgSizeBytes == BankSize32K && MapperId is 0 or 3)
        {
            BankCount = 1;
            FixedBankIndex = 0;
            UsesSwitchableLowWindow = false;
        }
        else
        {
            BankCount = Math.Max(1, PrgSizeBytes / BankSize16K);
            FixedBankIndex = (uint)(BankCount - 1);
            UsesSwitchableLowWindow = BankCount > 1 && MapperId is not 0 and not 3;
        }
    }

    public bool IsFixedAddress(ushort address) =>
        !UsesSwitchableLowWindow || address >= 0xC000;

    public uint NormalizeBank(uint prgBank, ushort address)
    {
        if (!UsesSwitchableLowWindow) return 0;
        if (address >= 0xC000) return FixedBankIndex;
        return (uint)(prgBank % (uint)BankCount);
    }

    public int MapAddressToOffset(uint prgBank, ushort address)
    {
        if (address < PrgBaseAddress || PrgSizeBytes <= 0) return -1;
        if (PrgSizeBytes <= BankSize16K)
        {
            return (address - PrgBaseAddress) % PrgSizeBytes;
        }
        if (!UsesSwitchableLowWindow)
        {
            return (address - PrgBaseAddress) % PrgSizeBytes;
        }

        uint effectiveBank = NormalizeBank(prgBank, address);
        int localOffset = address & 0x3FFF;
        int offset = (int)(effectiveBank * BankSize16K) + localOffset;
        return offset < PrgSizeBytes ? offset : -1;
    }

    public ushort ReadRom16(ReadOnlySpan<byte> prgRom, uint prgBank, ushort address, bool pageWrap6502 = false)
    {
        int loOffset = MapAddressToOffset(prgBank, address);
        ushort hiAddr = pageWrap6502
            ? (ushort)((address & 0xFF00) | ((address + 1) & 0x00FF))
            : (ushort)(address + 1);
        int hiOffset = MapAddressToOffset(prgBank, hiAddr);
        if (loOffset < 0 || hiOffset < 0 || loOffset >= prgRom.Length || hiOffset >= prgRom.Length)
        {
            return 0;
        }
        return (ushort)(prgRom[loOffset] | (prgRom[hiOffset] << 8));
    }
}
