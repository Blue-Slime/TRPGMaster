using System;
using MapEngine.Core;
using MapEngine.Core.Commands;
using MapEngine.Core.Components;
using MapEngine.Avalonia.Services;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Commands;

/// <summary>
/// 在地图上创建文本标注。
/// 锚点存 TransformComponent.X/Y（世界坐标），内容与样式存 TextComponent。
/// 渲染由 Avalonia 文本覆盖层负责（GL 层无字体栈）。
/// </summary>
public sealed class VmCreateTextCommand : ILocalOnlyCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly double _wx, _wy;
    private readonly string _text;
    private string? _createdId;

    public VmCreateTextCommand(MainWindowViewModel vm, double wx, double wy, string text)
    {
        _vm = vm;
        _wx = wx; _wy = wy;
        _text = text;
    }

    public string Description => "创建文本";

    public void Execute(World world)
    {
        // 层级树里名字太长会挤爆面板，超长内容截断显示
        var label = _text.Length > 12 ? _text[..12] + "…" : _text;

        var dto = new HierarchyNodeDto
        {
            Id         = Guid.NewGuid().ToString(),
            Name       = label,
            Icon       = "🅣",
            ObjectType = "Text",
            HasMapPosition = true,
            X = _wx,
            Y = _wy,
        };

        var root = _vm.GetOrCreateTextRoot();
        var item = new HierarchyItemViewModel(dto) { Parent = root };

        item.AddComponent(new TextComponent
        {
            Text     = _text,
            FontSize = _vm.TextDefaultFontSize,
            Color    = _vm.TextDefaultColor,
        });

        root.Children.Add(item);
        _vm.RegisterHierarchyItem(item);
        _vm.RefreshMapRenderableItemsPublic();
        _vm.SelectedHierarchyItem = item;
        _createdId = item.Id;
    }

    public void Undo(World world)
    {
        if (_createdId is null) return;
        var item = _vm.FindHierarchyById(_createdId);
        if (item?.Parent is null) return;
        item.Parent.Children.Remove(item);
        _vm.UnregisterHierarchyItem(item);
        _vm.RefreshMapRenderableItemsPublic();
        _createdId = null;
    }
}
