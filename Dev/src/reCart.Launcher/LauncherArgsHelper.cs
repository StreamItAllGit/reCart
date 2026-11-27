// @module: reCart.Launcher.LauncherArgsHelper
// @purpose: Pure BCL CLI sync-wait detection, autostart sanitization, and caller-CWD relative path normalization for reCart.exe.
// @depends-on: System, System.Collections.Generic, System.IO
// @exports: LauncherArgsHelper

using System;
using System.Collections.Generic;
using System.IO;

namespace reCart.Launcher;

/// <summary>
/// Evaluates sync-wait semantics and rebases caller-CWD relative file/directory CLI arguments while preserving option values verbatim.
/// </summary>
internal static class LauncherArgsHelper
{
    internal static readonly HashSet<string> ValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--open", "--module", "--rom", "--disc", "--preload-media", "--media-path", "--verify-module",
        "--reengine-dir", "--suite-dir", "--data-dir", "--config", "--log-dir", "--save-dir", "--feed",
        "--port", "--ui", "--backend", "--aspect-mode", "--scale", "--region", "--execution-tier",
        "--audio-driver", "--frames", "--overscan", "--fps-mode", "--target-fps", "--profile", "--suite-tab",
        "--automation-port", "--automation-token", "--fixture", "--dump-screenshot", "--dump-audio",
        "--shutdown-timeout-ms"
    };

    internal static readonly HashSet<string> PathOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--open", "--module", "--rom", "--disc", "--preload-media", "--media-path", "--verify-module",
        "--reengine-dir", "--suite-dir", "--data-dir", "--config", "--log-dir", "--save-dir", "--feed",
        "--fixture", "--dump-screenshot", "--dump-audio"
    };

    internal static readonly HashSet<string> SyncFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "--wait", "--verify", "--verify-module", "--headless", "--json",
        "--check-updates", "--register-assoc", "--doctor", "--automation-port",
        "--jit-bringup", "--require-backend", "--frames", "--dump-screenshot", "--dump-audio",
        "--dry-run", "--tray-only", "--help", "-h", "/?", "help", "--version", "-v"
    };

    private static readonly HashSet<string> BackendTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "gl", "gl33", "opengl", "opengl33", "vk", "vulkan", "dx", "dx12", "d3d12", "directx", "directx12",
        "parallelcpu", "parallel", "cpu", "sw", "software", "metal"
    };

    /// <summary>Strips <c>--autostart*</c> / <c>--auto-start*</c> tokens to enforce <c>AllowAutoStart = false</c>.</summary>
    public static string[] SanitizeArgs(string[]? args)
    {
        if (args is null || args.Length == 0) return Array.Empty<string>();
        var clean = new List<string>(args.Length);
        foreach (string raw in args)
        {
            string trimmed = (raw ?? string.Empty).Trim();
            if (trimmed.StartsWith("--autostart", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("--auto-start", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            clean.Add(raw ?? string.Empty);
        }
        return clean.ToArray();
    }

    /// <summary>Returns <see langword="true"/> when the launcher should attach to the parent console and wait for the child process.</summary>
    public static bool ShouldWaitForChild(string[]? args)
    {
        if (args is null || args.Length == 0) return false;

        for (int i = 0; i < args.Length; i++)
        {
            string trimmed = (args[i] ?? string.Empty).Trim();
            if (trimmed.Length == 0) continue;

            (string key, bool hasInlineValue) = SplitOption(trimmed);
            if (SyncFlags.Contains(key)) return true;

            if (ValueOptions.Contains(key))
            {
                if (!hasInlineValue && i + 1 < args.Length) i++;
                continue;
            }
        }

        return false;
    }

    /// <summary>Resolves relative path arguments against <paramref name="callerWorkingDirectory"/> while preserving non-path option values verbatim.</summary>
    public static string[] NormalizePathArguments(string[]? args, string callerWorkingDirectory)
    {
        if (args is null || args.Length == 0) return Array.Empty<string>();

        string baseDir = string.IsNullOrWhiteSpace(callerWorkingDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(callerWorkingDirectory);

        var normalized = new string[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            string raw = args[i] ?? string.Empty;
            string trimmed = raw.Trim();
            if (trimmed.Length == 0)
            {
                normalized[i] = raw;
                continue;
            }

            (string key, bool hasInlineValue) = SplitOption(trimmed);
            if (hasInlineValue)
            {
                string inlineVal = trimmed[(key.Length + 1)..].Trim().Trim('"');
                normalized[i] = PathOptions.Contains(key) && ShouldNormalizeValue(key, inlineVal, baseDir)
                    ? $"{key}={Path.GetFullPath(inlineVal, baseDir)}"
                    : raw;
                continue;
            }

            normalized[i] = raw;
            if (string.Equals(key, "--require-backend", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < args.Length &&
                BackendTokens.Contains((args[i + 1] ?? string.Empty).Trim()))
            {
                i++;
                normalized[i] = args[i] ?? string.Empty;
                continue;
            }

            if (ValueOptions.Contains(key))
            {
                if (i + 1 < args.Length)
                {
                    i++;
                    string val = args[i] ?? string.Empty;
                    string unquoted = val.Trim().Trim('"');
                    normalized[i] = PathOptions.Contains(key) && ShouldNormalizeValue(key, unquoted, baseDir)
                        ? Path.GetFullPath(unquoted, baseDir)
                        : val;
                }
                continue;
            }

            if (!trimmed.StartsWith("-", StringComparison.Ordinal) &&
                !string.Equals(trimmed, "/?", StringComparison.Ordinal) &&
                !string.Equals(trimmed, "help", StringComparison.OrdinalIgnoreCase))
            {
                normalized[i] = Path.GetFullPath(trimmed.Trim('"'), baseDir);
            }
        }

        return normalized;
    }

    private static (string Key, bool HasInlineValue) SplitOption(string trimmed)
    {
        if (trimmed.StartsWith("--", StringComparison.Ordinal))
        {
            int eqIdx = trimmed.IndexOf('=');
            if (eqIdx > 2) return (trimmed[..eqIdx], true);
        }
        return (trimmed, false);
    }

    private static bool ShouldNormalizeValue(string optionKey, string trimmedVal, string baseDir)
    {
        if (trimmedVal.Length == 0) return false;
        if (string.Equals(optionKey, "--feed", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(trimmedVal, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        if (string.Equals(optionKey, "--verify-module", StringComparison.OrdinalIgnoreCase))
        {
            return LooksLikePackageOrFilePath(trimmedVal, baseDir);
        }
        return true;
    }

    private static bool LooksLikePackageOrFilePath(string val, string baseDir)
    {
        if (Path.IsPathRooted(val) || val.Contains('/') || val.Contains('\\') || val.StartsWith('.'))
            return true;
        if (val.EndsWith(".recomp", StringComparison.OrdinalIgnoreCase) ||
            val.EndsWith(".decomp", StringComparison.OrdinalIgnoreCase) ||
            val.EndsWith(".nes", StringComparison.OrdinalIgnoreCase) ||
            val.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return true;
        string cand = Path.Combine(baseDir, val);
        return File.Exists(cand) || Directory.Exists(cand);
    }
}
