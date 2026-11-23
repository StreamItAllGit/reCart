// @module: reCart.Compiler.ColdAotCacheCompiler
// @purpose: Out-of-process cold AOT compiler turning clean-room .recomp C# into <dataDir>\cache\<moduleId>\<key>\<moduleId>.Generated.dll.
// @depends-on: System, System.Diagnostics, System.IO, System.IO.Compression, System.Reflection, System.Runtime.Loader, System.Security, System.Text, System.Threading, System.Threading.Tasks, Foundation.Core, Foundation.Retro, reCart.Contracts, reEngine.Core
// @exports: ColdAotCacheCompiler

using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security;
using System.Text;
using Foundation.Core;
using Foundation.Retro;
using reCart.Contracts;
using reEngine.Core.Aot;
using reEngine.Core.Manifests;

namespace reCart.Compiler;

/// <summary>
/// Implements <see cref="ICartCompilerPipeline"/> to validate clean-room <c>.recomp</c> packages, compile
/// lifted C# out-of-process into <c>&lt;dataDir&gt;\cache\</c>, index via <see cref="AotCacheManager"/>, and load modules.
/// </summary>
public sealed class ColdAotCacheCompiler : ICartCompilerPipeline
{
    private readonly Lifter6502ToCSharp _lifter = new();
    private readonly string _dotnetExecutable;
    private readonly TimeSpan _buildTimeout;

    /// <summary>Initializes a new <see cref="ColdAotCacheCompiler"/> using ambient <c>dotnet</c> CLI.</summary>
    public ColdAotCacheCompiler()
        : this("dotnet", TimeSpan.FromMinutes(3))
    {
    }

    internal ColdAotCacheCompiler(string dotnetExecutable, TimeSpan buildTimeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dotnetExecutable);
        _dotnetExecutable = dotnetExecutable;
        _buildTimeout = buildTimeout;
    }

    /// <inheritdoc />
    public string PackageCleanRoomRecomp(ReadOnlySpan<byte> romBytes, CartPackageManifest manifest, string outputRecompZipPath) =>
        CleanRoomRecompPackager.PackageCleanRoomRecomp(romBytes, manifest, outputRecompZipPath, _lifter);

    /// <inheritdoc />
    public Task<ICartRecompGameModule> CompileAndLoadAsync(
        string recompPackagePath,
        string romSha256Hex,
        string dataDirectory,
        Action<string>? progressCallback = null,
        CancellationToken ct = default) =>
        CompileAndLoadAsync(recompPackagePath, romSha256Hex, dataDirectory, ReadOnlyMemory<byte>.Empty, progressCallback, ct);

    /// <summary>
    /// Validates a 3-member lifted or 2-member clean-room recipe <c>.recomp</c> package, lifts locally from
    /// <paramref name="romBytes"/> when packaging a recipe archive, compiles out-of-process into <c>&lt;dataDir&gt;\cache\</c>, and loads the module.
    /// </summary>
    public async Task<ICartRecompGameModule> CompileAndLoadAsync(
        string recompPackagePath,
        string romSha256Hex,
        string dataDirectory,
        ReadOnlyMemory<byte> romBytes,
        Action<string>? progressCallback = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recompPackagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(romSha256Hex);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ct.ThrowIfCancellationRequested();

        string fullRecomp = Path.GetFullPath(recompPackagePath);
        string fullDataDir = Path.GetFullPath(dataDirectory);
        string normalizedSha = romSha256Hex.Trim().ToLowerInvariant();

        CleanRoomRecompPackager.AssertValidCleanRoomArchive(fullRecomp);
        _ = RecompPackageReader.Load(fullRecomp);

        CartPackageManifest manifest = ReadCartManifest(fullRecomp);
        string resolvedTypeName = string.IsNullOrWhiteSpace(manifest.ModuleTypeName) ? "reCart.Lifted.CartLiftedModule" : manifest.ModuleTypeName.Trim();
        string engineId = string.IsNullOrWhiteSpace(manifest.EngineId) ? CartEngineConstants.EngineId : manifest.EngineId.Trim();
        string modVersion = string.IsNullOrWhiteSpace(manifest.Version) ? CartEngineConstants.Version : manifest.Version.Trim();
        var cacheKey = new AotCacheKey(manifest.ModuleId, normalizedSha, engineId, CartEngineConstants.Version, modVersion);

        var cacheManager = new AotCacheManager(fullDataDir);
        AotCacheEntry? hit = await cacheManager.TryGetEntryAsync(cacheKey, ct).ConfigureAwait(false);
        if (hit is not null && File.Exists(hit.GeneratedAssemblyPath))
        {
            progressCallback?.Invoke($"[AOT] Cache hit: {hit.GeneratedAssemblyPath}");
            return LoadCompiledModule(hit.GeneratedAssemblyPath, resolvedTypeName);
        }

        progressCallback?.Invoke($"[AOT] Compiling '{manifest.ModuleId}' v{modVersion} out-of-process...");
        string cacheRoot = Path.Combine(fullDataDir, AotCacheLayoutHelper.CacheRootFolderName);
        string stagingDir = Path.Combine(cacheRoot, ".staging", $"{manifest.ModuleId}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDir);

        try
        {
            string sourcePath = CleanRoomRecompPackager.IsRecipeArchive(fullRecomp)
                ? MaterializeSourceFromRecipe(fullRecomp, manifest, romBytes, stagingDir)
                : ExtractLiftedSource(fullRecomp, stagingDir);
            string outDir = Path.Combine(stagingDir, "out");
            Directory.CreateDirectory(outDir);
            string csprojPath = WriteStagingProject(stagingDir, manifest.ModuleId, Path.GetFileName(sourcePath));

            await RunOutOfProcessBuildAsync(csprojPath, outDir, progressCallback, ct).ConfigureAwait(false);

            string builtDll = Path.Combine(outDir, $"{manifest.ModuleId}.Generated.dll");
            if (!File.Exists(builtDll))
            {
                throw new FileNotFoundException($"AOT build succeeded but did not produce '{builtDll}'.", builtDll);
            }

            AotCompileResult stored = await cacheManager.StoreCompiledAssemblyAsync(cacheKey, builtDll, ct).ConfigureAwait(false);
            if (!stored.Success || stored.Entry is null)
            {
                throw new InvalidOperationException($"Failed to store AOT assembly into cache: {stored.ErrorCode} {stored.ErrorMessage}");
            }

            progressCallback?.Invoke($"[AOT] Ready: {stored.Entry.GeneratedAssemblyPath}");
            return LoadCompiledModule(stored.Entry.GeneratedAssemblyPath, resolvedTypeName);
        }
        finally
        {
            try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true); }
            catch { /* best-effort staging cleanup */ }
        }
    }

    private static CartPackageManifest ReadCartManifest(string zipPath)
    {
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry entry = zip.GetEntry(CleanRoomRecompPackager.ManifestEntryName)
            ?? throw new InvalidDataException($"Archive missing '{CleanRoomRecompPackager.ManifestEntryName}'.");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        CartPackageManifest manifest = CartPackageManifest.Deserialize(reader.ReadToEnd());
        manifest.Validate();
        return manifest;
    }

    private static string ExtractLiftedSource(string zipPath, string stagingDir)
    {
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry entry = zip.GetEntry(CleanRoomRecompPackager.LiftedSourceEntryName)
            ?? throw new InvalidDataException($"Archive missing '{CleanRoomRecompPackager.LiftedSourceEntryName}'.");
        string destPath = AppEnvironment.AssertPathContained(Path.Combine(stagingDir, "CartLiftedModule.cs"), stagingDir);
        using Stream src = entry.Open();
        using FileStream dst = File.Create(destPath);
        src.CopyTo(dst);
        return destPath;
    }

    private static string MaterializeSourceFromRecipe(
        string zipPath,
        CartPackageManifest manifest,
        ReadOnlyMemory<byte> romBytes,
        string stagingDir)
    {
        if (romBytes.IsEmpty)
        {
            throw new InvalidOperationException(
                $"Clean-room recipe archive '{Path.GetFileName(zipPath)}' requires user BYO ROM bytes to lift and compile local AOT module into <dataDir>\\cache\\.");
        }

        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry entry = zip.GetEntry(CleanRoomRecompPackager.AotRecipeEntryName)
            ?? throw new InvalidDataException($"Archive missing '{CleanRoomRecompPackager.AotRecipeEntryName}'.");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        CartAotRecipeSpec recipe = CartAotRecipeSpec.Deserialize(reader.ReadToEnd());
        recipe.Validate();

        byte[] inesRom = CartPrgHeaderSynthesizer.SynthesizeIfHeaderless(
            romBytes.ToArray(),
            manifest.MapperId,
            manifest.SubmapperId,
            CartMirroringMode.Vertical,
            manifest.VideoRegionStandard);
        CartRomHeader header = CartRomHeaderParser.Parse(inesRom);
        uint fixedBank = header.PrgRomSizeBytes > 16384 ? (uint)(header.PrgRomSizeBytes / 16384 - 1) : 0;
        IReadOnlyList<(uint PrgBank, ushort Address)> seeds = recipe.ToSeedTuples(fixedBank);
        HashSet<ushort>? inlineDispatchers = recipe.JumpTables.Count > 0 ? [] : null;
        if (inlineDispatchers is not null)
        {
            foreach (CartAotJumpTableHint jt in recipe.JumpTables)
            {
                inlineDispatchers.Add(CartAotRecipeSpec.ParseHexAddress(jt.DispatcherAddress));
            }
        }

        LiftedProgramResult lifted = new Lifter6502ToCSharp().Lift(inesRom, manifest, seeds, inlineDispatchers);
        string destPath = AppEnvironment.AssertPathContained(Path.Combine(stagingDir, "CartLiftedModule.cs"), stagingDir);
        File.WriteAllText(destPath, lifted.CSharpSource, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return destPath;
    }

    private static string WriteStagingProject(string stagingDir, string moduleId, string sourceFileName)
    {
        string contractsDll = ResolveHostAssembly(typeof(ICartRecompGameModule), "reCart.Contracts.dll");
        string compilerDll = ResolveHostAssembly(typeof(UnliftedPcTrapException), "reCart.Compiler.dll");
        string retroDll = ResolveHostAssembly(typeof(IRecompGameModule), "Foundation.Retro.dll");
        string coreDll = ResolveHostAssembly(typeof(AotCacheManager), "reEngine.Core.dll");
        string csprojPath = Path.Combine(stagingDir, "CartLiftedModule.csproj");

        string xml = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <AssemblyName>{SecurityElement.Escape(moduleId)}.Generated</AssemblyName>
                <LangVersion>latest</LangVersion>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <Deterministic>true</Deterministic>
                <ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>
                <DebugType>none</DebugType>
                <DebugSymbols>false</DebugSymbols>
                <RunAnalyzers>false</RunAnalyzers>
                <EnableNETAnalyzers>false</EnableNETAnalyzers>
                <NuGetAudit>false</NuGetAudit>
                <RestoreIgnoreFailedSources>true</RestoreIgnoreFailedSources>
                <ImportDirectoryBuildProps>false</ImportDirectoryBuildProps>
                <ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="{SecurityElement.Escape(sourceFileName)}" />
                <Reference Include="reCart.Contracts"><HintPath>{SecurityElement.Escape(contractsDll)}</HintPath><Private>false</Private></Reference>
                <Reference Include="reCart.Compiler"><HintPath>{SecurityElement.Escape(compilerDll)}</HintPath><Private>false</Private></Reference>
                <Reference Include="Foundation.Retro"><HintPath>{SecurityElement.Escape(retroDll)}</HintPath><Private>false</Private></Reference>
                <Reference Include="reEngine.Core"><HintPath>{SecurityElement.Escape(coreDll)}</HintPath><Private>false</Private></Reference>
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(csprojPath, xml, new UTF8Encoding(false));
        return csprojPath;
    }

    private async Task RunOutOfProcessBuildAsync(
        string csprojPath,
        string outDir,
        Action<string>? progressCallback,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _dotnetExecutable,
            WorkingDirectory = Path.GetDirectoryName(csprojPath)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("build");
        psi.ArgumentList.Add(csprojPath);
        psi.ArgumentList.Add("--disable-build-servers");
        psi.ArgumentList.Add("-p:UseSharedCompilation=false");
        psi.ArgumentList.Add("-nodeReuse:false");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("Release");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(outDir);
        psi.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        var outputLines = new List<string>();
        using Process proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to launch out-of-process dotnet compiler.");
        using var jobGuard = new JobObjectGuard();
        jobGuard.TryAttachProcess(proc);

        proc.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) lock (outputLines) outputLines.Add(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) lock (outputLines) outputLines.Add(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_buildTimeout);

        Task waitTask = proc.WaitForExitAsync(timeoutCts.Token);
        while (!waitTask.IsCompleted)
        {
            Task heartbeatDelay = Task.Delay(TimeSpan.FromSeconds(10), timeoutCts.Token);
            Task finished = await Task.WhenAny(waitTask, heartbeatDelay).ConfigureAwait(false);
            if (!ReferenceEquals(finished, waitTask))
            {
                progressCallback?.Invoke("[HEARTBEAT] [AOT] Compiling lifted module via dotnet build...");
            }
        }
        await waitTask.ConfigureAwait(false);

        if (proc.ExitCode != 0)
        {
            string diag;
            lock (outputLines) diag = string.Join(Environment.NewLine, outputLines.TakeLast(25));
            throw new InvalidOperationException($"Out-of-process AOT build failed (ExitCode={proc.ExitCode}):{Environment.NewLine}{diag}");
        }
    }

    private static ICartRecompGameModule LoadCompiledModule(string assemblyPath, string? preferredTypeName)
    {
        string fullAssemblyPath = Path.GetFullPath(assemblyPath);
        var alc = new CartModuleLoadContext(Path.GetDirectoryName(fullAssemblyPath)!);
        using var ms = new MemoryStream(File.ReadAllBytes(fullAssemblyPath), writable: false);
        Assembly asm = alc.LoadFromStream(ms);

        Type? moduleType = !string.IsNullOrWhiteSpace(preferredTypeName)
            ? asm.GetType(preferredTypeName.Trim(), throwOnError: false, ignoreCase: false)
            : null;
        moduleType ??= asm.GetTypes().FirstOrDefault(t =>
            t is { IsClass: true, IsAbstract: false } && typeof(ICartRecompGameModule).IsAssignableFrom(t));

        if (moduleType is null)
        {
            throw new InvalidOperationException($"Compiled assembly '{assemblyPath}' contains no ICartRecompGameModule type.");
        }

        return Activator.CreateInstance(moduleType) as ICartRecompGameModule
            ?? throw new InvalidOperationException($"Failed to instantiate '{moduleType.FullName}'.");
    }

    private static string ResolveHostAssembly(Type anchorType, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(anchorType.Assembly.Location) && File.Exists(anchorType.Assembly.Location))
        {
            return Path.GetFullPath(anchorType.Assembly.Location);
        }
        string fallback = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(fallback)) return Path.GetFullPath(fallback);
        throw new FileNotFoundException($"Missing required AOT host reference assembly '{fileName}'.", fallback);
    }

    private sealed class CartModuleLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public CartModuleLoadContext(string basePath)
            : base(isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(basePath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is "reCart.Contracts" or "reCart.Compiler" or "Foundation.Retro" or "reEngine.Core" or "Foundation.Core")
            {
                return null;
            }
            string? resolved = _resolver.ResolveAssemblyToPath(assemblyName);
            return resolved is not null ? LoadFromAssemblyPath(resolved) : null;
        }
    }
}
