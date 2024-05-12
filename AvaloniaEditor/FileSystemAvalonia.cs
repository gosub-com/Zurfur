using Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Zurfur.Build;

namespace AvaloniaEditor;

/// <summary>
/// Retrieve files from Avalonia resoure
/// </summary>
public class FileSystemAvalonia : FileSystemInterface
{
    Dictionary<string, string[]> mLocalFiles = new();

    async Task<string[]> FileSystemInterface.ReadAllLinesAsync(string path, CancellationToken cancellationToken)
    {
        if (mLocalFiles.TryGetValue(path, out var localFile))
            return localFile;

        using var sr = new StreamReader(AssetLoader.Open(new Uri(path)));
        var output = new List<string>();
        var line = sr.ReadLine();
        while (line != null)
        {
            output.Add(line);
            line = sr.ReadLine();
        }
        return output.ToArray();
    }

    async public Task WriteAllLinesAsync(string path, IEnumerable<string> contents, CancellationToken cancellationToken = default)
    {
        mLocalFiles[path] = contents.ToArray();
    }

}
