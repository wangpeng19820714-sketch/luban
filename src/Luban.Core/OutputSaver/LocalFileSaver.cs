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

using Luban.Utils;

namespace Luban.OutputSaver;

[OutputSaver("local")]
public class LocalFileSaver : OutputSaverBase
{
    private static readonly NLog.Logger s_logger = NLog.LogManager.GetCurrentClassLogger();

    private sealed class CleanupState
    {
        public bool Cleaned { get; set; }
    }

    protected override void BeforeSave(OutputFileManifest outputFileManifest, string outputDir)
    {
        bool cleanMainOutput = EnvManager.Current.GetBoolOptionOrDefault($"{BuiltinOptionNames.OutputSaver}.{outputFileManifest.TargetName}", BuiltinOptionNames.CleanUpOutputDir,
            true, true);

        var normalFiles = outputFileManifest.DataFiles.Where(f => !AbtestExportUtil.IsAbtestOutputFile(f.File)).Select(f => f.File).ToList();
        if (cleanMainOutput)
        {
            FileCleaner.Clean(outputDir, normalFiles);
        }

        var abtestFiles = outputFileManifest.DataFiles.Where(f => AbtestExportUtil.IsAbtestOutputFile(f.File))
            .Select(f => AbtestExportUtil.StripAbtestPrefix(f.File))
            .ToList();
        if (abtestFiles.Count == 0 || !AbtestExportUtil.GetOptions().CleanUpOutputDir)
        {
            return;
        }

        string abtestOutputDir = AbtestExportUtil.GetRequiredOutputDataDir();
        string cleanupKey = $"abtest.cleanup.{abtestOutputDir}";
        var cleanupState = (CleanupState)GenerationContext.Current.GetOrAddUniqueObject(cleanupKey, () => new CleanupState());
        lock (cleanupState)
        {
            if (cleanupState.Cleaned)
            {
                return;
            }
            FileCleaner.Clean(abtestOutputDir, abtestFiles);
            cleanupState.Cleaned = true;
        }
    }

    public override void SaveFile(OutputFileManifest fileManifest, string outputDir, OutputFile outputFile)
    {
        string relativePath = outputFile.File;
        if (AbtestExportUtil.IsAbtestOutputFile(relativePath))
        {
            outputDir = AbtestExportUtil.GetRequiredOutputDataDir();
            relativePath = AbtestExportUtil.StripAbtestPrefix(relativePath);
        }

        string fullOutputPath = $"{outputDir}/{relativePath}";
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath));
        string tag = File.Exists(fullOutputPath) ? "overwrite" : "new";
        if (FileUtil.WriteAllBytes(fullOutputPath, outputFile.GetContentBytes()))
        {
            s_logger.Info("[{0}] {1} ", tag, fullOutputPath);
        }
    }
}
