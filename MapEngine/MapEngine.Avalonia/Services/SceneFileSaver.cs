using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MapEngine.Avalonia.Services;

/// <summary>
/// 把 HierarchyNodeDto 列表保存到 .scene 文件（VM-friendly 格式）。
/// 与 SceneFileLoader 配合：未来 Loader 可识别 "hierarchyDtoFormat" 标记进行反序列化。
/// </summary>
public static class SceneFileSaver
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    public static void SaveScene(string path, ScenePersistDocument document)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(document, Options);
        File.WriteAllText(path, json);
    }

    public static string ResolveDefaultScenePath(string projectRoot)
    {
        return Path.Combine(projectRoot, "Data", "scene.json");
    }
}

/// <summary>
/// 场景持久化文档：VM-friendly 格式。version 标记区别于 SceneDocument(GameObject)。
/// </summary>
public sealed class ScenePersistDocument
{
    public string Format { get; set; } = "hierarchy-v1";
    public int Version { get; set; } = 1;
    public string PackageName { get; set; } = string.Empty;
    public bool ShowGrid { get; set; } = true;
    public int ZoomPercent { get; set; } = 100;
    public int FeetPerCell { get; set; } = 5;
    public List<HierarchyNodeDto> HierarchyRoots { get; set; } = [];
}
