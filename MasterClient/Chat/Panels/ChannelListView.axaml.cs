using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace MasterClient.Chat.Panels;

public partial class ChannelListView : UserControl
{
    private const string DragFormat = "channel-item-index";
    private ListBox? _list;
    private Point _pressPos;
    private int _pressIndex = -1;
    private bool _dragging;

    public ChannelListView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _list = this.FindControl<ListBox>("ChannelListBox");
        if (_list != null)
        {
            _list.AddHandler(PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _list.AddHandler(PointerMovedEvent, OnPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            DragDrop.SetAllowDrop(_list, true);
            _list.AddHandler(DragDrop.DropEvent, OnDrop);
            _list.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 仅左键起拖
        if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed) return;
        _pressPos = e.GetPosition(_list);
        _pressIndex = IndexFromPointer(e);
        _dragging = false;
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressIndex < 0 || _dragging) return;
        if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed) { _pressIndex = -1; return; }

        var pos = e.GetPosition(_list);
        if (Math.Abs(pos.Y - _pressPos.Y) < 6 && Math.Abs(pos.X - _pressPos.X) < 6) return;

        _dragging = true;
        var data = new DataObject();
        data.Set(DragFormat, _pressIndex);
        try
        {
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        }
        catch { }
        finally { _dragging = false; _pressIndex = -1; }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DragFormat)) return;
        if (e.Data.Get(DragFormat) is not int fromIndex) return;
        if (DataContext is not ChannelListViewModel vm) return;

        var toIndex = IndexFromPoint(e.GetPosition(_list));
        if (toIndex < 0) toIndex = vm.Channels.Count - 1;

        await vm.MoveChannelAsync(fromIndex, toIndex);
    }

    private int IndexFromPointer(PointerPressedEventArgs e)
        => IndexFromPoint(e.GetPosition(_list));

    private int IndexFromPoint(Point p)
    {
        if (_list == null) return -1;
        var el = _list.InputHitTest(p) as Visual;
        while (el != null && el is not ListBoxItem)
            el = el.GetVisualParent();
        if (el is ListBoxItem item)
            return _list.IndexFromContainer(item);
        return -1;
    }
}
