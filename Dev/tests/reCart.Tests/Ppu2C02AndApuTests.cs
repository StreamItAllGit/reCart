// @module: reCart.Tests.Ppu2C02AndApuTests
// @purpose: Verifies dot-stepped RP2C02 PPU timing (NTSC/PAL/Dendy), VBlank/NMI/Sprite0/OAMDMA, 5-channel RP2A03 APU, zero-drop AudioRingBuffer, and QuickSave SHA-256 verification.
// @depends-on: Xunit, reCart.Contracts, reCart.Core.Apu, reCart.Core.Cpu, reCart.Core.Input, reCart.Core.Mappers, reCart.Core.Ppu, reCart.Core.Rom, reCart.Core.Runtime
// @exports: Ppu2C02AndApuTests

using System.Buffers.Binary;
using reCart.Contracts;
using reCart.Core.Apu;
using reCart.Core.Cpu;
using reCart.Core.Input;
using reCart.Core.Mappers;
using reCart.Core.Ppu;
using reCart.Core.Rom;
using reCart.Core.Runtime;
using Xunit;

namespace reCart.Tests;

public sealed class Ppu2C02AndApuTests
{
    private static CartMachineHost CreateBlankHost(CartVideoRegionStandard region = CartVideoRegionStandard.Ntsc)
    {
        byte[] rom = new byte[16 + 32768 + 8192];
        rom[0] = 0x4E; rom[1] = 0x45; rom[2] = 0x53; rom[3] = 0x1A;
        rom[4] = 2; rom[5] = 1; rom[6] = 0x01;
        Array.Fill(rom, (byte)0xEA, 16, 32768);
        BinaryPrimitives.WriteUInt16LittleEndian(rom.AsSpan(16 + 0x7FFC, 2), 0x8000);
        return CartMachineFactory.CreateFromRomBytes(rom, region, CartExecutionTierMode.StaticAotOnly);
    }

    private static byte[] CreateBlankRomBytes()
    {
        byte[] raw = new byte[16 + 32768 + 8192];
        raw[0] = 0x4E; raw[1] = 0x45; raw[2] = 0x53; raw[3] = 0x1A;
        raw[4] = 2; raw[5] = 1; raw[6] = 0x01;
        return raw;
    }

    [Fact]
    public void Ppu2C02_NtscOddFrameWithRenderingEnabled_SkipsLastPreRenderDot()
    {
        InesRomImage rom = CartridgeLoader.Load(CreateBlankRomBytes());
        var ppu = new Ppu2C02Core(CartMapperFactory.Create(rom), CartRegionTimingProfile.Ntsc);
        ppu.WriteRegister(0x2001, 0x18); // Enable BG + Sprites rendering

        int dotsFrame0 = 0; while (!ppu.StepDot()) dotsFrame0++; dotsFrame0++;
        int dotsFrame1 = 0; while (!ppu.StepDot()) dotsFrame1++; dotsFrame1++;

        Assert.Equal(262 * 341, dotsFrame0);
        Assert.Equal(262 * 341 - 1, dotsFrame1);
    }

    [Theory]
    [InlineData(CartVideoRegionStandard.Ntsc, 262, 341)]
    [InlineData(CartVideoRegionStandard.Pal, 312, 341)]
    [InlineData(CartVideoRegionStandard.Dendy, 312, 341)]
    public void Ppu2C02_ProducesRegionExactTotalFrameDotsWhenRenderingOff(CartVideoRegionStandard region, int scanlines, int dotsPerLine)
    {
        var profile = CartRegionTimingProfile.ForRegion(region);
        byte[] raw = new byte[16 + 32768 + 8192];
        raw[0] = 0x4E; raw[1] = 0x45; raw[2] = 0x53; raw[3] = 0x1A; raw[4] = 2; raw[5] = 1;
        var ppu = new Ppu2C02Core(CartMapperFactory.Create(CartridgeLoader.Load(raw)), profile);

        int count = 0;
        while (!ppu.StepDot()) count++;
        count++;
        Assert.Equal(scanlines * dotsPerLine, count);
    }

    [Fact]
    public void Ppu2C02_EntersVBlankRaisesNmiAndClearsBit7On2002Read()
    {
        byte[] raw = new byte[16 + 32768 + 8192];
        raw[0] = 0x4E; raw[1] = 0x45; raw[2] = 0x53; raw[3] = 0x1A; raw[4] = 2; raw[5] = 1;
        var ppu = new Ppu2C02Core(CartMapperFactory.Create(CartridgeLoader.Load(raw)), CartRegionTimingProfile.Ntsc);
        ppu.WriteRegister(0x2000, 0x80); // NMI enable

        bool sawNmi = false;
        while (!ppu.StepDot())
        {
            if (ppu.TakeNmiPending())
            {
                sawNmi = true;
                byte status1 = ppu.ReadRegister(0x2002);
                byte status2 = ppu.ReadRegister(0x2002);
                Assert.True((status1 & 0x80) != 0);
                Assert.Equal(0, status2 & 0x80);
            }
        }
        Assert.True(sawNmi);
    }

    [Fact]
    public void Ppu2C02_2007ReadBuffer_DelaysNametableReadsByOneAccessExceptPalette()
    {
        byte[] raw = new byte[16 + 32768 + 8192];
        raw[0] = 0x4E; raw[1] = 0x45; raw[2] = 0x53; raw[3] = 0x1A; raw[4] = 2; raw[5] = 1;
        var ppu = new Ppu2C02Core(CartMapperFactory.Create(CartridgeLoader.Load(raw)), CartRegionTimingProfile.Ntsc);

        ppu.WriteRegister(0x2006, 0x20); ppu.WriteRegister(0x2006, 0x00);
        ppu.WriteRegister(0x2007, 0xA1); ppu.WriteRegister(0x2007, 0xB2);

        ppu.WriteRegister(0x2006, 0x20); ppu.WriteRegister(0x2006, 0x00);
        _ = ppu.ReadRegister(0x2007); // Primes buffer
        Assert.Equal(0xA1, ppu.ReadRegister(0x2007));
        Assert.Equal(0xB2, ppu.ReadRegister(0x2007));

        ppu.WriteRegister(0x2006, 0x3F); ppu.WriteRegister(0x2006, 0x01);
        ppu.WriteRegister(0x2007, 0x27);
        ppu.WriteRegister(0x2006, 0x3F); ppu.WriteRegister(0x2006, 0x01);
        Assert.Equal(0x27, ppu.ReadRegister(0x2007)); // Immediate palette read
    }

    [Fact]
    public void Ppu2C02_SetsSpriteOverflowAndSprite0HitOnOpaqueOverlap()
    {
        byte[] raw = new byte[16 + 32768 + 8192];
        raw[0] = 0x4E; raw[1] = 0x45; raw[2] = 0x53; raw[3] = 0x1A; raw[4] = 2; raw[5] = 1;
        // CHR Tile 1 plane 0 = solid non-zero pixels
        Array.Fill(raw, (byte)0xFF, 16 + 32768 + 16, 8);
        var ppu = new Ppu2C02Core(CartMapperFactory.Create(CartridgeLoader.Load(raw)), CartRegionTimingProfile.Ntsc);

        // BG palette & Tile 1 across rows 0..2 so scanline 11 overlaps solid background
        ppu.WriteRegister(0x2006, 0x3F); ppu.WriteRegister(0x2006, 0x01); ppu.WriteRegister(0x2007, 0x16);
        ppu.WriteRegister(0x2006, 0x3F); ppu.WriteRegister(0x2006, 0x11); ppu.WriteRegister(0x2007, 0x27);
        ppu.WriteRegister(0x2006, 0x20); ppu.WriteRegister(0x2006, 0x00);
        for (int i = 0; i < 96; i++) ppu.WriteRegister(0x2007, 0x01);

        // Write 9 sprites at Y=10 so sprite overflow sets; Sprite 0 at X=16,Y=10 overlaps BG tile 1
        for (int s = 0; s < 9; s++)
        {
            ppu.WriteOamByte((byte)(s * 4 + 0), 10);
            ppu.WriteOamByte((byte)(s * 4 + 1), 0x01);
            ppu.WriteOamByte((byte)(s * 4 + 2), 0x00);
            ppu.WriteOamByte((byte)(s * 4 + 3), (byte)(16 + s * 8));
        }

        ppu.WriteRegister(0x2005, 0); ppu.WriteRegister(0x2005, 0);
        ppu.WriteRegister(0x2006, 0x20); ppu.WriteRegister(0x2006, 0x00);
        ppu.WriteRegister(0x2001, 0x1E);
        while (!ppu.StepDot()) { }
        _ = ppu.ReadRegister(0x2002);

        byte capturedStatus = 0;
        while (!ppu.StepDot())
        {
            if (ppu.Scanline == 241 && ppu.Dot == 2)
                capturedStatus = ppu.ReadRegister(0x2002);
        }

        Assert.True((capturedStatus & 0x40) != 0, $"Expected Sprite-0 Hit flag, status=0x{capturedStatus:X2}");
        Assert.True((capturedStatus & 0x20) != 0, $"Expected 8-Sprite Overflow flag, status=0x{capturedStatus:X2}");
    }

    [Fact]
    public void Apu2A03_Pulse1EmitsNonSilentPcmWithZeroDroppedSamples()
    {
        var apu = new Apu2A03Core(timing: CartRegionTimingProfile.Ntsc);
        apu.WriteRegister(0x4015, 0x01); // Enable Pulse1
        apu.WriteRegister(0x4000, 0xBF); // Duty 50%, constant volume 15
        apu.WriteRegister(0x4001, 0x08); // Negate sweep off-clamp
        apu.WriteRegister(0x4002, 0xFD); // ~440 Hz period low
        apu.WriteRegister(0x4003, 0x00); // Trigger length & high period

        Assert.True((apu.ReadStatus() & 0x01) != 0);
        for (int c = 0; c < 29780; c++) apu.ClockCycle();

        Span<short> pcm = stackalloc short[2048];
        int read = apu.ReadSamples(pcm);
        Assert.True(read > 512);
        Assert.Equal(0L, apu.DroppedSampleCount);

        long peak = 0;
        for (int i = 0; i < read; i++) peak = Math.Max(peak, Math.Abs((int)pcm[i]));
        Assert.True(peak > 100, $"Expected non-silent APU PCM output, peak={peak}");
    }

    [Fact]
    public void Apu2A03_DmcChannelIssuesReadCallbackAndStallsAndRaisesIrq()
    {
        int readCount = 0;
        var apu = new Apu2A03Core(addr => { readCount++; return 0xAA; }, CartRegionTimingProfile.Ntsc);
        apu.WriteRegister(0x4010, 0x8F); // IRQ enabled, rate F
        apu.WriteRegister(0x4012, 0x00); // $C000
        apu.WriteRegister(0x4013, 0x00); // 1 byte
        apu.WriteRegister(0x4015, 0x10); // Start DMC

        int totalStall = 0;
        for (int i = 0; i < 600; i++)
        {
            apu.ClockCycle();
            totalStall += apu.TakePendingDmcStallCycles();
        }
        Assert.True(readCount >= 1);
        Assert.True(totalStall >= 4);
        Assert.True(apu.IsIrqPending);
    }

    [Theory]
    [InlineData(0x00, 0x00, false)]
    [InlineData(0x80, 0x00, true)]
    public void Ppu2C02_NmiEnableFlagControlsVBlankInterrupt(byte ctrlValue, byte scrollValue, bool expectNmi)
    {
        byte[] raw = new byte[16 + 32768 + 8192];
        raw[0] = 0x4E; raw[1] = 0x45; raw[2] = 0x53; raw[3] = 0x1A; raw[4] = 2; raw[5] = 1;
        var ppu = new Ppu2C02Core(CartMapperFactory.Create(CartridgeLoader.Load(raw)), CartRegionTimingProfile.Ntsc);
        ppu.WriteRegister(0x2005, scrollValue);
        ppu.WriteRegister(0x2000, ctrlValue);
        bool sawNmi = false;
        while (!ppu.StepDot()) { if (ppu.TakeNmiPending()) sawNmi = true; }
        Assert.Equal(expectNmi, sawNmi);
    }

    [Fact]
    public void CartMachineHost_QuickSaveRoundTripsAndRejectsTamperedPayload()
    {
        using CartMachineHost host = CreateBlankHost(CartVideoRegionStandard.Ntsc);
        host.Cpu.A = 0x6B;
        host.Cpu.X = 0x2C;
        host.Bus.Write(0x0055, 0x9E);
        host.StepFrame();

        byte[] snapshot = host.CaptureQuickSave();
        host.Cpu.A = 0x00;
        host.Bus.Write(0x0055, 0x00);
        Assert.True(host.TryRestoreQuickSave(snapshot, out string diag), diag);
        Assert.Equal(0x6B, host.Cpu.A);
        Assert.Equal(0x2C, host.Cpu.X);
        Assert.Equal(0x9E, host.Bus.Read(0x0055));

        byte[] tampered = (byte[])snapshot.Clone();
        tampered[20] ^= 0xFF;
        Assert.False(host.TryRestoreQuickSave(tampered, out _));
    }
}
