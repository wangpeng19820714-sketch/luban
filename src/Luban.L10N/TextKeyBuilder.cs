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

namespace Luban.L10N;

/// <summary>
/// 统一的本地化合成 key 构造工具。
/// 规则: tablename_fieldname[_version]_recordkey
/// text-list 抽取 与 运行时翻译查表(compositeKey 模式) 必须严格使用同一套规则。
/// </summary>
internal static class TextKeyBuilder
{
    public static string BuildTextKey(string tableName, string fieldPath, string version, string recordKey)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            throw new Exception($"table:'{tableName}' text field path is empty");
        }
        return string.IsNullOrWhiteSpace(version)
            ? $"{SanitizeKeySegment(tableName)}_{SanitizeKeySegment(fieldPath)}_{SanitizeKeySegment(recordKey)}"
            : $"{SanitizeKeySegment(tableName)}_{SanitizeKeySegment(fieldPath)}_{SanitizeKeySegment(version)}_{SanitizeKeySegment(recordKey)}";
    }

    public static string AppendFieldPath(string fieldPath, string segment)
    {
        return string.IsNullOrWhiteSpace(fieldPath) ? segment : $"{fieldPath}_{segment}";
    }

    public static string BuildRecordKey(DefTable table, Record record, int rowIndex)
    {
        return table.Mode switch
        {
            TableMode.ONE => "one",
            TableMode.MAP => ConvertKeyValueToString(record.Data.Fields[table.IndexFieldIdIndex]),
            TableMode.LIST when table.IndexList.Count == 0 => $"row{rowIndex}",
            TableMode.LIST when table.IndexList.Count == 1 => ConvertKeyValueToString(record.Data.Fields[table.IndexList[0].IndexFieldIdIndex]),
            TableMode.LIST => string.Join("_", table.IndexList.Select(idx => $"{idx.IndexField.Name}={ConvertKeyValueToString(record.Data.Fields[idx.IndexFieldIdIndex])}")),
            _ => throw new Exception($"unknown table mode:{table.Mode}"),
        };
    }

    public static bool TryGetVersionField(DefTable table, out int fieldIndex)
    {
        if (table.ValueTType.DefBean.TryGetField("ver", out var field, out fieldIndex)
            || table.ValueTType.DefBean.TryGetField("version", out field, out fieldIndex))
        {
            if (field.CType is not TString)
            {
                throw new Exception($"table:'{table.FullName}' field:'{field.Name}' must be string when building text composite key");
            }
            return true;
        }
        fieldIndex = -1;
        return false;
    }

    public static string GetVersion(Record record, int fieldIndex)
    {
        if (fieldIndex < 0)
        {
            return string.Empty;
        }
        return record.Data.Fields[fieldIndex] is DString value ? value.Value?.Trim() ?? string.Empty : string.Empty;
    }

    public static string SanitizeKeySegment(string segment)
    {
        return (segment ?? string.Empty)
            .Trim()
            .Replace('\r', '_')
            .Replace('\n', '_')
            .Replace('\t', '_')
            .Replace(' ', '_');
    }

    public static string ConvertKeyValueToString(DType value)
    {
        return value switch
        {
            null => string.Empty,
            DString s => s.Value ?? string.Empty,
            DInt i => i.Value.ToString(),
            DLong l => l.Value.ToString(),
            DShort s => s.Value.ToString(),
            DByte b => b.Value.ToString(),
            DBool b => b.Value ? "true" : "false",
            DEnum e => e.ToString(),
            _ => value.ToString(),
        };
    }
}
