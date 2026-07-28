using System.Text.Json;
using System.Text.Json.Serialization;
using MapEngine.Core.Data;

namespace MapEngine.Core.Components;

public static class SceneSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(SceneDocument scene)
        => JsonSerializer.Serialize(scene, Options);

    public static SceneDocument? Deserialize(string json)
        => JsonSerializer.Deserialize<SceneDocument>(json, Options);

    public static SceneDocument ToDocument(IEnumerable<GameObject> roots)
    {
        return new SceneDocument
        {
            Version = 1,
            Objects = roots.Select(ToObjectData).ToList()
        };
    }

    public static List<GameObject> FromDocument(SceneDocument doc)
        => doc.Objects.Select(FromObjectData).ToList();

    private static GameObjectData ToObjectData(GameObject go)
    {
        var data = new GameObjectData
        {
            Id = go.Id.ToString(),
            Name = go.Name,
            Icon = go.Icon,
            ObjectType = go.ObjectType,
            IsActive = go.IsActive,
            IsLocked = go.IsLocked,
            SortOrder = go.SortOrder,
            Tags = go.Tags.Count > 0 ? go.Tags : null,
            Components = go.Components.Select(ToComponentData).ToList(),
            Children = go.Children.Count > 0
                ? go.Children.Select(ToObjectData).ToList()
                : null
        };
        return data;
    }

    private static ComponentData ToComponentData(IComponent component)
    {
        var props = new Dictionary<string, object?>();
        switch (component)
        {
            case TransformComponent t:
                props["x"] = t.X;
                props["y"] = t.Y;
                props["z"] = t.Z;
                props["rotation"] = t.Rotation;
                props["scaleX"] = t.ScaleX;
                props["scaleY"] = t.ScaleY;
                props["hasMapPosition"] = t.HasMapPosition;
                break;
            case SpriteRendererComponent s:
                props["spriteColor"] = s.Color;
                props["opacity"] = s.Opacity;
                props["sourceAssetPath"] = s.SourceAssetPath;
                props["sourceAssetKind"] = s.SourceAssetKind;
                props["sourceAssetName"] = s.SourceAssetName;
                props["alignX"] = s.AlignX;
                props["alignY"] = s.AlignY;
                props["hitTestEnabled"] = s.HitTestEnabled;
                break;
            case VisionComponent v:
                props["enabled"] = v.Enabled;
                props["radius"] = v.Radius;
                props["orientation"] = v.Orientation;
                if (v.VisionCones.Count > 0)
                    props["visionCones"] = v.VisionCones;
                break;
            case WallComponent w:
                props["x1"] = w.X1;
                props["y1"] = w.Y1;
                props["x2"] = w.X2;
                props["y2"] = w.Y2;
                props["sight"] = (int)w.Sight;
                props["move"] = (int)w.Move;
                props["sound"] = (int)w.Sound;
                props["light"] = (int)w.Light;
                props["dir"] = (int)w.Dir;
                props["door"] = (int)w.Door;
                props["state"] = (int)w.State;
                props["thickness"] = w.Thickness;
                if (!string.IsNullOrEmpty(w.TileTexturePath))
                    props["tileTexturePath"] = w.TileTexturePath;
                props["noCutaway"] = w.NoCutaway;
                break;
            case TokenComponent tk:
                props["tokenName"] = tk.TokenName;
                props["initiativeOrder"] = tk.InitiativeOrder;
                props["isPlayerControlled"] = tk.IsPlayerControlled;
                props["movementSpeed"] = tk.MovementSpeed;
                break;
        }
        return new ComponentData { Type = component.TypeName, Properties = props };
    }

    private static GameObject FromObjectData(GameObjectData data)
    {
        var go = new GameObject
        {
            Id = Guid.TryParse(data.Id, out var id) ? id : Guid.NewGuid(),
            Name = data.Name ?? "GameObject",
            Icon = data.Icon ?? "📦",
            ObjectType = data.ObjectType ?? "Empty",
            IsActive = data.IsActive,
            IsLocked = data.IsLocked,
            SortOrder = data.SortOrder,
            Tags = data.Tags ?? []
        };

        foreach (var cd in data.Components)
        {
            var component = CreateComponent(cd);
            if (component is not null)
                go.AddComponent(component);
        }

        if (data.Children is not null)
        {
            foreach (var childData in data.Children)
            {
                var child = FromObjectData(childData);
                child.Parent = go;
                go.Children.Add(child);
            }
        }

        return go;
    }

    private static IComponent? CreateComponent(ComponentData data)
    {
        return data.Type switch
        {
            "Transform" => new TransformComponent
            {
                X = GetDouble(data, "x"),
                Y = GetDouble(data, "y"),
                Z = GetDouble(data, "z"),
                Rotation = GetDouble(data, "rotation"),
                ScaleX = GetDouble(data, "scaleX", 1),
                ScaleY = GetDouble(data, "scaleY", 1),
                HasMapPosition = GetBool(data, "hasMapPosition")
            },
            "SpriteRenderer" => new SpriteRendererComponent
            {
                Color = GetString(data, "spriteColor", "#FF4444"),
                Opacity = GetDouble(data, "opacity", 1),
                SourceAssetPath = GetString(data, "sourceAssetPath"),
                SourceAssetKind = GetString(data, "sourceAssetKind"),
                SourceAssetName = GetString(data, "sourceAssetName"),
                AlignX = (int)GetDouble(data, "alignX", 1),
                AlignY = (int)GetDouble(data, "alignY", 1),
                HitTestEnabled = GetBool(data, "hitTestEnabled", true)
            },
            "Vision" => new VisionComponent
            {
                Enabled = GetBool(data, "enabled"),
                Radius = GetDouble(data, "radius", 60),
                Orientation = GetDouble(data, "orientation"),
                VisionCones = GetVisionCones(data)
            },
            "Wall" => new WallComponent
            {
                X1 = GetDouble(data, "x1"),
                Y1 = GetDouble(data, "y1"),
                X2 = GetDouble(data, "x2"),
                Y2 = GetDouble(data, "y2"),
                Sight = (SenseLevel)(int)GetDouble(data, "sight", 20),
                Move  = (SenseLevel)(int)GetDouble(data, "move",  20),
                Sound = (SenseLevel)(int)GetDouble(data, "sound", 20),
                Light = (SenseLevel)(int)GetDouble(data, "light", 20),
                Dir   = (WallDir)(int)GetDouble(data, "dir"),
                Door  = (DoorKind)(int)GetDouble(data, "door"),
                State = (DoorState)(int)GetDouble(data, "state"),
                Thickness = GetDouble(data, "thickness", 5),
                TileTexturePath = data.Properties.TryGetValue("tileTexturePath", out var tp)
                    ? tp is JsonElement te ? te.GetString() : tp as string
                    : null,
                NoCutaway = GetBool(data, "noCutaway")
            },
            "Token" => new TokenComponent
            {
                TokenName = GetString(data, "tokenName"),
                InitiativeOrder = (int)GetDouble(data, "initiativeOrder"),
                IsPlayerControlled = GetBool(data, "isPlayerControlled"),
                MovementSpeed = GetDouble(data, "movementSpeed", 30)
            },
            _ => null
        };
    }

    private static List<VisionConeData> GetVisionCones(ComponentData data)
    {
        if (!data.Properties.TryGetValue("visionCones", out var raw)) return [];
        if (raw is not JsonElement el || el.ValueKind != JsonValueKind.Array) return [];
        var result = new List<VisionConeData>();
        foreach (var item in el.EnumerateArray())
        {
            result.Add(new VisionConeData
            {
                Id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : Guid.NewGuid().ToString("N"),
                Name = item.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "视野锥" : "视野锥",
                CenterOffset = item.TryGetProperty("centerOffset", out var coEl) && coEl.TryGetDouble(out var co) ? co : 0,
                Range = item.TryGetProperty("range", out var rEl) && rEl.TryGetDouble(out var r) ? r : 12,
                FieldOfView = item.TryGetProperty("fieldOfView", out var fovEl) && fovEl.TryGetDouble(out var fov) ? fov : 90,
                IsEnabled = item.TryGetProperty("isEnabled", out var enEl) && enEl.ValueKind == JsonValueKind.True
            });
        }
        return result;
    }

    private static double GetDouble(ComponentData data, string key, double fallback = 0)
    {
        if (data.Properties.TryGetValue(key, out var val) && val is JsonElement el)
            return el.TryGetDouble(out var d) ? d : fallback;
        if (val is double dv) return dv;
        return fallback;
    }

    private static bool GetBool(ComponentData data, string key, bool fallback = false)
    {
        if (data.Properties.TryGetValue(key, out var val) && val is JsonElement el)
            return el.ValueKind == JsonValueKind.True;
        if (val is bool bv) return bv;
        return fallback;
    }

    private static string GetString(ComponentData data, string key, string fallback = "")
    {
        if (data.Properties.TryGetValue(key, out var val) && val is JsonElement el)
            return el.GetString() ?? fallback;
        if (val is string sv) return sv;
        return fallback;
    }
}

public sealed class SceneDocument
{
    public int Version { get; set; } = 1;
    public List<GameObjectData> Objects { get; set; } = [];
}

public sealed class GameObjectData
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Icon { get; set; }
    public string? ObjectType { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsLocked { get; set; }
    public int SortOrder { get; set; }
    public List<string>? Tags { get; set; }
    public List<ComponentData> Components { get; set; } = [];
    public List<GameObjectData>? Children { get; set; }
}

public sealed class ComponentData
{
    public string Type { get; set; } = string.Empty;
    public Dictionary<string, object?> Properties { get; set; } = [];
}
