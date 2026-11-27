// @module: reCart.Launcher.Program
// @purpose: Clean-root launcher (< 2 MB) resolving app\reCart.Player.exe via embedded Foundation.Launcher with sync-wait, stream forwarding, and CWD path normalization.
// @depends-on: System, System.ComponentModel, System.Diagnostics, System.IO, System.Reflection, System.Runtime.CompilerServices, System.Runtime.InteropServices, System.Threading, Foundation.Launcher
// @exports: Program

using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Foundation.Launcher;

namespace reCart.Launcher;

internal static class Program
{
    public const bool AllowAutoStart = false;
    private const int AttachParentProcess = -1;
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformationData
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int jobObjectInfoClass, ref JobObjectExtendedLimitInformationData lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    static Program()
    {
        AppDomain.CurrentDomain.AssemblyResolve += static (_, e) =>
        {
            if (string.Equals(new AssemblyName(e.Name).Name, "Foundation.Launcher", StringComparison.OrdinalIgnoreCase))
            {
                using Stream? stream = typeof(Program).Assembly.GetManifestResourceStream("Foundation.Launcher.dll");
                if (stream is not null)
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    return Assembly.Load(ms.ToArray());
                }
            }
            return null;
        };
    }

    [STAThread]
    private static int Main(string[] args) => RunLauncher(args ?? Array.Empty<string>());

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunLauncher(string[] rawArgs)
    {
        string rootDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? selfPath = Environment.ProcessPath;
        string appExe = RootLauncherHost.ResolveTargetExecutable(rootDir, Path.Combine("app", "reCart.Player.exe"), selfPath);

        if (!string.IsNullOrWhiteSpace(selfPath) &&
            string.Equals(Path.GetFullPath(appExe), Path.GetFullPath(selfPath), StringComparison.OrdinalIgnoreCase))
        {
            AttachParentConsoleIfInteractive();
            Console.Error.WriteLine($"Fail-closed self-spawn guard: refusing to launch '{appExe}' from itself.");
            return 1;
        }

        if (!File.Exists(appExe))
        {
            AttachParentConsoleIfInteractive();
            Console.Error.WriteLine($"reCart application binary not found at '{appExe}'.");
            return 1;
        }

        string[] cleanArgs = LauncherArgsHelper.SanitizeArgs(rawArgs);
        bool waitForChild = LauncherArgsHelper.ShouldWaitForChild(cleanArgs);
        if (waitForChild)
        {
            AttachParentConsoleIfInteractive();
        }

        bool redirOut = waitForChild && Console.IsOutputRedirected;
        bool redirErr = waitForChild && Console.IsErrorRedirected;
        string[] forwardedArgs = LauncherArgsHelper.NormalizePathArguments(cleanArgs, Environment.CurrentDirectory);
        var psi = new ProcessStartInfo
        {
            FileName = appExe,
            WorkingDirectory = Path.GetDirectoryName(appExe) ?? rootDir,
            UseShellExecute = false,
            CreateNoWindow = waitForChild && !redirOut && !redirErr,
            RedirectStandardOutput = redirOut,
            RedirectStandardError = redirErr
        };
        foreach (string arg in forwardedArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        using Process? proc = Process.Start(psi);
        if (proc is null)
        {
            Console.Error.WriteLine($"Failed to start reCart application at '{appExe}'.");
            return 1;
        }

        if (!waitForChild)
        {
            return 0;
        }

        if (redirOut) proc.OutputDataReceived += (_, ev) => { if (ev.Data is not null) Console.Out.WriteLine(ev.Data); };
        if (redirErr) proc.ErrorDataReceived += (_, ev) => { if (ev.Data is not null) Console.Error.WriteLine(ev.Data); };
        if (redirOut) proc.BeginOutputReadLine();
        if (redirErr) proc.BeginErrorReadLine();

        IntPtr jobHandle = AttachKillOnCloseJob(proc);
        int cancelled = 0;
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            Interlocked.Exchange(ref cancelled, 1);
        };

        Console.CancelKeyPress += cancelHandler;
        try
        {
            while (!proc.WaitForExit(100))
            {
                if (Volatile.Read(ref cancelled) == 1)
                {
                    if (!proc.WaitForExit(4000) && !proc.HasExited)
                    {
                        try { proc.Kill(entireProcessTree: true); }
                        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                        {
                            Trace.WriteLine($"Launcher cancel teardown skipped already-exited child: {ex.Message}");
                        }
                    }
                    break;
                }
            }
            if (Volatile.Read(ref cancelled) == 0)
            {
                proc.WaitForExit();
            }
            return Volatile.Read(ref cancelled) == 1 ? 130 : proc.ExitCode;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            if (jobHandle != IntPtr.Zero)
            {
                CloseHandle(jobHandle);
            }
        }
    }

    private static IntPtr AttachKillOnCloseJob(Process proc)
    {
        if (!OperatingSystem.IsWindows()) return IntPtr.Zero;
        IntPtr job = CreateJobObjectW(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return IntPtr.Zero;

        var info = new JobObjectExtendedLimitInformationData
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };
        uint size = (uint)Marshal.SizeOf<JobObjectExtendedLimitInformationData>();
        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, size) ||
            !AssignProcessToJobObject(job, proc.Handle))
        {
            Trace.WriteLine($"Launcher job-object guard skipped (Win32 error {Marshal.GetLastWin32Error()}).");
            CloseHandle(job);
            return IntPtr.Zero;
        }

        return job;
    }

    private static void AttachParentConsoleIfInteractive()
    {
        if (OperatingSystem.IsWindows() && !Console.IsOutputRedirected && !Console.IsErrorRedirected)
        {
            _ = AttachConsole(AttachParentProcess);
        }
    }
}
