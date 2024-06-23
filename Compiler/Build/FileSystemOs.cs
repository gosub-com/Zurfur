using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Zurfur.Build;

/// <summary>
/// Pass through to the OS file system.
/// TBD: Move out of the compiler into separate library
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
