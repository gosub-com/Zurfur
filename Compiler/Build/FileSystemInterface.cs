using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Zurfur.Build;

/// <summary>
/// The build system uses this interface to interact with the OS.
/// </summary>
public interface FileSystemInterface
{
    Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default);
    Task WriteAllLinesAsync(string path, IEnumerable<string> contents, CancellationToken cancellationToken = default);
}
