using System;

using Zurfur.Build;


/// <summary>
/// Retrieve files from Avalonia resoure
/// </summary>
public class FileSystemAvalonia : FileSystemInterface
{
    async Task<string[]> FileSystemInterface.ReadAllLinesAsync(string path, CancellationToken cancellationToken)
    {
        using var sr = new StreamReader(AssetLoader.Open(new Uri(path)));
        var output = new List<string>();
        var line = await sr.ReadLineAsync();
        while (line != null)
        {
            output.Add(line);
            line = await sr.ReadLineAsync();
        }
        return output.ToArray();
    }
}
