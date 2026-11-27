// @module: reCart.Player.ReCartSessionHost.MediaAndInput
// @purpose: Clean-room .recomp/iNES package loader, local/suite SHA-256 ROM discovery, 4-port Standard 33 NES input mapper, and QuickSave helpers.
// @depends-on: System, System.IO, System.IO.Compression, System.Runtime.InteropServices, System.Security.Cryptography, Foundation.Core, reCart.Contracts, reEngine.Core.Decomp, reEngine.Core.Resolution, reEngine.Host.Contracts
// @exports: ReCartSessionHost (partial)

using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Foundation.Core;
using Foundation.Retro;
using reCart.Compiler;
using reCart.Contracts;
using reEngine.Core.Decomp;
using reEngine.Core.Resolution;
using reEngine.Host.Contracts;

namespace reCart.Player;

internal sealed partial class ReCartSessionHost
{
    private static readonly string[] ProhibitedArchiveExtensions =
        [".generated.dll", ".g.cs", ".nes", ".fds", ".prg", ".chr", ".bin", ".rom", ".sav", ".dll", ".exe"];

    internal sealed record LoadedCartSessionInput(
        CartPackageManifest? Manifest,
        string? RecompPackagePath,
        string? RomFilePath,
        byte[]? RomBytes,
        string? RomSha256Hex,
        CartRomHeader? RomHeader,
        string ModuleId,
        string ModuleTitle,
        string MediaLabel,
        string PreloadRomDirectory);

    internal static LoadedCartSessionInput LoadCartInputs(PlayerCliOptions options)
    {
        CartPackageManifest? manifest = null;
        string? recompPath = null;
        string? romPath = options.RomPath;
        string dataRomDir = Path.Combine(options.Environment.DataDirectory, "rom");
        string preloadDir = dataRomDir;
        string? moduleRootDir = null;

        if (!string.IsNullOrWhiteSpace(options.OpenPath))
        {
            string fullOpen = Path.GetFullPath(options.OpenPath);
            if (!File.Exists(fullOpen) && !Directory.Exists(fullOpen))
                throw new FileNotFoundException($"Target package or ROM not found: '{fullOpen}'.", fullOpen);

            if (CartMediaArchiveLoader.IsDirectRomExtension(fullOpen) ||
                fullOpen.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
                (fullOpen.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !CartMediaArchiveLoader.ArchiveContainsManifest(fullOpen)))
            {
                romPath ??= fullOpen;
            }
            else if (Directory.Exists(fullOpen))
            {
                string mfPath = Path.Combine(fullOpen, "module.recomp.json");
                if (!File.Exists(mfPath)) throw new InvalidDataException($"Directory '{fullOpen}' missing 'module.recomp.json'.");
                manifest = ReadAndValidateManifestJson(File.ReadAllText(mfPath));
                moduleRootDir = fullOpen;
                recompPath = fullOpen;
            }
            else if (fullOpen.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                manifest = ReadAndValidateManifestJson(File.ReadAllText(fullOpen));
                moduleRootDir = ResolveModuleRootFromPackageFile(fullOpen);
                recompPath = fullOpen;
            }
            else
            {
                manifest = ReadAndValidateRecompZip(fullOpen);
                moduleRootDir = ResolveModuleRootFromPackageFile(fullOpen);
                recompPath = fullOpen;
            }

            if (manifest is not null && !string.IsNullOrWhiteSpace(moduleRootDir) && !Directory.Exists(dataRomDir))
            {
                string modulePreload = Path.GetFullPath(Path.Combine(moduleRootDir, manifest.PreloadRomDir));
                if (Directory.Exists(modulePreload)) preloadDir = modulePreload;
            }
        }

        if (string.IsNullOrWhiteSpace(romPath) && !options.DemoCart)
        {
            romPath = DiscoverRomCandidate(options, manifest, moduleRootDir, dataRomDir);
        }

        if (options.DemoCart && string.IsNullOrWhiteSpace(options.OpenPath) && string.IsNullOrWhiteSpace(romPath))
        {
            SyntheticCleanRoomCartPackage pkg = SyntheticCleanRoomCartBuilder.BuildPackage();
            string demoDir = AppEnvironment.AssertPathContained(Path.Combine(options.Environment.DataDirectory, "demo"), options.Environment.DataDirectory);
            Directory.CreateDirectory(demoDir);
            string demoRecompPath = Path.Combine(demoDir, $"{pkg.Manifest.ModuleId}.recomp");
            EnsureSyntheticDemoRecomp(pkg, demoRecompPath);
            CartRomHeader demoHeader = CartRomHeaderParser.Parse(pkg.RomBytes);
            return new LoadedCartSessionInput(
                pkg.Manifest, demoRecompPath, RomFilePath: null, pkg.RomBytes, pkg.RomSha256Hex,
                demoHeader, pkg.Manifest.ModuleId, pkg.Manifest.DisplayName,
                "Ironframe Clean-Room .recomp (AOT)", preloadDir);
        }

        byte[]? romBytes = null;
        string? romSha = null;
        CartRomHeader? header = null;
        if (!string.IsNullOrWhiteSpace(romPath))
        {
            (romBytes, romSha, header) = LoadAndVerifyRomFile(romPath, manifest);
        }

        string moduleId = manifest?.ModuleId
            ?? (options.DemoCart ? "ironframe-outerfortress-cart-01"
                : (!string.IsNullOrWhiteSpace(romPath) ? SanitizeSlug(Path.GetFileNameWithoutExtension(romPath)) : CartEngineConstants.AppId));
        string moduleTitle = manifest?.DisplayName
            ?? (options.DemoCart ? "Ironframe: Outer Fortress"
                : (!string.IsNullOrWhiteSpace(romPath) ? Path.GetFileNameWithoutExtension(romPath) : $"{CartEngineConstants.DisplayName} v{CartEngineConstants.Version}"));
        string mediaLabel = !string.IsNullOrWhiteSpace(romPath)
            ? Path.GetFileName(romPath)
            : (options.DemoCart ? "Built-in Clean-Room Demo Cart" : "BYO .nes / .fds (data\\rom\\)");

        return new LoadedCartSessionInput(manifest, recompPath, romPath, romBytes, romSha, header, moduleId, moduleTitle, mediaLabel, preloadDir);
    }

    private static string? DiscoverRomCandidate(
        PlayerCliOptions options,
        CartPackageManifest? manifest,
        string? moduleRootDir,
        string dataRomDir)
    {
        bool hasChecksums = manifest is { RequiredDiscChecksums.Count: > 0 };
        if (Directory.Exists(dataRomDir))
        {
            List<string> dataCandidates = EnumerateRomCandidates(dataRomDir);
            if (dataCandidates.Count > 0)
            {
                if (hasChecksums)
                {
                    string? verified = dataCandidates.FirstOrDefault(p => MatchesAnyDeclaredChecksum(manifest!, p));
                    if (verified is not null) return verified;
                }
                if (!options.Verify || !hasChecksums)
                    return dataCandidates[0];
            }
        }

        if (manifest is not null && !string.IsNullOrWhiteSpace(moduleRootDir) && !Path.IsPathRooted(manifest.PreloadRomDir))
        {
            string modPreload = Path.GetFullPath(Path.Combine(moduleRootDir, manifest.PreloadRomDir));
            if (!string.Equals(modPreload, Path.GetFullPath(dataRomDir), StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(modPreload))
            {
                List<string> modCandidates = EnumerateRomCandidates(modPreload);
                string? verified = hasChecksums
                    ? modCandidates.FirstOrDefault(p => MatchesAnyDeclaredChecksum(manifest, p))
                    : modCandidates.FirstOrDefault();
                if (verified is not null) return verified;
            }
        }

        if (manifest is null || manifest.RequiredDiscChecksums.Count == 0 || IsIsolatedTestEnv()) return null;

        string? boundary = options.Environment.Sandbox.IsSandboxActive ? options.Environment.Sandbox.SandboxRoot : null;
        string? suiteRoot = (!string.IsNullOrWhiteSpace(moduleRootDir) &&
            Directory.GetParent(moduleRootDir) is { } gamesDir &&
            gamesDir.Name.Equals("games", StringComparison.OrdinalIgnoreCase))
            ? gamesDir.Parent?.FullName
            : SuiteMediaTreeWalker.DeriveSuiteRootDirectory(AppContext.BaseDirectory, options.Environment.DataDirectory, boundary);
        if (string.IsNullOrWhiteSpace(suiteRoot)) return null;
        if (boundary is not null)
        {
            try { suiteRoot = AppEnvironment.AssertPathContained(Path.GetFullPath(suiteRoot), boundary); }
            catch (InvalidOperationException) { return null; }
        }

        foreach (DiscChecksumEntry entry in manifest.RequiredDiscChecksums.Where(e => !string.IsNullOrWhiteSpace(e.ExpectedSha256)))
        {
            SuiteMediaDiscoveryResult suiteHit = SuiteMediaTreeWalker.Discover(
                moduleRootDir, suiteRoot, manifest.ModuleId, entry.ExpectedSha256, manifest.PreloadRomDir, boundary);
            SuiteMediaCandidate? exact = suiteHit.Candidates.FirstOrDefault(c => c.MatchesExpectedSha256);
            if (exact is not null) return exact.FilePath;
        }
        return null;
    }

    private static List<string> EnumerateRomCandidates(string dir) =>
        CartMediaArchiveLoader.EnumerateRomCandidates(dir);

    private static (byte[] RomBytes, string Sha256Hex, CartRomHeader Header) LoadAndVerifyRomFile(string romPath, CartPackageManifest? manifest)
    {
        if (!File.Exists(romPath)) throw new FileNotFoundException($"ROM image not found: '{romPath}'.", romPath);
        byte[] rawBytes = CartMediaArchiveLoader.ReadRomBytesFromFileOrArchive(romPath);
        string romSha = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();
        VerifyChecksumsIfDeclared(manifest, romSha);
        byte[] romBytes = CartPrgHeaderSynthesizer.SynthesizeIfHeaderless(
            rawBytes,
            manifest?.MapperId ?? 2,
            manifest?.SubmapperId ?? 0,
            CartMirroringMode.Vertical,
            manifest?.VideoRegionStandard ?? CartVideoRegionStandard.Ntsc);
        return (romBytes, romSha, CartRomHeaderParser.Parse(romBytes));
    }

    private static string? ResolveModuleRootFromPackageFile(string fullOpenFile)
    {
        string? dir = Path.GetDirectoryName(fullOpenFile);
        if (string.IsNullOrEmpty(dir)) return null;
        return string.Equals(Path.GetFileName(dir), "app", StringComparison.OrdinalIgnoreCase)
            ? (Directory.GetParent(dir)?.FullName ?? dir)
            : dir;
    }

    private static void EnsureSyntheticDemoRecomp(SyntheticCleanRoomCartPackage pkg, string demoRecompPath)
    {
        bool needsCreate = !File.Exists(demoRecompPath);
        if (!needsCreate)
        {
            try
            {
                CartPackageManifest existing = ReadAndValidateRecompZip(demoRecompPath);
                needsCreate = !existing.RequiredDiscChecksums.Any(c =>
                    string.Equals(c.ExpectedSha256, pkg.RomSha256Hex, StringComparison.OrdinalIgnoreCase));
            }
            catch { needsCreate = true; }
        }
        if (needsCreate) CleanRoomRecompPackager.PackageCleanRoomRecomp(pkg.RomBytes, pkg.Manifest, demoRecompPath);
    }

    private static bool MatchesAnyDeclaredChecksum(CartPackageManifest manifest, string filePath) =>
        CartMediaArchiveLoader.MatchesAnyDeclaredChecksum(manifest, filePath);

    private static bool IsIsolatedTestEnv() =>
        string.Equals(Environment.GetEnvironmentVariable("APP_ISOLATED_TEST"), "1", StringComparison.Ordinal) ||
        string.Equals(Environment.GetEnvironmentVariable("APP_ISOLATED_TEST"), "true", StringComparison.OrdinalIgnoreCase);

    internal static CartPackageManifest ReadAndValidateRecompZip(string zipPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        string sentinelRoot = Path.Combine(Path.GetTempPath(), "recart_zip_check");
        ZipArchiveEntry? manifestEntry = null;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string normalized = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            AppEnvironment.AssertPathContained(Path.Combine(sentinelRoot, normalized), sentinelRoot);
            if (ProhibitedArchiveExtensions.Any(ext => entry.FullName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Clean-room violation in '{Path.GetFileName(zipPath)}': forbidden entry '{entry.FullName}'.");
            if (string.Equals(entry.FullName, "module.recomp.json", StringComparison.OrdinalIgnoreCase))
                manifestEntry = entry;
        }
        if (manifestEntry is null)
            throw new InvalidDataException($"Package '{Path.GetFileName(zipPath)}' missing required 'module.recomp.json'.");
        using var reader = new StreamReader(manifestEntry.Open());
        return ReadAndValidateManifestJson(reader.ReadToEnd());
    }

    private static CartPackageManifest ReadAndValidateManifestJson(string json)
    {
        CartPackageManifest manifest = CartPackageManifest.Deserialize(json);
        manifest.Validate();
        if (!EngineAliasCatalog.AreEquivalent(manifest.EngineId, CartEngineConstants.EngineId))
            throw new InvalidDataException($"Package '{manifest.ModuleId}' targets incompatible engine '{manifest.EngineId}'.");
        return manifest;
    }

    private static void VerifyChecksumsIfDeclared(CartPackageManifest? manifest, string actualSha256Hex)
    {
        if (manifest is null || manifest.RequiredDiscChecksums.Count == 0) return;
        bool hasRequired = manifest.RequiredDiscChecksums.Any(c => c.IsRequired);
        if (!hasRequired) return;
        bool matched = manifest.RequiredDiscChecksums.Any(c =>
            string.Equals(c.ExpectedSha256, actualSha256Hex, StringComparison.OrdinalIgnoreCase));
        if (!matched)
            throw new InvalidDataException($"BYO ROM SHA-256 '{actualSha256Hex}' did not match required checksum for '{manifest.ModuleId}'.");
    }

    internal uint[] MapHostInputToNesPorts(HostInputFrame input, long frameNumber)
    {
        var ports = new uint[4];
        FillNesPorts(input, frameNumber, ports);
        return ports;
    }

    internal void FillNesPorts(HostInputFrame input, long frameNumber, Span<uint> ports)
    {
        ports[..4].Clear();
        bool turboPulseOn = _turboFramesInterval <= 0 || ((frameNumber / _turboFramesInterval) % 2 == 0);
        ports[0] = MapButtonsToNesMask(input.DigitalButtons, input.LeftStickX, input.LeftStickY, turboPulseOn);
        if (input.PadPorts.Count > 1 && input.PadPorts[1].IsConnected)
            ports[1] = MapButtonsToNesMask(input.PadPorts[1].Buttons, input.PadPorts[1].LeftX, input.PadPorts[1].LeftY, turboPulseOn);
        if (_isFourScoreEnabled)
        {
            for (int p = 2; p < Math.Min(4, input.PadPorts.Count); p++)
                if (input.PadPorts[p].IsConnected)
                    ports[p] = MapButtonsToNesMask(input.PadPorts[p].Buttons, input.PadPorts[p].LeftX, input.PadPorts[p].LeftY, turboPulseOn);
        }
    }

    private uint MapButtonsToNesMask(HostGamepadButtons buttons, float stickX, float stickY, bool turboPulseOn)
    {
        const float stickThreshold = 0.45f;
        bool allowDpad = !string.Equals(_controllerDpadMode, "Stick Only", StringComparison.OrdinalIgnoreCase);
        bool allowStick = !string.Equals(_controllerDpadMode, "D-Pad Only", StringComparison.OrdinalIgnoreCase);

        uint mask = 0u;
        if (buttons.HasFlag(HostGamepadButtons.South) || (buttons.HasFlag(HostGamepadButtons.North) && turboPulseOn)) mask |= 0x01u;
        if (buttons.HasFlag(HostGamepadButtons.East) || buttons.HasFlag(HostGamepadButtons.West) ||
            (buttons.HasFlag(HostGamepadButtons.RightShoulder) && turboPulseOn)) mask |= 0x02u;
        if (buttons.HasFlag(HostGamepadButtons.Select)) mask |= 0x04u;
        if (buttons.HasFlag(HostGamepadButtons.Start)) mask |= 0x08u;

        if ((allowDpad && buttons.HasFlag(HostGamepadButtons.DPadUp)) || (allowStick && stickY <= -stickThreshold)) mask |= 0x10u;
        if ((allowDpad && buttons.HasFlag(HostGamepadButtons.DPadDown)) || (allowStick && stickY >= stickThreshold)) mask |= 0x20u;
        if ((allowDpad && buttons.HasFlag(HostGamepadButtons.DPadLeft)) || (allowStick && stickX <= -stickThreshold)) mask |= 0x40u;
        if ((allowDpad && buttons.HasFlag(HostGamepadButtons.DPadRight)) || (allowStick && stickX >= stickThreshold)) mask |= 0x80u;
        return mask;
    }

    private static uint PackRgba(byte r, byte g, byte b) =>
        BitConverter.IsLittleEndian
            ? ((uint)r | ((uint)g << 8) | ((uint)b << 16) | 0xFF000000u)
            : (((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | 0xFFu);

    private static void RenderIdleBackdropFrame(Span<byte> rgba, int width, int height)
    {
        int totalPixels = Math.Min(rgba.Length / 4, checked(width * height));
        MemoryMarshal.Cast<byte, uint>(rgba)[..totalPixels].Fill(PackRgba(14, 17, 23));
    }

    private void RenderDemoFrame(uint port0Mask, bool fastForward, Span<byte> rgba, int width, int height)
    {
        int step = fastForward ? 4 : 2;
        if ((port0Mask & 0x80u) != 0) _demoX = Math.Min(width - 24, _demoX + step);
        if ((port0Mask & 0x40u) != 0) _demoX = Math.Max(8, _demoX - step);
        if ((port0Mask & 0x20u) != 0) _demoY = Math.Min(height - 24, _demoY + step);
        if ((port0Mask & 0x10u) != 0) _demoY = Math.Max(20, _demoY - step);
        bool actionPressed = (port0Mask & 0x03u) != 0;

        uint spriteColor = actionPressed ? PackRgba(255, 230, 140) : PackRgba(245, 158, 11);
        uint borderColor = PackRgba(245, 158, 11);
        uint gridColor = PackRgba(30, 42, 64);
        uint bgColor = PackRgba(14, 20, 32);
        Span<uint> pixels = MemoryMarshal.Cast<byte, uint>(rgba);

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width;
            bool borderY = y < 4 || y >= height - 4;
            bool gridY = (y & 15) == 0;
            bool inSpriteY = y >= _demoY && y < _demoY + 16;

            for (int x = 0; x < width; x++)
            {
                if (inSpriteY && x >= _demoX && x < _demoX + 16)
                    pixels[rowOffset + x] = spriteColor;
                else if (borderY || x < 4 || x >= width - 4)
                    pixels[rowOffset + x] = borderColor;
                else
                    pixels[rowOffset + x] = (gridY || (x & 15) == 0) ? gridColor : bgColor;
            }
        }
    }

    private int SynthesizeDemoAudio(Span<short> pcm)
    {
        int count = Math.Min(pcm.Length, 1470) & ~1;
        if (count <= 0) return 0;
        if (!_demoInputActive || _demoChimeSamplesRemaining <= 0)
        {
            pcm[..count].Clear();
            return count;
        }

        double freq = 440.0 + (_demoX % 120);
        double step = (2.0 * Math.PI * freq) / CartEngineConstants.DefaultAudioSampleRateHz;
        int active = Math.Min(count, _demoChimeSamplesRemaining);
        for (int i = 0; i + 1 < active; i += 2)
        {
            double env = (double)_demoChimeSamplesRemaining / 4410.0;
            short sample = (short)(Math.Sin(_audioPhase) * 650.0 * env);
            pcm[i] = sample;
            pcm[i + 1] = sample;
            _audioPhase += step;
            if (_audioPhase >= 2.0 * Math.PI) _audioPhase -= 2.0 * Math.PI;
            _demoChimeSamplesRemaining = Math.Max(0, _demoChimeSamplesRemaining - 2);
        }
        if (active < count) pcm[active..count].Clear();
        return count;
    }

    private static string SanitizeSlug(string raw)
    {
        var chars = raw.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        string s = new string(chars).Trim('-');
        return s.Length > 0 ? s : CartEngineConstants.AppId;
    }
}
