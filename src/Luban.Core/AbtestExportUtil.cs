// Copyright 2025 Code Philosophy
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using Luban.Datas;
using Luban.Defs;
using Luban.Types;

namespace Luban;

public sealed class AbtestExportOptions
{
    public bool Enable { get; init; }

    public string VersionFieldName { get; init; }

    public string OutputDataDir { get; init; }

    public bool CleanUpOutputDir { get; init; }
}

public static class AbtestExportUtil
{
    public const string OutputPrefix = "__abtest__/";

    public static AbtestExportOptions GetOptions()
    {
        return new AbtestExportOptions
        {
            Enable = EnvManager.Current.GetBoolOptionOrDefault(BuiltinOptionNames.AbtestFamily, BuiltinOptionNames.AbtestEnable, false, false),
            VersionFieldName = EnvManager.Current.GetOptionOrDefault(BuiltinOptionNames.AbtestFamily, BuiltinOptionNames.AbtestVersionField, false, "version"),
            OutputDataDir = EnvManager.Current.GetOptionOrDefault(BuiltinOptionNames.AbtestFamily, BuiltinOptionNames.AbtestOutputDataDir, false, ""),
            CleanUpOutputDir = EnvManager.Current.GetBoolOptionOrDefault(BuiltinOptionNames.AbtestFamily, BuiltinOptionNames.AbtestCleanUpOutputDir, false, true),
        };
    }

    public static bool IsEnabled()
    {
        return GetOptions().Enable;
    }

    public static bool TryGetVersionField(DefTable table, out DefField field, out int fieldIndex)
    {
        var options = GetOptions();
        foreach (var candidate in GetVersionFieldCandidates(options.VersionFieldName))
        {
            if (table.ValueTType.DefBean.TryGetField(candidate, out field, out fieldIndex))
            {
                if (field.CType is not TString)
                {
                    throw new Exception($"table:'{table.FullName}' field:'{field.Name}' must be string when abtest export is enabled");
                }
                return true;
            }
        }
        field = null;
        fieldIndex = -1;
        return false;
    }

    private static IEnumerable<string> GetVersionFieldCandidates(string configuredFieldName)
    {
        if (!string.IsNullOrWhiteSpace(configuredFieldName))
        {
            yield return configuredFieldName.Trim();
        }
        if (string.Equals(configuredFieldName, "version", StringComparison.OrdinalIgnoreCase))
        {
            yield return "ver";
        }
    }

    public static string NormalizeVersionValue(DefTable table, Record record, int fieldIndex)
    {
        if (fieldIndex < 0)
        {
            return "";
        }

        if (record.Data.Fields[fieldIndex] is not DString version)
        {
            throw new Exception($"table:'{table.FullName}' record:'{record.Source}' abtest version field must be string");
        }

        string normalized = version.Value.Trim();
        if (normalized.Length == 0)
        {
            return "";
        }

        ValidateVersionValue(table, record, normalized);
        return normalized;
    }

    public static void ValidateVersionValue(DefTable table, Record record, string version)
    {
        if (version == "." || version == "..")
        {
            throw new Exception($"table:'{table.FullName}' record:'{record.Source}' has invalid abtest version '{version}'");
        }
        if (version.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new Exception($"table:'{table.FullName}' record:'{record.Source}' has invalid abtest version '{version}'");
        }
    }

    public static string GetFormatDirName(string dataTargetName)
    {
        return EnvManager.Current.GetOptionOrDefault(BuiltinOptionNames.AbtestFamily, $"dirNameMap.{dataTargetName}", false, dataTargetName switch
        {
            "bin" => "Bytes",
            "json" => "Json",
            _ => dataTargetName,
        });
    }

    public static string BuildOutputFilePath(string version, string dataTargetName, string originFilePath)
    {
        return $"{OutputPrefix}{version}/{GetFormatDirName(dataTargetName)}/{originFilePath}";
    }

    public static bool IsAbtestOutputFile(string path)
    {
        return path.StartsWith(OutputPrefix, StringComparison.Ordinal);
    }

    public static string StripAbtestPrefix(string path)
    {
        return path[OutputPrefix.Length..];
    }

    public static string GetRequiredOutputDataDir()
    {
        string dir = GetOptions().OutputDataDir;
        if (string.IsNullOrWhiteSpace(dir))
        {
            throw new Exception($"option '{BuiltinOptionNames.AbtestFamily}.{BuiltinOptionNames.AbtestOutputDataDir}' is required when abtest export emits incremental files");
        }
        return dir;
    }
}
