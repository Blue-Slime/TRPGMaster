using System.Collections.Concurrent;
using System.Text.Json;

namespace MapEngine.Core.Assets;

public sealed class AssetDatabase
{
    private readonly ConcurrentDictionary<Guid, string> _guidToPath = new();
    private readonly ConcurrentDictionary<string, Guid> _pathToGuid = new(StringComparer.OrdinalIgnoreCase);
    private string _rootPath = string.Empty;

    public string RootPath => _rootPath;
    public int Count => _guidToPath.Count;

    public void Initialize(string rootPath)
    {
        _rootPath = Path.GetFullPath(rootPath);
        _guidToPath.Clear();
        _pathToGuid.Clear();
        ScanDirectory(_rootPath);
    }

    public Guid GetOrAssignGuid(string assetPath)
    {
        var fullPath = Path.GetFullPath(assetPath);
        if (_pathToGuid.TryGetValue(fullPath, out var existing))
            return existing;

        var metaPath = fullPath + ".meta";
        if (File.Exists(metaPath))
        {
            var meta = ReadMeta(metaPath);
            if (meta.HasValue)
            {
                Register(meta.Value, fullPath);
                return meta.Value;
            }
        }

        var guid = Guid.NewGuid();
        WriteMeta(metaPath, guid);
        Register(guid, fullPath);
        return guid;
    }

    public string? ResolvePath(Guid guid)
        => _guidToPath.TryGetValue(guid, out var path) ? path : null;

    public Guid? ResolveGuid(string assetPath)
    {
        var fullPath = Path.GetFullPath(assetPath);
        return _pathToGuid.TryGetValue(fullPath, out var guid) ? guid : null;
    }

    public void NotifyMoved(string oldPath, string newPath)
    {
        var oldFull = Path.GetFullPath(oldPath);
        var newFull = Path.GetFullPath(newPath);

        if (!_pathToGuid.TryRemove(oldFull, out var guid))
            return;

        _guidToPath[guid] = newFull;
        _pathToGuid[newFull] = guid;

        var oldMeta = oldFull + ".meta";
        var newMeta = newFull + ".meta";
        if (File.Exists(oldMeta))
        {
            try { File.Move(oldMeta, newMeta, overwrite: true); } catch { }
        }
        else
        {
            WriteMeta(newMeta, guid);
        }
    }

    public void NotifyDeleted(string assetPath)
    {
        var fullPath = Path.GetFullPath(assetPath);
        if (_pathToGuid.TryRemove(fullPath, out var guid))
            _guidToPath.TryRemove(guid, out _);
    }

    private void ScanDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return;

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                continue;

            var metaPath = file + ".meta";
            if (File.Exists(metaPath))
            {
                var guid = ReadMeta(metaPath);
                if (guid.HasValue)
                {
                    Register(guid.Value, file);
                    continue;
                }
            }

            var newGuid = Guid.NewGuid();
            WriteMeta(metaPath, newGuid);
            Register(newGuid, file);
        }
    }

    private void Register(Guid guid, string fullPath)
    {
        _guidToPath[guid] = fullPath;
        _pathToGuid[fullPath] = guid;
    }

    private static Guid? ReadMeta(string metaPath)
    {
        try
        {
            var json = File.ReadAllText(metaPath);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("guid", out var prop))
            {
                var str = prop.GetString();
                if (Guid.TryParse(str, out var guid))
                    return guid;
            }
        }
        catch { }
        return null;
    }

    private static void WriteMeta(string metaPath, Guid guid)
    {
        try
        {
            var json = JsonSerializer.Serialize(new { guid = guid.ToString("D") });
            File.WriteAllText(metaPath, json);
        }
        catch { }
    }
}
