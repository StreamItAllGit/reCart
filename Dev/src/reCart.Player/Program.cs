// @module: reCart.Player.Program
// @purpose: WinExe CLI entrypoint for reCart.Player enforcing Release automation fail-closed ExitCode=2, --version/--help/--verify, and ReCartSessionHost execution.
// @depends-on: System, System.IO, System.Threading, System.Threading.Tasks, Foundation.Core, reCart.Contracts, reEngine.Core.Resolution, reEngine.Host.Contracts
// @exports: Program

using Foundation.Core;
using reCart.Compiler;
using reCart.Contracts;
using reCart.Core.Runtime;
using reEngine.Core.Resolution;
using reEngine.Host.Contracts;

namespace reCart.Player;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) => RunMain(args ?? Array.Empty<string>());

    internal static int RunMain(string[] args) =>
        RunMainAsync(args).GetAwaiter().GetResult();

    internal static async Task<int> RunMainAsync(
        string[] args,
        Func<CartVideoRegionStandard, CartExecutionTierMode, byte[]?, ICartMachineRuntime?>? runtimeFactory = null,
        ICartCompilerPipeline? compilerPipeline = null,
        CancellationToken ct = default)
    {
        args ??= Array.Empty<string>();
#if !RECART_ENABLE_AUTOMATION
        if (PlayerCliOptions.HasAutomationPortFlag(args))
        {
            Console.Error.WriteLine("Automation loopback (--automation-port) is disabled in Release builds (ExitCode=2).");
            return 2;
        }
#endif

        PlayerCliOptions options;
        try
        {
            options = PlayerCliOptions.Parse(args);
        }
        catch (Exception ex) when (SandboxEnvironmentSeam.IsContainmentViolation(ex))
        {
            Console.Error.WriteLine(ex.Message);
            return SandboxEnvironmentSeam.ContainmentExitCode;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        if (options.ShowVersion)
        {
            Console.WriteLine($"{CartEngineConstants.EngineId} v{CartEngineConstants.Version}");
            return 0;
        }
        if (options.ShowHelp)
        {
            Console.WriteLine(PlayerCliOptions.FormatHelpText());
            return 0;
        }

#if !RECART_ENABLE_AUTOMATION
        if (options.AutomationPort.HasValue)
        {
            Console.Error.WriteLine("Automation loopback (--automation-port) is disabled in Release builds (ExitCode=2).");
            return 2;
        }
#endif

        try
        {
            Directory.CreateDirectory(options.Environment.LogDirectory);
            CrashBundleService.RegisterGlobalHandlers(options.Environment.LogDirectory, CartEngineConstants.AppId, CartEngineConstants.Version);

            if (options.Verify)
            {
                return ReCartSessionHost.VerifyTarget(options, Console.Out, Console.Error);
            }

            if (options.Doctor)
            {
                return ReCartSessionHost.RunDoctor(options, Console.Out, Console.Error);
            }

            if (options.Environment.DryRun)
            {
                Console.WriteLine($"[DRY-RUN] {CartEngineConstants.DisplayName} v{CartEngineConstants.Version} backend={options.BackendId} aspect={options.AspectRatioMode} dataDir='{options.Environment.DataDirectory}'.");
                return 0;
            }

            if (options.LockstepSteps is > 0)
            {
                return await ReCartLockstepRunner.RunAsync(options, compilerPipeline ?? new ColdAotCacheCompiler(), Console.Out, Console.Error, ct).ConfigureAwait(false);
            }

            runtimeFactory ??= (region, tier, romBytes) =>
                romBytes is not null ? CartMachineFactory.CreateFromRomBytes(romBytes, region, tier) : null;
            compilerPipeline ??= new ColdAotCacheCompiler();

            var sessionHost = new ReCartSessionHost(options, runtimeFactory, compilerPipeline);
            HostSessionResult result = await sessionHost.RunAsync(ct).ConfigureAwait(false);
            if (result.ExitCode != 0) Console.Error.WriteLine(result.Summary);
            else Console.WriteLine(result.Summary);
            return result.ExitCode;
        }
        catch (BackendUnavailableException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 6;
        }
        catch (Exception ex) when (SandboxEnvironmentSeam.IsContainmentViolation(ex))
        {
            Console.Error.WriteLine(ex.Message);
            return SandboxEnvironmentSeam.ContainmentExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or InvalidDataException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            CrashBundleService.TryWriteCrashBundle(options.Environment.LogDirectory, CartEngineConstants.AppId, CartEngineConstants.Version, ex);
            Console.Error.WriteLine($"Unhandled {CartEngineConstants.DisplayName} player error: {ex.Message}");
            return 1;
        }
    }
}
