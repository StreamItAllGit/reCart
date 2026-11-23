// @module: reCart.Decomp.SixTellAnalyzerHelpers
// @purpose: Comment stripping, module-header/comment checks, readability scoring, and cross-file NES ABI prototype verification helpers.
// @depends-on: System, System.Collections.Generic, System.Linq, System.Text.RegularExpressions
// @exports: SixTellAnalyzerHelpers

using System.Text.RegularExpressions;

namespace reCart.Decomp;

internal static partial class SixTellAnalyzerHelpers
{
    private static readonly HashSet<string> AllowedShortIdentifiers = new(StringComparer.Ordinal)
    {
        "i", "j", "k", "n", "x", "y", "dx", "dy", "id", "ok", "pc", "sp", "sr", "ch", "hi", "lo"
    };

    private static readonly HashSet<string> DomainNumericStemPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "bank", "rom", "prg", "chr", "ppu", "apu", "pad", "joy", "pulse", "channel", "track",
        "voice", "stage", "level", "room", "world", "area", "sprite", "actor", "slot", "timer",
        "frame", "nametable", "palette", "tile", "irq", "nmi", "mmc", "port", "player", "bit"
    };

    [GeneratedRegex(@"\b(?:tmp_?\d+|temp_?\d+|var_?\d+|val_?\d+|arg_?\d+|ret_val\d*|work_word|work_acc|status_flags|tmp_val|accum_val|reg_val|slot_val|ctx_val|abi_val|byte_[0-9A-Fa-f]+|word_[0-9A-Fa-f]+|dword_[0-9A-Fa-f]+|off_[0-9A-Fa-f]+|unk_[0-9A-Za-z_]+|fn_\d+|func_?\d+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderIdentRegex();

    [GeneratedRegex(@"(?:^|[;(,])\s*(?:const\s+|volatile\s+|unsigned\s+|signed\s+|register\s+|static\s+|struct\s+)*(?!return\b|goto\b|else\b|case\b|typedef\b)[A-Za-z_]\w*(?:\s*\*+)?\s+([A-Za-z_]\w*)\s*(?=[=;,)\[])", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex DeclaredIdentRegex();

    [GeneratedRegex(@"^(?<stem>[A-Za-z_]\w*?)_(?:0\d|[2-9]\d*)$", RegexOptions.CultureInvariant)]
    private static partial Regex SsaNumericSuffixRegex();

    [GeneratedRegex(@"^[va][1-9]\d*$", RegexOptions.CultureInvariant)]
    private static partial Regex HexRaysShortVarRegex();

    [GeneratedRegex(@"^\s*(?:extern\s+)?(?<ret>(?:const\s+|volatile\s+|unsigned\s+|signed\s+|struct\s+)*[A-Za-z_]\w*(?:\s*\*+)?)\s+(?<name>[A-Za-z_]\w*)\s*\((?<params>[^;{}]*)\)\s*;", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex HeaderPrototypeRegex();

    [GeneratedRegex(@"^\s*(?<qual>(?:static\s+|inline\s+|__inline\s+)*)(?<ret>(?:const\s+|volatile\s+|unsigned\s+|signed\s+|struct\s+)*[A-Za-z_]\w*(?:\s*\*+)?)\s+(?<name>[A-Za-z_]\w*)\s*\((?<params>[^;{}]*)\)\s*\{", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex FunctionDefRegex();

    internal static string StripCommentsAndStringsPreserveLines(string text)
    {
        char[] buf = text.ToCharArray();
        for (int i = 0; i < text.Length;)
        {
            char ch = text[i];
            char nxt = i + 1 < text.Length ? text[i + 1] : '\0';
            if (ch == '/' && nxt is '/' or '*')
            {
                bool block = nxt == '*';
                buf[i] = buf[i + 1] = ' ';
                for (i += 2; i < text.Length; i++)
                {
                    if (!block && text[i] == '\n') break;
                    if (block && text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/') { buf[i] = buf[i + 1] = ' '; i += 2; break; }
                    if (text[i] != '\n') buf[i] = ' ';
                }
            }
            else if (ch is '"' or '\'')
            {
                char quote = ch;
                for (i++; i < text.Length; i++)
                {
                    if (text[i] == '\\' && i + 1 < text.Length) { if (text[i + 1] != '\n') buf[i + 1] = ' '; buf[i] = ' '; i++; continue; }
                    if (text[i] == quote) { i++; break; }
                    if (text[i] != '\n') buf[i] = ' ';
                }
            }
            else i++;
        }
        return new string(buf);
    }

    internal static void CheckModuleHeaderAndComments(
        string rawSource,
        string strippedSource,
        string fileLabel,
        bool isHeader,
        List<DecompTellFinding> findings)
    {
        string[] rawLines = rawSource.Replace("\r\n", "\n").Split('\n');
        string[] strippedLines = strippedSource.Replace("\r\n", "\n").Split('\n');
        bool hasTopHeaderComment = false;
        int nonEmptySeen = 0;

        for (int idx = 0; idx < rawLines.Length && nonEmptySeen < 16; idx++)
        {
            string rawTrim = rawLines[idx].Trim();
            string stripTrim = idx < strippedLines.Length ? strippedLines[idx].Trim() : string.Empty;
            if (string.IsNullOrEmpty(rawTrim)) continue;
            nonEmptySeen++;

            if (rawTrim.StartsWith("//", StringComparison.Ordinal) || rawTrim.StartsWith("/*", StringComparison.Ordinal) || rawTrim.StartsWith("*", StringComparison.Ordinal))
            {
                if (!IsGhidraBoilerplateComment(rawTrim))
                {
                    hasTopHeaderComment = true;
                    break;
                }
            }
            else if (!string.IsNullOrEmpty(stripTrim) && !stripTrim.StartsWith("#", StringComparison.Ordinal) && nonEmptySeen >= 2)
            {
                break;
            }
        }

        if (!hasTopHeaderComment)
        {
            findings.Add(new(SixTellKind.MissingModuleHeaderOrComments, "D4-T5-HDR", fileLabel, 1,
                rawLines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "(empty)",
                "Missing top-of-file module header comment describing module ownership and purpose."));
        }

        if (isHeader) return;

        int codeLines = strippedLines.Count(l => !string.IsNullOrWhiteSpace(l) && !l.Trim().StartsWith("#", StringComparison.Ordinal));
        int humanCommentLines = 0;
        for (int idx = 0; idx < rawLines.Length; idx++)
        {
            string raw = rawLines[idx].Trim();
            if ((raw.Contains("//", StringComparison.Ordinal) || raw.Contains("/*", StringComparison.Ordinal)) && !IsGhidraBoilerplateComment(raw))
            {
                humanCommentLines++;
            }
        }

        if (codeLines > 35 && humanCommentLines <= 1)
        {
            findings.Add(new(SixTellKind.MissingModuleHeaderOrComments, "D4-T5-CMT", fileLabel, 1,
                $"codeLines={codeLines}, comments={humanCommentLines}",
                "Non-trivial C translation unit lacks explanatory function/block comments."));
        }
    }

    internal static void CheckLowIdentifierReadability(string strippedSource, string fileLabel, List<DecompTellFinding> findings)
    {
        foreach (Match m in PlaceholderIdentRegex().Matches(strippedSource))
        {
            findings.Add(new(SixTellKind.LowIdentifierReadability, "D4-T4-PLACEHOLDER", fileLabel, LineOf(strippedSource, m.Index), m.Value,
                $"Mechanical or placeholder identifier '{m.Value}' indicates unrefactored decompiler output."));
        }

        var declared = new List<(string Ident, int Index)>();
        foreach (Match m in DeclaredIdentRegex().Matches(strippedSource))
        {
            string ident = m.Groups[1].Value;
            if (!IsCKeyword(ident))
            {
                declared.Add((ident, m.Index));
            }
        }

        int hexRaysShortCount = declared.Count(d => HexRaysShortVarRegex().IsMatch(d.Ident));
        if (hexRaysShortCount >= 2)
        {
            var first = declared.First(d => HexRaysShortVarRegex().IsMatch(d.Ident));
            findings.Add(new(SixTellKind.LowIdentifierReadability, "D4-T4-HEXRAYS", fileLabel, LineOf(strippedSource, first.Index), first.Ident,
                $"Hex-Rays style positional local identifiers ({hexRaysShortCount}x vN/aN) reduce readability."));
        }

        foreach ((string ident, int index) in declared)
        {
            Match ssa = SsaNumericSuffixRegex().Match(ident);
            if (ssa.Success && !HasDomainPrefix(ssa.Groups["stem"].Value))
            {
                findings.Add(new(SixTellKind.LowIdentifierReadability, "D4-T4-SSA", fileLabel, LineOf(strippedSource, index), ident,
                    $"SSA clone numeric suffix on declared identifier '{ident}'."));
            }
        }

        if (declared.Count >= 4)
        {
            int readable = declared.Count(d =>
                d.Ident.Length >= 3 &&
                !PlaceholderIdentRegex().IsMatch(d.Ident) &&
                !HexRaysShortVarRegex().IsMatch(d.Ident) &&
                !SsaNumericSuffixRegex().IsMatch(d.Ident));
            int nonTrivial = declared.Count(d => !AllowedShortIdentifiers.Contains(d.Ident));
            if (nonTrivial >= 4 && (double)readable / nonTrivial < 0.40)
            {
                findings.Add(new(SixTellKind.LowIdentifierReadability, "D4-T4-RATIO", fileLabel, 1,
                    $"readable={readable}/{nonTrivial}",
                    "Low descriptive identifier readability ratio (< 40% meaningful names in declarations)."));
            }
        }
    }

    internal static void CheckCrossFileAbiMismatches(
        IReadOnlyList<(string Label, string StrippedText, bool IsHeader)> parsedFiles,
        List<DecompTellFinding> findings)
    {
        var prototypes = new Dictionary<string, (string Ret, int ParamCount, string Label, int Line)>(StringComparer.Ordinal);
        foreach ((string label, string text, bool isHeader) in parsedFiles.Where(f => f.IsHeader))
        {
            foreach (Match m in HeaderPrototypeRegex().Matches(text))
            {
                string name = m.Groups["name"].Value;
                if (IsCKeyword(name)) continue;
                string ret = NormalizeCType(m.Groups["ret"].Value);
                int pCount = CountCParams(m.Groups["params"].Value);
                if (pCount < 0) continue;
                prototypes[name] = (ret, pCount, label, LineOf(text, m.Index));
            }
        }

        foreach ((string label, string text, bool isHeader) in parsedFiles.Where(f => !f.IsHeader))
        {
            foreach (Match m in FunctionDefRegex().Matches(text))
            {
                if (m.Groups["qual"].Value.Contains("static", StringComparison.Ordinal)) continue;
                string name = m.Groups["name"].Value;
                if (IsCKeyword(name) || !prototypes.TryGetValue(name, out var proto)) continue;
                string ret = NormalizeCType(m.Groups["ret"].Value);
                int pCount = CountCParams(m.Groups["params"].Value);
                if (pCount < 0) continue;
                if (!string.Equals(proto.Ret, ret, StringComparison.Ordinal) || proto.ParamCount != pCount)
                {
                    findings.Add(new(SixTellKind.AbiMismatch, "D4-T6-PROTO", label, LineOf(text, m.Index),
                        $"{ret} {name}({pCount} args) vs {proto.Label}:{proto.Line} {proto.Ret}({proto.ParamCount} args)",
                        $"Function definition ABI for '{name}' mismatches header prototype in '{proto.Label}'."));
                }
            }
        }
    }

    internal static int LineOf(string text, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n') line++;
        }
        return line;
    }

    private static bool IsGhidraBoilerplateComment(string line) =>
        line.Contains("WARNING:", StringComparison.Ordinal) ||
        line.StartsWith("// Function:", StringComparison.Ordinal) ||
        line.Contains("@callers none", StringComparison.Ordinal) ||
        line.Contains("@callees none", StringComparison.Ordinal);

    private static bool HasDomainPrefix(string stem) =>
        DomainNumericStemPrefixes.Any(p => stem.Equals(p, StringComparison.OrdinalIgnoreCase) || stem.StartsWith(p + "_", StringComparison.OrdinalIgnoreCase));

    private static bool IsCKeyword(string ident) =>
        ident is "if" or "for" or "while" or "switch" or "return" or "sizeof" or "typedef" or "struct" or "enum" or "union" or "else";

    private static string NormalizeCType(string raw)
    {
        string compact = Regex.Replace(raw, @"\s+", " ").Trim();
        compact = Regex.Replace(compact, @"\b(const|volatile)\b", string.Empty).Trim();
        return compact switch
        {
            "byte" or "uchar" or "uint8_t" => "u8",
            "sbyte" or "int8_t" => "s8",
            "ushort" or "word" or "uint16_t" or "nes_addr_t" => "u16",
            "int16_t" => "s16",
            "uint" or "dword" or "uint32_t" => "u32",
            "int32_t" => "s32",
            _ => Regex.Replace(compact, @"\s+", " ").Trim()
        };
    }

    private static int CountCParams(string rawParams)
    {
        string trimmed = rawParams.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed == "void") return 0;
        if (trimmed.Contains("...", StringComparison.Ordinal)) return -1;
        return trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
