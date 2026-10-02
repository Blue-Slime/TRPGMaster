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
                props["assetRef"] = s.AssetRef;
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
                props["currentHP"] = tk.CurrentHP;
                props["maxHP"] = tk.MaxHP;
                props["isInInitiativeTracker"] = tk.IsInInitiativeTracker;
                props["shape"] = tk.Shape;
                if (tk.Conditions.Count > 0)
                    props["conditions"] = tk.Conditions.Select(c => new ConditionData
                    {
                        Id = c.Id,
                        Name = c.Name,
                        Icon = c.Icon,
                        StackCount = c.StackCount,
                        RemainingRounds = c.RemainingRounds,
                        ColorHex = c.ColorHex
                    }).ToList();
                break;
            case ShapeComponent sh:
                props["shapeType"] = sh.ShapeType;
                props["width"] = sh.Width;
                props["height"] = sh.Height;
                props["x2"] = sh.X2;
                props["y2"] = sh.Y2;
                // ValueTuple 不被 System.Text.Json 序列化，投影成 PointData
                if (sh.Points.Count > 0)
                    props["points"] = sh.Points
                        .Select(p => new PointData { X = p.X, Y = p.Y })
                        .ToList();
                props["coneAngle"] = sh.ConeAngle;
                props["coneRadius"] = sh.ConeRadius;
                props["rotation"] = sh.Rotation;
                props["strokeColor"] = sh.StrokeColor;
                props["fillColor"] = sh.FillColor;
                props["strokeWidth"] = sh.StrokeWidth;
                props["isFilled"] = sh.IsFilled;
                props["strokeStyle"] = (int)sh.StrokeStyle;
                break;
            case TextComponent tx:
                props["text"] = tx.Text;
                props["fontSize"] = tx.FontSize;
                props["color"] = tx.Color;
                props["backgroundColor"] = tx.BackgroundColor;
                props["isBold"] = tx.IsBold;
                props["isItalic"] = tx.IsItalic;
                props["align"] = (int)tx.Align;
                break;
            case GraphNodeComponent gn:
                props["kind"] = (int)gn.Kind;
                props["displayName"] = gn.DisplayName;
                props["description"] = gn.Description;
                props["visibility"] = (int)gn.Visibility;
                props["renderMode"] = (int)gn.RenderMode;
                props["iconAssetRef"] = gn.IconAssetRef;
                props["color"] = gn.Color;
                props["size"] = gn.Size;
                props["shape"] = gn.Shape;
                break;
            case GraphLinkComponent gl:
                props["linkId"] = gl.LinkId;
                props["targetNodeId"] = gl.TargetNodeId;
                props["kind"] = (int)gl.Kind;
                props["isBidirectional"] = gl.IsBidirectional;
                props["label"] = gl.Label;
                props["visibility"] = (int)gl.Visibility;
                props["isPassable"] = gl.IsPassable;
                props["cost"] = gl.Cost;
                props["color"] = gl.Color;
                props["width"] = gl.Width;
                props["strokeStyle"] = (int)gl.StrokeStyle;
                break;
            case WallPathComponent wp:
                if (wp.Points.Count > 0)
                    props["points"] = wp.Points
                        .Select(p => new PointData { X = p.X, Y = p.Y })
                        .ToList();
                props["isClosed"] = wp.IsClosed;
                props["sight"] = (int)wp.Sight;
                props["move"] = (int)wp.Move;
                props["sound"] = (int)wp.Sound;
                props["light"] = (int)wp.Light;
                props["thickness"] = wp.Thickness;
                props["color"] = wp.Color;
                if (wp.Doors.Count > 0)
                    props["doors"] = wp.Doors.Select(d => new DoorSegmentData
                    {
                        Id = d.Id,
                        StartAnchorIndex = d.StartAnchorIndex,
                        EndAnchorIndex = d.EndAnchorIndex,
                        Kind = (int)d.Kind,
                        State = (int)d.State,
                        Swing = (int)d.Swing,
                        SightOverride = d.SightOverride.HasValue ? (int)d.SightOverride.Value : null,
                        MoveOverride = d.MoveOverride.HasValue ? (int)d.MoveOverride.Value : null
                    }).ToList();
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
                AssetRef = GetString(data, "assetRef"),
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
                MovementSpeed = GetDouble(data, "movementSpeed", 30),
                CurrentHP = (int)GetDouble(data, "currentHP", 100),
                MaxHP = (int)GetDouble(data, "maxHP", 100),
                IsInInitiativeTracker = GetBool(data, "isInInitiativeTracker", false),
                Shape = GetString(data, "shape", "Rectangle"),
                Conditions = GetConditions(data)
            },
            "Shape" => new ShapeComponent
            {
                ShapeType = GetString(data, "shapeType", "rect"),
                Width = GetDouble(data, "width", 60),
                Height = GetDouble(data, "height", 60),
                X2 = GetDouble(data, "x2"),
                Y2 = GetDouble(data, "y2"),
                Points = GetPoints(data),
                ConeAngle = GetDouble(data, "coneAngle", 30),
                ConeRadius = GetDouble(data, "coneRadius", 120),
                Rotation = GetDouble(data, "rotation"),
                StrokeColor = GetString(data, "strokeColor", "#845EF7"),
                FillColor = GetString(data, "fillColor", "#40845EF7"),
                StrokeWidth = GetDouble(data, "strokeWidth", 2),
                IsFilled = GetBool(data, "isFilled", true),
                StrokeStyle = (StrokeStyle)(int)GetDouble(data, "strokeStyle")
            },
            "Text" => new TextComponent
            {
                Text = GetString(data, "text"),
                FontSize = GetDouble(data, "fontSize", 16),
                Color = GetString(data, "color", "#F8F9FA"),
                BackgroundColor = GetString(data, "backgroundColor", "#A0000000"),
                IsBold = GetBool(data, "isBold"),
                IsItalic = GetBool(data, "isItalic"),
                Align = (TextAlign)(int)GetDouble(data, "align", 1)
            },
            "GraphNode" => new GraphNodeComponent
            {
                Kind = (GraphNodeKind)(int)GetDouble(data, "kind"),
                DisplayName = GetString(data, "displayName"),
                Description = GetString(data, "description"),
                Visibility = (GraphVisibility)(int)GetDouble(data, "visibility"),
                RenderMode = (GraphNodeRenderMode)(int)GetDouble(data, "renderMode", 1),
                IconAssetRef = GetString(data, "iconAssetRef"),
                Color = GetString(data, "color", "#4A90E2"),
                Size = GetDouble(data, "size", 48),
                Shape = GetString(data, "shape", "circle")
            },
            "GraphLink" => new GraphLinkComponent
            {
                // LinkId 缺失时补新的，保证每条边始终有稳定 ID
                LinkId = GetString(data, "linkId") is { Length: > 0 } lid ? lid : Guid.NewGuid().ToString("N"),
                TargetNodeId = GetString(data, "targetNodeId"),
                Kind = (GraphLinkKind)(int)GetDouble(data, "kind"),
                IsBidirectional = GetBool(data, "isBidirectional", true),
                Label = GetString(data, "label"),
                Visibility = (GraphVisibility)(int)GetDouble(data, "visibility"),
                IsPassable = GetBool(data, "isPassable", true),
                Cost = GetDouble(data, "cost", 1),
                Color = GetString(data, "color", "#8A8F98"),
                Width = GetDouble(data, "width", 2),
                StrokeStyle = (StrokeStyle)(int)GetDouble(data, "strokeStyle")
            },
            "WallPath" => new WallPathComponent
            {
                Points = GetPoints(data),
                IsClosed = GetBool(data, "isClosed"),
                Sight = (SenseLevel)(int)GetDouble(data, "sight", 20),
                Move = (SenseLevel)(int)GetDouble(data, "move", 20),
                Sound = (SenseLevel)(int)GetDouble(data, "sound", 20),
                Light = (SenseLevel)(int)GetDouble(data, "light", 20),
                Thickness = GetDouble(data, "thickness", 5),
                Color = GetString(data, "color", "#D32F2F"),
                Doors = GetDoorSegments(data)
            },
            _ => null
        };
    }

    private static List<(double X, double Y)> GetPoints(ComponentData data)
    {
        if (!data.Properties.TryGetValue("points", out var raw)) return [];
        if (raw is not JsonElement el || el.ValueKind != JsonValueKind.Array) return [];
        var result = new List<(double, double)>();
        foreach (var item in el.EnumerateArray())
        {
            var x = item.TryGetProperty("x", out var xEl) && xEl.TryGetDouble(out var xv) ? xv : 0;
            var y = item.TryGetProperty("y", out var yEl) && yEl.TryGetDouble(out var yv) ? yv : 0;
            result.Add((x, y));
        }
        return result;
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

    private static List<ConditionEntry> GetConditions(ComponentData data)
    {
        if (!data.Properties.TryGetValue("conditions", out var raw)) return [];
        if (raw is not JsonElement el || el.ValueKind != JsonValueKind.Array) return [];
        var result = new List<ConditionEntry>();
        foreach (var item in el.EnumerateArray())
        {
            result.Add(new ConditionEntry
            {
                Id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N"),
                Name = item.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "",
                Icon = item.TryGetProperty("icon", out var iEl) ? iEl.GetString() ?? "🎭" : "🎭",
                StackCount = item.TryGetProperty("stackCount", out var scEl) && scEl.TryGetInt32(out var sc) ? sc : 1,
                RemainingRounds = item.TryGetProperty("remainingRounds", out var rrEl) && rrEl.TryGetInt32(out var rr) ? rr : -1,
                ColorHex = item.TryGetProperty("colorHex", out var chEl) ? chEl.GetString() ?? "#10B981" : "#10B981"
            });
        }
        return result;
    }

    private static List<DoorSegment> GetDoorSegments(ComponentData data)
    {
        if (!data.Properties.TryGetValue("doors", out var raw)) return [];
        if (raw is not JsonElement el || el.ValueKind != JsonValueKind.Array) return [];
        var result = new List<DoorSegment>();
        foreach (var item in el.EnumerateArray())
        {
            var door = new DoorSegment
            {
                Id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N"),
                StartAnchorIndex = item.TryGetProperty("startAnchorIndex", out var siEl) && siEl.TryGetInt32(out var si) ? si : 0,
                EndAnchorIndex = item.TryGetProperty("endAnchorIndex", out var eiEl) && eiEl.TryGetInt32(out var ei) ? ei : 0,
                Kind = item.TryGetProperty("kind", out var kEl) && kEl.TryGetInt32(out var k) ? (DoorKind)k : DoorKind.Door,
                State = item.TryGetProperty("state", out var stEl) && stEl.TryGetInt32(out var st) ? (DoorState)st : DoorState.Closed,
                Swing = item.TryGetProperty("swing", out var swEl) && swEl.TryGetInt32(out var sw) ? (DoorSwing)sw : DoorSwing.None
            };

            // 处理可选的感知覆盖
            if (item.TryGetProperty("sightOverride", out var sightEl) && sightEl.ValueKind != JsonValueKind.Null && sightEl.TryGetInt32(out var sight))
                door.SightOverride = (SenseLevel)sight;

            if (item.TryGetProperty("moveOverride", out var moveEl) && moveEl.ValueKind != JsonValueKind.Null && moveEl.TryGetInt32(out var move))
                door.MoveOverride = (SenseLevel)move;

            result.Add(door);
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

public sealed class DoorSegmentData
{
    public string Id { get; set; } = string.Empty;
    public int StartAnchorIndex { get; set; }
    public int EndAnchorIndex { get; set; }
    public int Kind { get; set; }
    public int State { get; set; }
    public int Swing { get; set; }
    public int? SightOverride { get; set; }
    public int? MoveOverride { get; set; }
}
