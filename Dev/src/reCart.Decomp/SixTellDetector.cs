// @module: reCart.Decomp.SixTellDetector
// @purpose: Detects all 6 D4_CleanSource decompiler tells across NES C/header directories and build configs.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Text, System.Text.RegularExpressions
// @exports: SixTellDetector

using System.Text;
using System.Text.RegularExpressions;

namespace reCart.Decomp;

/// <summary>
/// Inspects C/header source directories and build configs for the 6 canonical decompiler tells at <c>D4_CleanSource</c>.
/// </summary>
public static partial class SixTellDetector
{
    private static readonly HashSet<string> BuildConfigFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".clangd", "compile_commands.json", "CMakeLists.txt", "Makefile", "GNUmakefile", "meson.build"
    };

    private static readonly (string RuleId, Regex Pattern, string Description)[] RawTokenRules =
    [
        ("D4-T1-UNDEF", new Regex(@"\bundefined(?:1|2|3|4|8)?\b", RegexOptions.CultureInvariant),
            "Raw Ghidra undefined* pseudo-type detected; replace with explicit stdint type (uint8_t/uint16_t)."),
        ("D4-T1-PARAM", new Regex(@"\bparam_?\d+\b", RegexOptions.CultureInvariant),
            "Raw Ghidra param_* positional parameter detected; assign domain parameter name."),
        ("D4-T1-PCODE", new Regex(@"\b(?:CONCAT(?:11|12|13|21|22|31|44)|SUB(?:21|31|32|41|42|81|82|84)|ZEXT(?:12|13|14|23|24)|SEXT(?:12|13|14|23|24)|(?:SCARRY|SBORROW|CARRY)[12]|BADSPACEBASE)\b", RegexOptions.CultureInvariant),
            "Raw Ghidra P-code macro (CONCAT*/SUB*/ZEXT*/SEXT*) detected; express using clean C bit shifts or casts."),
        ("D4-T1-REG", new Regex(@"\b(?:extraout|unaff)_[A-Za-z0-9_]+\b|\bin_(?:[A-Z][A-Za-z0-9_]*|stack_[0-9A-Za-z_]+|register_[0-9A-Za-z_]+|reg[0-9_]*|r\d+|[re]?(?:ax|bx|cx|dx|si|di|bp|sp|ip)|[abcd][hl]|pc|sr|sp|cc|cf|zf|nf|vf|af|df|of|sf|pf)\b", RegexOptions.CultureInvariant),
            "Raw Ghidra register-artifact variable (in_*/extraout_*/unaff_*) detected."),
        ("D4-T1-VAR", new Regex(@"\b(?:[apbcsuilf]+Var\d+|pcVar\d+|pp?[A-Za-z]?Var\d+|local_[0-9A-Fa-f]+|stack0x[0-9A-Fa-f]+)\b", RegexOptions.CultureInvariant),
            "Raw Ghidra stack/register temporary variable (uVar*/iVar*/pcVar*/bVar*) detected."),
        ("D4-T1-SYM", new Regex(@"\b(?:FUN|DAT|UNK|PTR|LAB|SUB|switchD)_[0-9A-Za-z_]+\b|\b(?:sub|loc)_[0-9A-Fa-f]+\b|\bcode\s*\*", RegexOptions.CultureInvariant),
            "Unmapped Ghidra/Hex-Rays symbol (FUN_*/DAT_*/UNK_*/LAB_*/sub_*) detected."),
        ("D4-T1-SHIFT", new Regex(@"(?:<<|\*)\s*(?:16|24|0x10|0x18|0x10000|0x1000000)[uUlL]*[\s\)]*>>\s*(?:16|24|0x10|0x18)\b", RegexOptions.CultureInvariant),
            "Unfolded ISA sign-extension shift sequence detected; fold into (int16_t)/(int8_t) cast."),
        ("D4-T1-PTR", new Regex(@"&\s*g_[A-Za-z0-9_]+(?:\s*\))?\s*[+-](?![>=\-])|\*\s*\([^);]+\*\)\s*&\s*(?:\(|g_[A-Za-z0-9_]+|[A-Za-z_]\w*\s*(?:\.|->|\[))", RegexOptions.CultureInvariant),
            "Raw global byte-offset arithmetic or redundant address-cast dereference detected."),
        ("D4-T1-FLOW", new Regex(@"\bwhile\s*\(\s*true\s*\)|\bgoto\s+(?:LAB|loc|L)_[0-9A-Za-z_]+\s*;|\b([A-Za-z_]\w*(?:\.[A-Za-z_]\w*|->[A-Za-z_]\w*){0,3})\s*(?<![!<>])=\s*\1\s*[+-]\s*1\s*;", RegexOptions.CultureInvariant),
            "Unidiomatic decompiler control flow or three-address increment detected.")
    ];

    private static readonly (string RuleId, Regex Pattern, string Description)[] AbiMismatchRules =
    [
        ("D4-T6-CALLCONV", new Regex(@"\b(?:__stdcall|__cdecl|__fastcall|__thiscall|__vectorcall|WINAPI|APIENTRY)\b|__attribute__\s*\(\(\s*(?:ms_abi|sysv_abi|stdcall|fastcall|cdecl)\b", RegexOptions.CultureInvariant),
            "Non-6502 host calling convention detected in NES decompilation source."),
        ("D4-T6-PTRSZ", new Regex(@"\bsizeof\s*\(\s*void\s*\*\s*\)\s*(?:==|!=|<|>)\s*(?:4|8)\b|\bsizeof\s*\(\s*long\s*\)\s*==\s*8\b|\(\s*(?:uintptr_t|intptr_t|uint64_t|u64)\s*\)\s*0x[0-9A-Fa-f]{4}\b", RegexOptions.CultureInvariant),
            "Host 32/64-bit pointer ABI assumption detected; NES 6502 uses 16-bit addresses."),
        ("D4-T6-MMIO", new Regex(@"\(\s*(?:volatile\s+)?(?:uint16_t|int16_t|uint32_t|int32_t|uint64_t|int64_t|u16|s16|u32|s32|u64|s64|ushort|uint|ulong)\s*\*\s*\)\s*(?:0x(?:200[0-7]|40[01][0-9A-Fa-f]))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "Wide (>8-bit) pointer access targeting an 8-bit NES PPU/APU/IO hardware register.")
    ];

    [GeneratedRegex(@"_(?:0[1-9]|[1-9]\d+)\.c$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SplitFragmentFileRegex();

    [GeneratedRegex(@"/\*\s*WARNING:[^\r\n]*|^\s*//\s*Function:[^\r\n]*|^\s*\*\s*@(?:callers|callees)\s+none\b", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex GhidraBoilerplateRegex();

    [GeneratedRegex(@"(?:^|[\s""',=\[])-w(?=$|[\s""',\];])", RegexOptions.CultureInvariant)]
    private static partial Regex BareWarnFlagRegex();

    [GeneratedRegex(@"#\s*pragma\s+(?:clang|GCC)\s+diagnostic\s+ignored\s+""(?:-Weverything|-Wall|-Wextra)""", RegexOptions.CultureInvariant)]
    private static partial Regex PragmaWarnSuppressRegex();

    [GeneratedRegex(@"(?:--target=|-target[""',\s-]+|-march=|-mabi=)(?:x86_64|amd64|aarch64|arm64|win64|x64|lp64)\b|-m64\b|\b(?:__x86_64__|_WIN64|__LP64__)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Host64TargetConfigRegex();

    /// <summary>Returns <see langword="true"/> if <paramref name="filePath"/> matches a split fragment name (<c>_*_01.._NN.c</c> or <c>_01.._NN.c</c>).</summary>
    public static bool IsSplitFragmentFileName(string filePath) =>
        !string.IsNullOrWhiteSpace(filePath) && SplitFragmentFileRegex().IsMatch(Path.GetFileName(filePath));

    /// <summary>Scans a directory of C/header files and build configuration files for all 6 decompiler tells.</summary>
    public static SixTellScanResult ScanDirectory(string directoryPath, bool includeBuildConfigs = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        string fullRoot = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException($"Source directory not found: '{fullRoot}'.");
        }

        string[] sourceFiles = Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories)
            .Where(p => IsCOrHeaderExtension(Path.GetExtension(p)))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var findings = new List<DecompTellFinding>();
        var parsedForAbi = new List<(string Label, string StrippedText, bool IsHeader)>();

        foreach (string file in sourceFiles)
        {
            string label = Path.GetRelativePath(fullRoot, file).Replace('\\', '/');
            string text = File.ReadAllText(file, Encoding.UTF8);
            bool isHeader = Path.GetExtension(file).Equals(".h", StringComparison.OrdinalIgnoreCase);
            findings.AddRange(ScanSourceText(text, label, isHeader));
            parsedForAbi.Add((label, SixTellAnalyzerHelpers.StripCommentsAndStringsPreserveLines(text), isHeader));
        }

        SixTellAnalyzerHelpers.CheckCrossFileAbiMismatches(parsedForAbi, findings);

        if (includeBuildConfigs)
        {
            foreach (string cfg in CollectBuildConfigs(fullRoot))
            {
                string label = Path.GetRelativePath(fullRoot, cfg).Replace('\\', '/');
                findings.AddRange(ScanBuildConfigText(File.ReadAllText(cfg, Encoding.UTF8), label));
            }
        }

        return BuildScanResult(sourceFiles.Length, findings);
    }

    /// <summary>Scans a single C or header source text string across all 6 tell families.</summary>
    public static IReadOnlyList<DecompTellFinding> ScanSourceText(string sourceText, string fileNameOrPath, bool isHeaderFile = false)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        string label = string.IsNullOrWhiteSpace(fileNameOrPath) ? (isHeaderFile ? "source.h" : "source.c") : fileNameOrPath;
        var findings = new List<DecompTellFinding>();

        if (!isHeaderFile && IsSplitFragmentFileName(label))
        {
            findings.Add(new(SixTellKind.SplitFragmentFiles, "D4-T3-FRAG", label, 1, Path.GetFileName(label),
                "Mechanical split fragment filename (_*_01.._NN.c) banned at D4_CleanSource; coalesce into logical domain modules."));
        }

        if (sourceText.Contains("-Wno-everything", StringComparison.OrdinalIgnoreCase) ||
            sourceText.Contains("-Wno-all", StringComparison.OrdinalIgnoreCase) ||
            PragmaWarnSuppressRegex().IsMatch(sourceText))
        {
            findings.Add(new(SixTellKind.BannedWarningSuppression, "D4-T2-PRAGMA", label, 1, "-Wno-everything / pragma ignored",
                "Blanket warning suppression (-Wno-everything or diagnostic ignored) detected in source."));
        }

        foreach (Match m in GhidraBoilerplateRegex().Matches(sourceText))
        {
            findings.Add(new(SixTellKind.RawDecompilerTokens, "D4-T1-COMMENT", label, SixTellAnalyzerHelpers.LineOf(sourceText, m.Index), TrimSnippet(m.Value),
                "Raw Ghidra auto-comment boilerplate (/* WARNING: */ or // Function:) detected."));
        }

        string stripped = SixTellAnalyzerHelpers.StripCommentsAndStringsPreserveLines(sourceText);
        foreach ((string ruleId, Regex rx, string desc) in RawTokenRules)
        {
            foreach (Match m in rx.Matches(stripped))
            {
                findings.Add(new(SixTellKind.RawDecompilerTokens, ruleId, label, SixTellAnalyzerHelpers.LineOf(stripped, m.Index), TrimSnippet(m.Value), desc));
            }
        }

        foreach ((string ruleId, Regex rx, string desc) in AbiMismatchRules)
        {
            foreach (Match m in rx.Matches(stripped))
            {
                findings.Add(new(SixTellKind.AbiMismatch, ruleId, label, SixTellAnalyzerHelpers.LineOf(stripped, m.Index), TrimSnippet(m.Value), desc));
            }
        }

        SixTellAnalyzerHelpers.CheckLowIdentifierReadability(stripped, label, findings);
        SixTellAnalyzerHelpers.CheckModuleHeaderAndComments(sourceText, stripped, label, isHeaderFile, findings);
        return findings;
    }

    /// <summary>Scans a build configuration file (<c>.clangd</c>, <c>compile_commands.json</c>, <c>Makefile</c>) for banned suppression flags or host ABI mismatches.</summary>
    public static IReadOnlyList<DecompTellFinding> ScanBuildConfigText(string configText, string fileLabel = "compile_commands.json")
    {
        ArgumentNullException.ThrowIfNull(configText);
        var findings = new List<DecompTellFinding>();

        if (configText.Contains("-Wno-everything", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new(SixTellKind.BannedWarningSuppression, "D4-T2-NOWARN-EVERYTHING", fileLabel, 1, "-Wno-everything",
                "Banned '-Wno-everything' compiler warning suppression flag detected."));
        }
        if (configText.Contains("-Wno-all", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new(SixTellKind.BannedWarningSuppression, "D4-T2-NOWARN-ALL", fileLabel, 1, "-Wno-all",
                "Banned '-Wno-all' compiler warning suppression flag detected."));
        }
        if (BareWarnFlagRegex().IsMatch(configText))
        {
            findings.Add(new(SixTellKind.BannedWarningSuppression, "D4-T2-BARE-W", fileLabel, 1, "-w",
                "Banned bare '-w' warning suppression flag detected."));
        }
        foreach (Match m in Host64TargetConfigRegex().Matches(configText))
        {
            findings.Add(new(SixTellKind.AbiMismatch, "D4-T6-TARGET", fileLabel, SixTellAnalyzerHelpers.LineOf(configText, m.Index), m.Value,
                $"Host 64-bit target ABI '{m.Value}' conflicts with NES 16-bit freestanding ABI."));
        }

        return findings;
    }

    private static SixTellScanResult BuildScanResult(int filesScanned, List<DecompTellFinding> findings)
    {
        SixTellKind[] kinds = findings.Select(f => f.TellKind).Distinct().OrderBy(k => (int)k).ToArray();
        bool clean = findings.Count == 0;
        string summary = clean
            ? $"SixTellDetector passed across {filesScanned} C/header file(s) with 0 tells."
            : $"SixTellDetector flagged {findings.Count} violation(s) across {kinds.Length} tell category(ies): {string.Join(", ", kinds)}.";
        return new SixTellScanResult(clean, filesScanned, kinds, findings, summary);
    }

    private static IEnumerable<string> CollectBuildConfigs(string fullRoot) =>
        Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories)
            .Where(p =>
            {
                string name = Path.GetFileName(p);
                return BuildConfigFileNames.Contains(name) ||
                       name.EndsWith(".cmake", StringComparison.OrdinalIgnoreCase) ||
                       name.EndsWith(".mk", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

    private static bool IsCOrHeaderExtension(string ext) =>
        ext.Equals(".c", StringComparison.OrdinalIgnoreCase) || ext.Equals(".h", StringComparison.OrdinalIgnoreCase);

    private static string TrimSnippet(string value)
    {
        string compact = Regex.Replace(value, @"\s+", " ").Trim();
        return compact.Length > 72 ? compact[..72] + "..." : compact;
    }
}
