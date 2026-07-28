namespace MapEngine.Core.Components;

public sealed class GameObject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "GameObject";
    public string Icon { get; set; } = "📦";
    public string ObjectType { get; set; } = "Empty";
    public bool IsActive { get; set; } = true;
    public bool IsLocked { get; set; }
    public int SortOrder { get; set; }
    public List<string> Tags { get; set; } = [];

    public GameObject? Parent { get; set; }
    public List<GameObject> Children { get; } = [];
    public List<IComponent> Components { get; } = [];

    /// <summary>
    /// 所属世界。由 <see cref="MapEngine.Core.World"/> 在 AddObject 时设置,RemoveObject 时清空。
    /// 游离(未入 World)的对象为 null。组件不应依赖它做业务逻辑。
    /// </summary>
    public MapEngine.Core.World? World { get; internal set; }

    public T? GetComponent<T>() where T : class, IComponent
        => Components.OfType<T>().FirstOrDefault();

    public IEnumerable<T> GetComponents<T>() where T : class, IComponent
        => Components.OfType<T>();

    /// <summary>
    /// 直接挂载组件(不触发索引登记)。用于游离子树构建期。
    /// 对象已入 World 后,请用 World.AddComponent 以保证索引/dirty 同步。
    /// </summary>
    public T AddComponent<T>(T component) where T : class, IComponent
    {
        component.Owner = this;
        Components.Add(component);
        return component;
    }

    public bool RemoveComponent<T>() where T : class, IComponent
    {
        var component = GetComponent<T>();
        if (component is null) return false;
        component.Owner = null;
        Components.Remove(component);
        return true;
    }

    public bool RemoveComponent(IComponent component)
    {
        if (!Components.Contains(component)) return false;
        component.Owner = null;
        Components.Remove(component);
        return true;
    }

    public GameObject Clone(bool deepChildren = true)
    {
        var clone = new GameObject
        {
            Id = Guid.NewGuid(),
            Name = Name,
            Icon = Icon,
            ObjectType = ObjectType,
            IsActive = IsActive,
            IsLocked = IsLocked,
            SortOrder = SortOrder,
            Tags = [.. Tags]
        };

        foreach (var component in Components)
        {
            var cloned = component.Clone();
            cloned.Owner = clone;
            clone.Components.Add(cloned);
        }

        if (deepChildren)
        {
            foreach (var child in Children)
            {
                var clonedChild = child.Clone(true);
                clonedChild.Parent = clone;
                clone.Children.Add(clonedChild);
            }
        }

        return clone;
    }
}
