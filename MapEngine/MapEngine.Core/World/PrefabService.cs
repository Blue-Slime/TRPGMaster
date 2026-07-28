using MapEngine.Core.Components;

namespace MapEngine.Core.Assets;

public sealed class PrefabAsset
{
    public Guid PrefabId { get; init; }
    public string Name { get; set; } = "Prefab";
    public GameObject Template { get; set; } = new();
}

public sealed class PrefabInstance
{
    public Guid InstanceId { get; init; } = Guid.NewGuid();
    public Guid PrefabId { get; init; }
    public GameObject GameObject { get; init; } = new();
}

public sealed class PrefabService
{
    private readonly Dictionary<Guid, PrefabAsset> _prefabs = new();
    private readonly Dictionary<Guid, Guid> _instanceToPrefab = new();

    public IReadOnlyDictionary<Guid, PrefabAsset> Prefabs => _prefabs;

    public void RegisterPrefab(PrefabAsset prefab)
    {
        _prefabs[prefab.PrefabId] = prefab;
    }

    public PrefabAsset CreatePrefabFromGameObject(GameObject source, Guid prefabId)
    {
        var template = source.Clone(deepChildren: true);
        var prefab = new PrefabAsset
        {
            PrefabId = prefabId,
            Name = source.Name,
            Template = template
        };
        _prefabs[prefabId] = prefab;
        return prefab;
    }

    public PrefabInstance Instantiate(Guid prefabId)
    {
        if (!_prefabs.TryGetValue(prefabId, out var prefab))
            throw new InvalidOperationException($"Prefab {prefabId} not found.");

        var instance = prefab.Template.Clone(deepChildren: true);
        var prefabInstance = new PrefabInstance
        {
            PrefabId = prefabId,
            GameObject = instance
        };

        _instanceToPrefab[instance.Id] = prefabId;
        return prefabInstance;
    }

    public bool IsInstance(Guid gameObjectId)
        => _instanceToPrefab.ContainsKey(gameObjectId);

    public Guid? GetPrefabId(Guid gameObjectId)
        => _instanceToPrefab.TryGetValue(gameObjectId, out var id) ? id : null;

    public void ApplyToPrefab(Guid prefabId, GameObject sourceInstance)
    {
        if (!_prefabs.TryGetValue(prefabId, out var prefab))
            return;

        prefab.Template = sourceInstance.Clone(deepChildren: true);
        prefab.Name = sourceInstance.Name;
    }

    public List<Guid> GetAllInstances(Guid prefabId)
    {
        return _instanceToPrefab
            .Where(kv => kv.Value == prefabId)
            .Select(kv => kv.Key)
            .ToList();
    }

    public void TrackInstance(Guid gameObjectId, Guid prefabId)
    {
        _instanceToPrefab[gameObjectId] = prefabId;
    }

    public void UntrackInstance(Guid gameObjectId)
    {
        _instanceToPrefab.Remove(gameObjectId);
    }
}
