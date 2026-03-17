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

using Luban.DataTarget;
using Luban.Datas;
using Luban.Defs;
using Luban.Types;

namespace Luban.L10N.DataTarget;

[DataTarget("text-list")]
internal class TextKeyListDataTarget : DataTargetBase
{
    protected override string DefaultOutputFileExt => "txt";

    public override bool ExportAllRecords => true;

    public override AggregationType AggregationType => AggregationType.Tables;

    public override OutputFile ExportTable(DefTable table, List<Record> records)
    {
        throw new NotImplementedException();
    }

    public override OutputFile ExportTables(List<DefTable> tables)
    {
        var textCollection = new TextKeyCollection();
        foreach (var table in tables)
        {
            CollectTableTextEntries(table, textCollection);
        }

        var lines = textCollection.Entries
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}\t{EscapeTextValue(kv.Value)}");
        var content = string.Join("\n", lines);

        string outputFile = EnvManager.Current.GetOption(BuiltinOptionNames.L10NFamily, BuiltinOptionNames.L10NTextListFile, false);

        return CreateOutputFile(outputFile, content);
    }

    private static void CollectTableTextEntries(DefTable table, TextKeyCollection textCollection)
    {
        var records = GenerationContext.Current.GetTableAllDataList(table);
        TryGetVersionField(table, out int versionFieldIndex);
        for (int i = 0; i < records.Count; i++)
        {
            var record = records[i];
            string version = GetVersion(record, versionFieldIndex);
            string recordKey = BuildRecordKey(table, record, i);
            CollectDataTextEntries(textCollection, table.OutputDataFile, recordKey, version, record.Data, table.ValueTType, null);
        }
    }

    private static void CollectDataTextEntries(TextKeyCollection textCollection, string tableName, string recordKey, string version, DType data, TType type, string fieldPath)
    {
        switch (data)
        {
            case null:
                return;
            case DString textData when type.HasTag("text"):
                textCollection.AddEntry(BuildTextKey(tableName, fieldPath, version, recordKey), textData.Value);
                return;
            case DBean beanData when type is TBean beanType:
            {
                var fields = beanData.ImplType.HierarchyFields;
                for (int i = 0; i < beanData.Fields.Count; i++)
                {
                    var fieldValue = beanData.Fields[i];
                    if (fieldValue == null)
                    {
                        continue;
                    }
                    var field = fields[i];
                    string childPath = string.IsNullOrEmpty(fieldPath) ? field.Name : $"{fieldPath}_{field.Name}";
                    CollectDataTextEntries(textCollection, tableName, recordKey, version, fieldValue, field.CType, childPath);
                }
                return;
            }
            case DArray arrayData:
                foreach (var element in arrayData.Datas)
                {
                    CollectDataTextEntries(textCollection, tableName, recordKey, version, element, type.ElementType, fieldPath);
                }
                return;
            case DList listData:
                foreach (var element in listData.Datas)
                {
                    CollectDataTextEntries(textCollection, tableName, recordKey, version, element, type.ElementType, fieldPath);
                }
                return;
            case DSet setData:
                foreach (var element in setData.Datas)
                {
                    CollectDataTextEntries(textCollection, tableName, recordKey, version, element, type.ElementType, fieldPath);
                }
                return;
            case DMap mapData when type is TMap mapType:
                foreach (var kv in mapData.DataMap)
                {
                    CollectDataTextEntries(textCollection, tableName, recordKey, version, kv.Key, mapType.KeyType, AppendFieldPath(fieldPath, "key"));
                    CollectDataTextEntries(textCollection, tableName, recordKey, version, kv.Value, mapType.ValueType, AppendFieldPath(fieldPath, "value"));
                }
                return;
            default:
                return;
        }
    }

    private static string BuildTextKey(string tableName, string fieldPath, string version, string recordKey)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            throw new Exception($"table:'{tableName}' text field path is empty");
        }
        return string.IsNullOrWhiteSpace(version)
            ? $"{SanitizeKeySegment(tableName)}_{SanitizeKeySegment(fieldPath)}_{SanitizeKeySegment(recordKey)}"
            : $"{SanitizeKeySegment(tableName)}_{SanitizeKeySegment(fieldPath)}_{SanitizeKeySegment(version)}_{SanitizeKeySegment(recordKey)}";
    }

    private static string AppendFieldPath(string fieldPath, string segment)
    {
        return string.IsNullOrWhiteSpace(fieldPath) ? segment : $"{fieldPath}_{segment}";
    }

    private static string BuildRecordKey(DefTable table, Record record, int rowIndex)
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

    private static bool TryGetVersionField(DefTable table, out int fieldIndex)
    {
        if (table.ValueTType.DefBean.TryGetField("ver", out var field, out fieldIndex)
            || table.ValueTType.DefBean.TryGetField("version", out field, out fieldIndex))
        {
            if (field.CType is not TString)
            {
                throw new Exception($"table:'{table.FullName}' field:'{field.Name}' must be string when generating text-list");
            }
            return true;
        }
        fieldIndex = -1;
        return false;
    }

    private static string GetVersion(Record record, int fieldIndex)
    {
        if (fieldIndex < 0)
        {
            return string.Empty;
        }
        return record.Data.Fields[fieldIndex] is DString value ? value.Value?.Trim() ?? string.Empty : string.Empty;
    }

    private static string SanitizeKeySegment(string segment)
    {
        return (segment ?? string.Empty)
            .Trim()
            .Replace('\r', '_')
            .Replace('\n', '_')
            .Replace('\t', '_')
            .Replace(' ', '_');
    }

    private static string EscapeTextValue(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\t", "\\t")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    private static string ConvertKeyValueToString(DType value)
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
