using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Zurfur.Build
{
    /// <summary>
    /// The build system uses this interface to interact with the OS.
    /// </summary>
    public interface FileSystemInterface
    {
        Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default);
        Task WriteAllLinesAsync(string path, IEnumerable<string> contents, CancellationToken cancellationToken = default);
    }

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
        public static List<string> EnumerateAllFiles(string basePath)
        {
            var dirs = new List<string>();
            EnumerateAllFiles(basePath, dirs);
            return dirs;
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
}
