// @module: reCart.Decomp.CartDecompPackageResolver
// @purpose: Manifest loading, BYO ROM/FDS + upstream research path discovery, and toolchain/schema resolution helpers.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, reCart.Contracts, reEngine.Core.Decomp, reEngine.Core.Manifests
// @exports: CartDecompPackageResolver

using reCart.Contracts;
using reEngine.Core.Decomp;
using reEngine.Core.Manifests;

namespace reCart.Decomp;

internal static class CartDecompPackageResolver
{
    internal static (DecompPackageManifest Manifest, bool IsResearchRepo) LoadManifest(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        string full = Path.GetFullPath(packagePath);

        string? researchPath = CartMediaIntakeInspector.ResolveResearchManifestPath(full);
        if (Directory.Exists(full))
        {
            string modDecomp = Path.Combine(full, "module.decomp.json");
            if (File.Exists(modDecomp)) return (DecompPipelineCoordinator.ParseManifest(modDecomp), false);

            string? altDecomp = Directory.EnumerateFiles(full, "*.decomp.json", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (altDecomp is not null) return (DecompPipelineCoordinator.ParseManifest(altDecomp), false);

            if (researchPath is not null) return (BuildManifestFromResearch(researchPath, full), true);
            throw new FileNotFoundException($"No module.decomp.json or research.manifest.json found in '{full}'.", modDecomp);
        }

        if (researchPath is not null)
        {
            string repoRoot = Path.GetFileName(Path.GetDirectoryName(researchPath) ?? string.Empty).Equals("Dev", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(Path.GetDirectoryName(researchPath)!)!
                : Path.GetDirectoryName(researchPath)!;
            return (BuildManifestFromResearch(researchPath, repoRoot), true);
        }

        return (DecompPipelineCoordinator.ParseManifest(full), false);
    }

    internal static bool TryResolveMediaOrResearch(
        DecompRunOptions options,
        DecompPackageManifest manifest,
        out string? mediaPath,
        out string? researchRoot,
        out string? error)
    {
        mediaPath = null;
        researchRoot = null;
        error = null;

        string? explicitMedia = GetArg(options, "romPath") ?? GetArg(options, "discPath") ?? GetArg(options, "exePath") ??
                                GetArg(options, "romDir") ?? GetArg(options, "gamedataDir");
        if (!string.IsNullOrWhiteSpace(explicitMedia))
        {
            string full = Path.GetFullPath(explicitMedia);
            if (File.Exists(full)) mediaPath = full;
            else if (Directory.Exists(full)) mediaPath = FindFirstCartMedia(full);
            if (mediaPath is null)
            {
                error = $"Requested BYO NES/FDS media path not found or contains no .nes/.fds files: '{full}'.";
                return false;
            }
        }
        else if (!string.IsNullOrWhiteSpace(options.DataDirectory) && Directory.Exists(options.DataDirectory))
        {
            string fullData = Path.GetFullPath(options.DataDirectory);
            foreach (string sub in new[] { Path.Combine(fullData, "data", "rom"), Path.Combine(fullData, "rom"), Path.Combine(fullData, "gamedata"), fullData })
            {
                if (!Directory.Exists(sub)) continue;
                mediaPath = FindFirstCartMedia(sub);
                if (mediaPath is not null) break;
            }
        }

        string? candResearch = GetArg(options, "upstreamResearchPath") ?? manifest.UpstreamResearchPath;
        if (!string.IsNullOrWhiteSpace(candResearch))
        {
            string fullRes = Path.GetFullPath(candResearch);
            if (CartMediaIntakeInspector.ResolveResearchManifestPath(fullRes) is not null)
            {
                researchRoot = fullRes;
            }
            else if (GetArg(options, "upstreamResearchPath") is not null)
            {
                error = $"Requested upstreamResearchPath missing research.manifest.json: '{fullRes}'.";
                return false;
            }
        }

        return true;
    }

    internal static string? ResolveOptionalSchemaFile(string? explicitPath, string? researchRoot, string outDir, string pkgPath, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            string full = Path.GetFullPath(explicitPath);
            return File.Exists(full) ? full : null;
        }
        var probes = new List<string>();
        if (!string.IsNullOrWhiteSpace(researchRoot))
        {
            probes.Add(Path.Combine(researchRoot, "Dev", "schemas", fileName));
            probes.Add(Path.Combine(researchRoot, "schemas", fileName));
            probes.Add(Path.Combine(researchRoot, fileName));
        }
        probes.Add(Path.Combine(outDir, fileName));
        if (Directory.Exists(pkgPath))
        {
            probes.Add(Path.Combine(pkgPath, fileName));
            probes.Add(Path.Combine(pkgPath, "schemas", fileName));
        }
        return probes.FirstOrDefault(File.Exists);
    }

    internal static OfflineToolchainStatus LocateToolchain(DecompRunOptions options, DecompPackageManifest manifest)
    {
        string baseDir = !string.IsNullOrWhiteSpace(options.EngineDirectoryOverride)
            ? options.EngineDirectoryOverride
            : AppContext.BaseDirectory;
        ReEngineInstallProfile profile = string.Equals(GetArg(options, "profile"), "Lightweight", StringComparison.OrdinalIgnoreCase)
            ? ReEngineInstallProfile.Lightweight
            : ReEngineInstallProfile.Full;
        return OfflineToolchainLocator.Locate(baseDir, profile, options.ToolchainDirectory, manifest.ToolchainRequirements);
    }

    internal static bool HasCOrHeaderFiles(string dir) =>
        Directory.Exists(dir) &&
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Any(p => p.EndsWith(".c", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".h", StringComparison.OrdinalIgnoreCase));

    internal static string? GetArg(DecompRunOptions options, string key)
    {
        if (options.PlatformArguments is null) return null;
        if (options.PlatformArguments.TryGetValue(key, out string? v)) return v;
        return options.PlatformArguments.FirstOrDefault(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
    }

    private static DecompPackageManifest BuildManifestFromResearch(string manifestPath, string repoRoot)
    {
        ResearchManifestValidationResult res = D4BuildReportValidator.ValidateResearchManifestFile(manifestPath, expectedPlatformId: null);
        return new DecompPackageManifest
        {
            ModuleId = string.IsNullOrWhiteSpace(res.GameId) ? "nes-research" : res.GameId,
            Title = res.GameId,
            PlatformId = res.Platform,
            EngineId = CartEngineConstants.EngineId,
            EngineVersion = CartEngineConstants.Version,
            CompatibleEngineTrack = CartEngineConstants.CompatibleEngineTrack,
            UpstreamResearchPath = repoRoot
        };
    }

    private static string? FindFirstCartMedia(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".nes", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".fds", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
}
