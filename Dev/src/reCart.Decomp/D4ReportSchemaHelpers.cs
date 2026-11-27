// @module: reCart.Decomp.D4ReportSchemaHelpers
// @purpose: Case-insensitive JSON schema extraction, compiler-flag linting, and D5/research-manifest validation helpers.
// @depends-on: System, System.Collections.Generic, System.Linq, System.Text.Json, System.Text.RegularExpressions
// @exports: D4ReportSchemaHelpers

using System.Text.Json;
using System.Text.RegularExpressions;

namespace reCart.Decomp;

internal static partial class D4ReportSchemaHelpers
{
    private static readonly string[] ValidDecompTiers = ["none", "D0", "D1", "D2", "D3", "D4", "D5"];

    internal static readonly JsonDocumentOptions LenientJsonDocOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    [GeneratedRegex(@"(?:^|\s)-w(?:\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex BareWarnRegex();

    [GeneratedRegex(@"^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256HexRegex();

    internal static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object)
        {
            if (obj.TryGetProperty(name, out value)) return true;
            foreach (JsonProperty prop in obj.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    internal static bool TryGetInt(JsonElement obj, out int result, params string[] names)
    {
        foreach (string name in names)
        {
            if (TryGetPropertyIgnoreCase(obj, name, out JsonElement el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out result))
            {
                return true;
            }
        }
        result = 0;
        return false;
    }

    internal static bool TryGetLong(JsonElement obj, out long result, params string[] names)
    {
        foreach (string name in names)
        {
            if (TryGetPropertyIgnoreCase(obj, name, out JsonElement el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out result))
            {
                return true;
            }
        }
        result = 0;
        return false;
    }

    internal static bool TryGetBool(JsonElement obj, out bool result, params string[] names)
    {
        foreach (string name in names)
        {
            if (TryGetPropertyIgnoreCase(obj, name, out JsonElement el) &&
                (el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False))
            {
                result = el.GetBoolean();
                return true;
            }
        }
        result = false;
        return false;
    }

    internal static string? TryGetString(JsonElement obj, params string[] names)
    {
        foreach (string name in names)
        {
            if (TryGetPropertyIgnoreCase(obj, name, out JsonElement el) && el.ValueKind == JsonValueKind.String)
            {
                return el.GetString();
            }
        }
        return null;
    }

    internal static IReadOnlyList<string> ExtractCompilerFlags(JsonElement root)
    {
        if (!TryGetPropertyIgnoreCase(root, "compilerFlags", out JsonElement el)) return Array.Empty<string>();
        if (el.ValueKind == JsonValueKind.String)
        {
            string raw = el.GetString() ?? string.Empty;
            return raw.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        }
        if (el.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (JsonElement item in el.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                {
                    list.Add(item.GetString()!.Trim());
                }
            }
            return list;
        }
        return Array.Empty<string>();
    }

    internal static void ValidateCompilerFlags(IReadOnlyList<string> flags, string sourceLabel, List<string> violations)
    {
        if (flags.Count == 0) return;
        string joined = string.Join(' ', flags);
        if (joined.Contains("-Wno-everything", StringComparison.OrdinalIgnoreCase))
        {
            violations.Add($"Banned '-Wno-everything' suppression flag detected in '{sourceLabel}'.");
        }
        if (joined.Contains("-Wno-all", StringComparison.OrdinalIgnoreCase))
        {
            violations.Add($"Banned '-Wno-all' suppression flag detected in '{sourceLabel}'.");
        }
        if (BareWarnRegex().IsMatch(joined))
        {
            violations.Add($"Banned bare '-w' suppression flag detected in '{sourceLabel}'.");
        }
    }

    internal static D5ByteMatchValidationResult ValidateD5Core(JsonElement root)
    {
        var violations = new List<string>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new(false, 0, -1, 0, ["d5_byte_match.json root must be a JSON object."], "Invalid d5_byte_match.json root.");
        }

        if (!TryGetBool(root, out bool passed, "passed", "isPassed"))
        {
            violations.Add("d5_byte_match.json missing required 'passed' boolean.");
        }
        else if (!passed)
        {
            violations.Add("d5_byte_match.json reported passed=false.");
        }

        if (!TryGetInt(root, out int mismatchedBytes, "mismatchedBytes", "mismatchedWords"))
        {
            violations.Add("d5_byte_match.json missing required 'mismatchedBytes'.");
            mismatchedBytes = -1;
        }
        else if (mismatchedBytes != 0)
        {
            violations.Add($"d5_byte_match.json mismatchedBytes={mismatchedBytes} (expected 0).");
        }

        if (!TryGetInt(root, out int matchedBinaries, "matchedBinaries", "matchedBinaryCount"))
        {
            violations.Add("d5_byte_match.json missing required 'matchedBinaries' or 'matchedBinaryCount'.");
        }
        else if (matchedBinaries <= 0)
        {
            violations.Add($"d5_byte_match.json matchedBinaries={matchedBinaries} (expected > 0).");
        }

        if (TryGetBool(root, out bool romMatched, "isVerified", "romMatched") && !romMatched)
        {
            violations.Add("d5_byte_match.json reported isVerified/romMatched=false.");
        }

        bool hasRomSize = TryGetLong(root, out long romBytes, "romBytes", "fileSizeBytes");
        if (hasRomSize && romBytes <= 0)
        {
            violations.Add($"d5_byte_match.json romBytes={romBytes} (expected > 0).");
        }

        if (TryGetPropertyIgnoreCase(root, "binaries", out JsonElement binsEl) && binsEl.ValueKind == JsonValueKind.Array)
        {
            int count = 0;
            foreach (JsonElement bin in binsEl.EnumerateArray())
            {
                count++;
                string? rel = TryGetString(bin, "relpath", "relativePath", "name", "path");
                if (string.IsNullOrWhiteSpace(rel) || rel.Contains("..", StringComparison.Ordinal))
                {
                    violations.Add($"d5_byte_match.json binary entry #{count} has invalid relpath '{rel}'.");
                }
                if (TryGetInt(bin, out int binMismatch, "mismatchedBytes") && binMismatch != 0)
                {
                    violations.Add($"d5_byte_match.json binary '{rel}' has mismatchedBytes={binMismatch}.");
                }
                if (TryGetLong(bin, out long bytes, "bytes", "fileSizeBytes") && bytes <= 0)
                {
                    violations.Add($"d5_byte_match.json binary '{rel}' has invalid bytes={bytes}.");
                }
                string? sha = TryGetString(bin, "sha256", "sha256Hex");
                if (sha is not null && !Sha256HexRegex().IsMatch(sha))
                {
                    violations.Add($"d5_byte_match.json binary '{rel}' has malformed sha256 '{sha}'.");
                }
            }
            if (matchedBinaries > 0 && count < matchedBinaries)
            {
                violations.Add($"d5_byte_match.json binaries count ({count}) < matchedBinaries ({matchedBinaries}).");
            }
        }

        bool ok = violations.Count == 0;
        string summary = ok
            ? $"d5_byte_match.json verified ({matchedBinaries} binary match(es), 0 mismatched bytes)."
            : $"d5_byte_match.json validation failed ({violations.Count} violation(s)): {string.Join("; ", violations)}";
        return new(ok, matchedBinaries, mismatchedBytes, romBytes, violations, summary);
    }

    internal static int ParseTierNumber(string? tier)
    {
        if (string.IsNullOrWhiteSpace(tier) || string.Equals(tier, "none", StringComparison.OrdinalIgnoreCase)) return -1;
        return Array.FindIndex(ValidDecompTiers, t => string.Equals(t, tier.Trim(), StringComparison.OrdinalIgnoreCase)) - 1;
    }

    internal static bool IsValidTierToken(string? tier) =>
        !string.IsNullOrWhiteSpace(tier) && ValidDecompTiers.Contains(tier.Trim(), StringComparer.OrdinalIgnoreCase);
}
