using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Zurfur.Build;

namespace Zurfur.Ide;

/// <summary>
/// Pass through to the OS file system
/// </summary>
public class FileSystemOs : FileSystemInterface
{
    Task<string[]> FileSystemInterface.ReadAllLinesAsync(string path, CancellationToken cancellationToken)
    {
        return File.ReadAllLinesAsync(path, cancellationToken);
    }

    public Task WriteAllLinesAsync(string path, IEnumerable<string> contents, CancellationToken cancellationToken = default)
    {
        return File.WriteAllLinesAsync(path, contents, cancellationToken);
    }

    /// <summary>
    /// Recursively retrieve all files under the directory
    /// </summary>
    public async Task<string[]> EnumerateAllFiles(string basePath, CancellationToken token = default)
    {
        return EnumerateAllFiles(basePath);
    }

    public static string[] EnumerateAllFiles(string basePath)
    {
        var dirs = new List<string>();
        EnumerateAllFiles(basePath, dirs);
        return dirs.ToArray();
    }

    static void EnumerateAllFiles(string path, List<string> result)
    {
        if (!Directory.Exists(path))
            return;
        foreach (var file in Directory.GetFiles(path))
            result.Add(file);
        foreach (var d in Directory.GetDirectories(path))
            EnumerateAllFiles(d, result);
    }

}
