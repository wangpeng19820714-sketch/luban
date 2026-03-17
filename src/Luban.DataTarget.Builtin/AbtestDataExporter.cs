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
using Luban.Defs;
using System.Reflection;

namespace Luban.DataExporter.Builtin;

[DataExporter("abtest")]
public class AbtestDataExporter : DataExporterBase
{
    private static string GetDataTargetName(IDataTarget dataTarget)
    {
        return dataTarget.GetType().GetCustomAttribute<DataTargetAttribute>()?.Name
            ?? throw new Exception($"dataTarget:{dataTarget.GetType().FullName} missing DataTargetAttribute");
    }

    public override void Handle(GenerationContext ctx, IDataTarget dataTarget, OutputFileManifest manifest)
    {
        base.Handle(ctx, dataTarget, manifest);
        if (!AbtestExportUtil.IsEnabled())
        {
            return;
        }

        if (dataTarget.AggregationType != AggregationType.Table)
        {
            throw new NotSupportedException($"abtest exporter only supports table aggregation. dataTarget:{GetDataTargetName(dataTarget)}");
        }

        string dataTargetName = GetDataTargetName(dataTarget);
        List<DefTable> tables = dataTarget.ExportAllRecords ? ctx.Tables : ctx.ExportTables;
        foreach (var table in tables)
        {
            var tableInfo = ctx.GetTableDataInfo(table);
            foreach (var kv in tableInfo.AbtestRecordsByVersion)
            {
                var output = dataTarget.ExportTable(table, kv.Value);
                manifest.AddFile(new OutputFile
                {
                    File = AbtestExportUtil.BuildOutputFilePath(kv.Key, dataTargetName, output.File),
                    Content = output.Content,
                    Encoding = output.Encoding,
                });
            }
        }
    }
}
