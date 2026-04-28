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

using Luban.DataLoader;
using Luban.Datas;
using Luban.Defs;
using Luban.RawDefs;
using Luban.Types;
using Luban.Utils;

namespace Luban.L10N;

[TextProvider("default")]
public class DefaultTextProvider : ITextProvider
{
    private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

    private const string LookupModeValue = "value";
    private const string LookupModeCompositeKey = "compositeKey";

    private string _keyFieldName;
    private string _ValueFieldName;

    private bool _convertTextKeyToValue;
    private string _lookupMode = LookupModeValue;

    private readonly Dictionary<string, string> _texts = new();

    private readonly HashSet<string> _unknownTextKeys = new();

    public void Load()
    {
        EnvManager env = EnvManager.Current;

        _keyFieldName = env.GetOptionOrDefault(BuiltinOptionNames.L10NFamily, BuiltinOptionNames.L10NTextFileKeyFieldName, false, "");
        if (string.IsNullOrWhiteSpace(_keyFieldName))
        {
            throw new Exception($"'-x {BuiltinOptionNames.L10NFamily}.{BuiltinOptionNames.L10NTextFileKeyFieldName}=xxx' missing");
        }

        _convertTextKeyToValue = DataUtil.ParseBool(env.GetOptionOrDefault(BuiltinOptionNames.L10NFamily, BuiltinOptionNames.L10NConvertTextKeyToValue, false, "false"));
        if (_convertTextKeyToValue)
        {
            _ValueFieldName = env.GetOptionOrDefault(BuiltinOptionNames.L10NFamily, BuiltinOptionNames.L10NTextFileLanguageFieldName, false, "");
            if (string.IsNullOrWhiteSpace(_ValueFieldName))
            {
                throw new Exception($"'-x {BuiltinOptionNames.L10NFamily}.{BuiltinOptionNames.L10NTextFileLanguageFieldName}=xxx' missing");
            }
        }

        _lookupMode = (env.GetOptionOrDefault(BuiltinOptionNames.L10NFamily, BuiltinOptionNames.L10NTextFileLookupMode, false, LookupModeValue) ?? LookupModeValue).Trim();
        if (_lookupMode != LookupModeValue && _lookupMode != LookupModeCompositeKey)
        {
            throw new Exception($"'-x {BuiltinOptionNames.L10NFamily}.{BuiltinOptionNames.L10NTextFileLookupMode}' must be '{LookupModeValue}' or '{LookupModeCompositeKey}'");
        }

        string textProviderFile = env.GetOption(BuiltinOptionNames.L10NFamily, BuiltinOptionNames.L10NTextFilePath, false);
        LoadTextListFromFile(textProviderFile);
    }

    public bool ConvertTextKeyToValue => _convertTextKeyToValue;

    public bool IsValidKey(string key)
    {
        return _texts.ContainsKey(key);
    }

    public bool TryGetText(string key, out string text)
    {
        return _texts.TryGetValue(key, out text);
    }

    private void LoadTextListFromFile(string fileName)
    {
        var ass = new DefAssembly(new RawAssembly()
        {
            Targets = new List<RawTarget> { new() { Name = "default", Manager = "Tables" } },
        }, "default", new List<string>(), null, null);


        var rawFields = new List<RawField> { new() { Name = _keyFieldName, Type = "string" }, };
        if (_convertTextKeyToValue)
        {
            rawFields.Add(new() { Name = _ValueFieldName, Type = "string" });
        }
        var defTableRecordType = new DefBean(new RawBean()
        {
            Namespace = "__intern__",
            Name = "__TextInfo__",
            Parent = "",
            Alias = "",
            IsValueType = false,
            Sep = "",
            Fields = rawFields,
        })
        {
            Assembly = ass,
        };

        ass.AddType(defTableRecordType);
        defTableRecordType.PreCompile();
        defTableRecordType.Compile();
        defTableRecordType.PostCompile();
        var tableRecordType = TBean.Create(false, defTableRecordType, null);

        (var actualFile, var sheetName) = FileUtil.SplitFileAndSheetName(FileUtil.Standardize(fileName));
        var records = DataLoaderManager.Ins.LoadTableFile(tableRecordType, actualFile, sheetName, new Dictionary<string, string>());

        foreach (var r in records)
        {
            DBean data = r.Data;

            string key = ((DString)data.GetField(_keyFieldName)).Value;
            string value = _convertTextKeyToValue ? ((DString)data.GetField(_ValueFieldName)).Value : key;
            if (string.IsNullOrEmpty(key))
            {
                s_logger.Error("textFile:{} key:{} is empty. ignore it!", fileName, key);
                continue;
            }
            if (!_texts.TryAdd(key, value))
            {
                s_logger.Error("textFile:{} key:{} is duplicated", fileName, key);
            }
        }
        ;
    }

    public void AddUnknownKey(string key)
    {
        _unknownTextKeys.Add(key);
    }

    public void ProcessDatas()
    {
        if (!_convertTextKeyToValue)
        {
            return;
        }

        if (_lookupMode == LookupModeCompositeKey)
        {
            ProcessDatasByCompositeKey();
            return;
        }

        var trans = new TextKeyToValueTransformer(this);
        foreach (var table in GenerationContext.Current.Tables)
        {
            foreach (var record in GenerationContext.Current.GetTableAllDataList(table))
            {
                record.Data = (DBean)record.Data.Apply(trans, table.ValueTType);
            }
        }
    }

    private void ProcessDatasByCompositeKey()
    {
        foreach (var table in GenerationContext.Current.Tables)
        {
            var records = GenerationContext.Current.GetTableAllDataList(table);
            TextKeyBuilder.TryGetVersionField(table, out int versionFieldIndex);
            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                string version = TextKeyBuilder.GetVersion(record, versionFieldIndex);
                string recordKey = TextKeyBuilder.BuildRecordKey(table, record, i);
                record.Data = (DBean)TransformDataByCompositeKey(record.Data, table.ValueTType, table.OutputDataFile, recordKey, version, null);
            }
        }
    }

    private DType TransformDataByCompositeKey(DType data, TType type, string tableName, string recordKey, string version, string fieldPath)
    {
        switch (data)
        {
            case null:
                return null;
            case DString textData when type.HasTag("text"):
            {
                if (string.IsNullOrEmpty(textData.Value))
                {
                    return textData;
                }
                string compositeKey = TextKeyBuilder.BuildTextKey(tableName, fieldPath, version, recordKey);
                if (_texts.TryGetValue(compositeKey, out string text))
                {
                    return DString.ValueOf(type, text);
                }
                s_logger.Error("can't find target language text. table:'{}' fieldPath:'{}' recordKey:'{}' compositeKey:'{}' origin:'{}'",
                    tableName, fieldPath, recordKey, compositeKey, textData.Value);
                return textData;
            }
            case DBean beanData when type is TBean beanType:
            {
                var fields = beanData.ImplType.HierarchyFields;
                var newFields = new List<DType>(beanData.Fields.Count);
                bool changed = false;
                for (int i = 0; i < beanData.Fields.Count; i++)
                {
                    var fieldValue = beanData.Fields[i];
                    if (fieldValue == null)
                    {
                        newFields.Add(null);
                        continue;
                    }
                    var field = fields[i];
                    string childPath = TextKeyBuilder.AppendFieldPath(fieldPath, field.Name);
                    var newVal = TransformDataByCompositeKey(fieldValue, field.CType, tableName, recordKey, version, childPath);
                    if (!ReferenceEquals(newVal, fieldValue))
                    {
                        changed = true;
                    }
                    newFields.Add(newVal);
                }
                return changed ? new DBean(beanType, beanData.ImplType, newFields) : beanData;
            }
            case DArray arrayData when type is TArray arrayType:
            {
                var newList = new List<DType>(arrayData.Datas.Count);
                bool changed = false;
                foreach (var element in arrayData.Datas)
                {
                    var newVal = TransformDataByCompositeKey(element, arrayType.ElementType, tableName, recordKey, version, fieldPath);
                    if (!ReferenceEquals(newVal, element))
                    {
                        changed = true;
                    }
                    newList.Add(newVal);
                }
                return changed ? new DArray(arrayType, newList) : arrayData;
            }
            case DList listData when type is TList listType:
            {
                var newList = new List<DType>(listData.Datas.Count);
                bool changed = false;
                foreach (var element in listData.Datas)
                {
                    var newVal = TransformDataByCompositeKey(element, listType.ElementType, tableName, recordKey, version, fieldPath);
                    if (!ReferenceEquals(newVal, element))
                    {
                        changed = true;
                    }
                    newList.Add(newVal);
                }
                return changed ? new DList(listType, newList) : listData;
            }
            case DSet setData when type is TSet setType:
            {
                var newList = new List<DType>(setData.Datas.Count);
                bool changed = false;
                foreach (var element in setData.Datas)
                {
                    var newVal = TransformDataByCompositeKey(element, setType.ElementType, tableName, recordKey, version, fieldPath);
                    if (!ReferenceEquals(newVal, element))
                    {
                        changed = true;
                    }
                    newList.Add(newVal);
                }
                return changed ? new DSet(setType, newList) : setData;
            }
            case DMap mapData when type is TMap mapType:
            {
                var newMap = new Dictionary<DType, DType>(mapData.DataMap.Count);
                bool changed = false;
                foreach (var kv in mapData.DataMap)
                {
                    var newKey = TransformDataByCompositeKey(kv.Key, mapType.KeyType, tableName, recordKey, version, TextKeyBuilder.AppendFieldPath(fieldPath, "key"));
                    var newVal = TransformDataByCompositeKey(kv.Value, mapType.ValueType, tableName, recordKey, version, TextKeyBuilder.AppendFieldPath(fieldPath, "value"));
                    if (!ReferenceEquals(newKey, kv.Key) || !ReferenceEquals(newVal, kv.Value))
                    {
                        changed = true;
                    }
                    newMap.Add(newKey, newVal);
                }
                return changed ? new DMap(mapType, newMap) : mapData;
            }
            default:
                return data;
        }
    }
}
