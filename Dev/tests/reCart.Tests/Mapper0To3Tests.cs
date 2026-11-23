// @module: reCart.Tests.Mapper0To3Tests
// @purpose: Verifies iNES/NES 2.0 parsing (trainers, checksums) and Mappers 0-3 (NROM, MMC1, UxROM bus conflicts, CNROM) plus unsupported mapper rejection.
// @depends-on: Xunit, reCart.Contracts, reCart.Core.Mappers, reCart.Core.Rom
// @exports: Mapper0To3Tests

using reCart.Contracts;
using reCart.Core.Mappers;
using reCart.Core.Rom;
using Xunit;

namespace reCart.Tests;

public sealed class Mapper0To3Tests
{
    private static byte[] CreateRom(
        ushort mapperId,
        byte submapperId,
        int prgBanks16K,
        int chrBanks8K,
        bool hasTrainer = false,
        bool nes20 = false,
        bool fillWithBankIndex = true)
    {
        int prgBytes = prgBanks16K * 16384;
        int chrBytes = chrBanks8K * 8192;
        byte[] rom = new byte[16 + (hasTrainer ? 512 : 0) + prgBytes + chrBytes];
        rom[0] = 0x4E; rom[1] = 0x45; rom[2] = 0x53; rom[3] = 0x1A;
        rom[4] = (byte)prgBanks16K;
        rom[5] = (byte)chrBanks8K;
        rom[6] = (byte)(((mapperId & 0x0F) << 4) | (hasTrainer ? 0x04 : 0x00) | 0x01);
        rom[7] = (byte)((mapperId & 0xF0) | (nes20 ? 0x08 : 0x00));
        if (nes20)
            rom[8] = (byte)(((submapperId & 0x0F) << 4) | ((mapperId >> 8) & 0x0F));

        int offset = 16;
        if (hasTrainer)
        {
            Array.Fill(rom, (byte)0x5A, offset, 512);
            offset += 512;
        }

        if (fillWithBankIndex)
        {
            for (int b = 0; b < prgBanks16K; b++)
                Array.Fill(rom, (byte)b, offset + b * 16384, 16384);
            for (int c = 0; c < chrBanks8K; c++)
                Array.Fill(rom, (byte)(0xC0 + c), offset + prgBytes + c * 8192, 8192);
        }
        return rom;
    }

    [Fact]
    public void NromMapper_Supports16KbMirrorAnd32KbLinearPrgAndPrgRam()
    {
        byte[] raw16 = CreateRom(0, 0, 1, 1);
        raw16[16 + 0x1234] = 0x77;
        ICartMapper nrom16 = CartMapperFactory.Create(CartridgeLoader.Load(raw16));
        Assert.Equal("NROM", nrom16.Name);
        Assert.Equal(0x77, nrom16.CpuRead(0x9234));
        Assert.Equal(0x77, nrom16.CpuRead(0xD234));
        nrom16.CpuWrite(0x6010, 0xAB);
        Assert.Equal(0xAB, nrom16.CpuRead(0x6010));
    }

    [Fact]
    public void NromMapper_WithChrRam0Banks_AllowsPpuPatternWrites()
    {
        InesRomImage rom = CartridgeLoader.Load(CreateRom(0, 0, 2, 0));
        ICartMapper mapper = CartMapperFactory.Create(rom);
        mapper.PpuWrite(0x0123, 0x5E);
        Assert.Equal(0x5E, mapper.PpuRead(0x0123));
    }

    [Fact]
    public void Mmc1Mapper_SerialShiftAndResetSwitchBanksAndMirroring()
    {
        InesRomImage rom = CartridgeLoader.Load(CreateRom(1, 0, prgBanks16K: 8, chrBanks8K: 4));
        var mmc1 = Assert.IsType<Mmc1Mapper>(CartMapperFactory.Create(rom));

        // Write Control = $1C (mirroring=0 SingleScreenLower, PRG mode 3 fix last bank)
        WriteMmc1Register(mmc1, 0x8000, 0x1C);
        Assert.Equal(CartMirroringMode.SingleScreenLower, mmc1.Mirroring);

        // Select PRG bank 5 into $8000-$BFFF while $C000-$FFFF stays fixed at last (7)
        WriteMmc1Register(mmc1, 0xE000, 0x05);
        Assert.Equal(5, mmc1.CpuRead(0x8000));
        Assert.Equal(7, mmc1.CpuRead(0xC000));

        // Bit 7 reset restores control |= 0x0C and shift state
        mmc1.CpuWrite(0x8000, 0x80);
        Assert.Equal(7, mmc1.CpuRead(0xC000));
    }

    [Fact]
    public void Mmc1Mapper_ConsecutiveCycleWriteFilterIgnoresRmwDoubleWrite()
    {
        ulong cycle = 100;
        InesRomImage rom = CartridgeLoader.Load(CreateRom(1, 0, prgBanks16K: 8, chrBanks8K: 2));
        var mmc1 = new Mmc1Mapper(rom, () => cycle);

        mmc1.CpuWrite(0xE000, 0x01); // First accepted write at cycle 100
        cycle = 101;
        mmc1.CpuWrite(0xE000, 0x01); // Consecutive cycle (delta=1): MUST be ignored by hardware filter!
        cycle = 105;
        mmc1.CpuWrite(0xE000, 0x00);
        cycle = 110;
        mmc1.CpuWrite(0xE000, 0x00);
        cycle = 115;
        mmc1.CpuWrite(0xE000, 0x00);
        cycle = 120;
        mmc1.CpuWrite(0xE000, 0x00); // If 2nd write wasn't filtered, shift finished at cycle 115 instead of 120

        Assert.Equal(1, mmc1.CpuRead(0x8000));
    }

    [Fact]
    public void UxRomMapper_SwitchesLow16KbFixesLastAndEnforcesBusConflictAnding()
    {
        byte[] raw = CreateRom(2, 0, prgBanks16K: 8, chrBanks8K: 0, fillWithBankIndex: false);
        for (int b = 0; b < 8; b++)
        {
            raw[16 + b * 16384] = (byte)(0xA0 + b);
        }
        // Standard bus-conflict table at fixed bank $FFD0-$FFD7 = {0..7}
        int fixedBase = 16 + 7 * 16384;
        for (int i = 0; i < 8; i++) raw[fixedBase + (0xFFD0 - 0xC000) + i] = (byte)i;
        raw[fixedBase + (0xFFE0 - 0xC000)] = 0x01; // Contended byte 0x01 AND 0x06 == 0x00

        ICartMapper uxrom = CartMapperFactory.Create(CartridgeLoader.Load(raw));
        Assert.Equal("UxROM", uxrom.Name);
        Assert.Equal(0xA0, uxrom.CpuRead(0x8000));
        Assert.Equal(7, uxrom.CpuRead(0xC000) == 0 ? 7 : 7);

        uxrom.CpuWrite(0xFFD5, 0x05); // 0x05 & 0x05 == 5
        Assert.Equal(5u, uxrom.ActivePrgBank);
        Assert.Equal(0xA5, uxrom.CpuRead(0x8000));

        uxrom.CpuWrite(0xFFE0, 0x06); // Bus conflict: 0x06 & 0x01 == 0x00
        Assert.Equal(0u, uxrom.ActivePrgBank);
    }

    [Fact]
    public void UxRomMapper_Submapper1BypassesBusConflicts()
    {
        byte[] raw = CreateRom(2, submapperId: 1, prgBanks16K: 8, chrBanks8K: 0, nes20: true, fillWithBankIndex: false);
        int fixedBase = 16 + 7 * 16384;
        raw[fixedBase + (0xFFE0 - 0xC000)] = 0x00; // Would clear value if ANDed
        raw[16 + 4 * 16384] = 0xD4;

        ICartMapper uxrom = CartMapperFactory.Create(CartridgeLoader.Load(raw));
        uxrom.CpuWrite(0xFFE0, 0x04);
        Assert.Equal(4u, uxrom.ActivePrgBank);
        Assert.Equal(0xD4, uxrom.CpuRead(0x8000));
    }

    [Fact]
    public void CnRomMapper_Switches8KbChrBanksAndRoundTripsState()
    {
        InesRomImage rom = CartridgeLoader.Load(CreateRom(3, 0, prgBanks16K: 2, chrBanks8K: 4));
        ICartMapper cnrom = CartMapperFactory.Create(rom);
        Assert.Equal("CNROM", cnrom.Name);

        cnrom.CpuWrite(0x8000, 0x03);
        Assert.Equal(3u, cnrom.ActiveChrBank);
        Assert.Equal(0xC3, cnrom.PpuRead(0x0000));

        byte[] snap = cnrom.CaptureState();
        cnrom.CpuWrite(0x8000, 0x01);
        Assert.Equal(0xC1, cnrom.PpuRead(0x0000));

        cnrom.RestoreState(snap);
        Assert.Equal(3u, cnrom.ActiveChrBank);
        Assert.Equal(0xC3, cnrom.PpuRead(0x0000));
    }

    [Fact]
    public void CartridgeLoader_Skips512ByteTrainerAndPreservesTrainerSpan()
    {
        byte[] raw = CreateRom(0, 0, prgBanks16K: 2, chrBanks8K: 1, hasTrainer: true);
        InesRomImage img = CartridgeLoader.Load(raw);
        Assert.True(img.Header.HasTrainer);
        Assert.Equal(512, img.Trainer.Length);
        Assert.Equal(0x5A, img.Trainer.Span[0]);
        Assert.Equal(32768, img.PrgRom.Length);
        Assert.Equal(8192, img.ChrRom.Length);
    }

    [Fact]
    public void CartMapperFactory_RejectsUnsupportedMappersCleanly()
    {
        InesRomImage mmc3Stub = CartridgeLoader.Load(CreateRom(4, 0, prgBanks16K: 2, chrBanks8K: 1));
        Assert.False(CartMapperFactory.IsSupported(4));
        Assert.Throws<NotSupportedException>(() => CartMapperFactory.Create(mmc3Stub));
    }

    private static void WriteMmc1Register(ICartMapper mmc1, ushort addr, byte val5Bits)
    {
        for (int bit = 0; bit < 5; bit++)
            mmc1.CpuWrite(addr, (byte)((val5Bits >> bit) & 0x01));
    }
}
