using System.IO;
using System.Text;
using System.Text.Json;

namespace DocTwin.Persistence;

internal static class LocalJsonFile
{
    public static T? Read<T>(string path, int maximumBytes, JsonSerializerOptions? options = null)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > maximumBytes)
            throw new InvalidDataException("Local settings exceed their storage safety limit. No file was overwritten.");
        return JsonSerializer.Deserialize<T>(stream, options);
    }

    public static IDisposable AcquireWriteLock(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
            1, FileOptions.DeleteOnClose);
    }

    public static void Write(string path, string json, int maximumBytes)
    {
        if (Encoding.UTF8.GetByteCount(json) > maximumBytes)
            throw new InvalidDataException("Local settings would exceed their storage safety limit. No file was overwritten.");
        string pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(Encoding.UTF8.GetBytes(json));
                stream.Flush(flushToDisk: true);
            }
            File.Move(pending, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(pending)) File.Delete(pending);
        }
    }
}
