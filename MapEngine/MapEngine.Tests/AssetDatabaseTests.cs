using MapEngine.Core.Assets;
using MapEngine.Core.Components;
using Xunit;

namespace MapEditor.Tests;

public class AssetDatabaseTests
{
    private readonly string _testRoot;

    public AssetDatabaseTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"MapEditorTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    [Fact]
    public void Initialize_AssignsGuidsToAllFiles()
    {
        File.WriteAllText(Path.Combine(_testRoot, "hero.asset"), "{}");
        File.WriteAllText(Path.Combine(_testRoot, "map.png"), "fake");

        var db = new AssetDatabase();
        db.Initialize(_testRoot);

        Assert.Equal(2, db.Count);
    }

    [Fact]
    public void GetOrAssignGuid_ReturnsSameGuidOnSecondCall()
    {
        var filePath = Path.Combine(_testRoot, "test.asset");
        File.WriteAllText(filePath, "{}");

        var db = new AssetDatabase();
        db.Initialize(_testRoot);

        var guid1 = db.GetOrAssignGuid(filePath);
        var guid2 = db.GetOrAssignGuid(filePath);

        Assert.Equal(guid1, guid2);
        Assert.NotEqual(Guid.Empty, guid1);
    }

    [Fact]
    public void Guid_PersistsAcrossReinitialize()
    {
        var filePath = Path.Combine(_testRoot, "persist.asset");
        File.WriteAllText(filePath, "{}");

        var db1 = new AssetDatabase();
        db1.Initialize(_testRoot);
        var guid = db1.GetOrAssignGuid(filePath);

        var db2 = new AssetDatabase();
        db2.Initialize(_testRoot);
        var guid2 = db2.GetOrAssignGuid(filePath);

        Assert.Equal(guid, guid2);
    }

    [Fact]
    public void NotifyMoved_UpdatesMapping()
    {
        var oldPath = Path.Combine(_testRoot, "old.asset");
        var newPath = Path.Combine(_testRoot, "new.asset");
        File.WriteAllText(oldPath, "{}");

        var db = new AssetDatabase();
        db.Initialize(_testRoot);
        var guid = db.GetOrAssignGuid(oldPath);

        File.Move(oldPath, newPath);
        db.NotifyMoved(oldPath, newPath);

        Assert.Equal(newPath, db.ResolvePath(guid));
        Assert.Null(db.ResolveGuid(oldPath));
        Assert.Equal(guid, db.ResolveGuid(newPath));
    }

    [Fact]
    public void NotifyDeleted_RemovesMapping()
    {
        var filePath = Path.Combine(_testRoot, "delete.asset");
        File.WriteAllText(filePath, "{}");

        var db = new AssetDatabase();
        db.Initialize(_testRoot);
        var guid = db.GetOrAssignGuid(filePath);

        db.NotifyDeleted(filePath);

        Assert.Null(db.ResolvePath(guid));
        Assert.Null(db.ResolveGuid(filePath));
    }
}

public class PrefabServiceTests
{
    [Fact]
    public void CreatePrefab_And_Instantiate_ProducesIndependentCopy()
    {
        var service = new PrefabService();

        var source = new GameObject { Name = "Goblin" };
        source.AddComponent(new TransformComponent { X = 10, Y = 20 });
        source.AddComponent(new TokenComponent { TokenName = "Goblin Warrior" });

        var prefabId = Guid.NewGuid();
        service.CreatePrefabFromGameObject(source, prefabId);

        var instance = service.Instantiate(prefabId);

        Assert.NotEqual(source.Id, instance.GameObject.Id);
        Assert.Equal("Goblin", instance.GameObject.Name);
        Assert.Equal(10.0, instance.GameObject.GetComponent<TransformComponent>()!.X);
        Assert.Equal("Goblin Warrior", instance.GameObject.GetComponent<TokenComponent>()!.TokenName);

        instance.GameObject.GetComponent<TransformComponent>()!.X = 999;
        var instance2 = service.Instantiate(prefabId);
        Assert.Equal(10.0, instance2.GameObject.GetComponent<TransformComponent>()!.X);
    }

    [Fact]
    public void IsInstance_TracksInstantiatedObjects()
    {
        var service = new PrefabService();
        var source = new GameObject { Name = "Template" };
        source.AddComponent(new TransformComponent());

        var prefabId = Guid.NewGuid();
        service.CreatePrefabFromGameObject(source, prefabId);
        var instance = service.Instantiate(prefabId);

        Assert.True(service.IsInstance(instance.GameObject.Id));
        Assert.Equal(prefabId, service.GetPrefabId(instance.GameObject.Id));
    }

    [Fact]
    public void ApplyToPrefab_UpdatesTemplate()
    {
        var service = new PrefabService();
        var source = new GameObject { Name = "Original" };
        source.AddComponent(new TransformComponent { X = 0 });

        var prefabId = Guid.NewGuid();
        service.CreatePrefabFromGameObject(source, prefabId);

        var modified = new GameObject { Name = "Modified" };
        modified.AddComponent(new TransformComponent { X = 42 });
        service.ApplyToPrefab(prefabId, modified);

        var newInstance = service.Instantiate(prefabId);
        Assert.Equal("Modified", newInstance.GameObject.Name);
        Assert.Equal(42.0, newInstance.GameObject.GetComponent<TransformComponent>()!.X);
    }

    [Fact]
    public void GetAllInstances_ReturnsTrackedIds()
    {
        var service = new PrefabService();
        var source = new GameObject { Name = "Multi" };
        source.AddComponent(new TransformComponent());

        var prefabId = Guid.NewGuid();
        service.CreatePrefabFromGameObject(source, prefabId);

        var i1 = service.Instantiate(prefabId);
        var i2 = service.Instantiate(prefabId);

        var all = service.GetAllInstances(prefabId);
        Assert.Equal(2, all.Count);
        Assert.Contains(i1.GameObject.Id, all);
        Assert.Contains(i2.GameObject.Id, all);
    }
}
