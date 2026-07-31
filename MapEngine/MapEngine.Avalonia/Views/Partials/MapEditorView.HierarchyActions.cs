using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Views;

/// <summary>场景层级树：右键上下文菜单与增删/移动/复制/粘贴操作。</summary>
public partial class MapEditorView
{
    private void ShowMapObjectContextMenu(HierarchyItemViewModel item, Control anchor)
    {
        if (_viewModel is null) return;
        // 局部捕获，避免闭包里对可空字段解引用（_viewModel 在菜单弹出后理论上可能被换掉）
        var vm = _viewModel;

        var menu = new ContextMenu();

        // ── 标题行（不可点击，显示对象名）──────────────────────
        var header = new MenuItem
        {
            Header = item.Name,
            Icon = MenuIcon(IconPaths.Token),
            IsEnabled = false
        };
        menu.Items.Add(header);
        menu.Items.Add(new Separator());

        // ── 重命名 ──────────────────────────────────────────────
        var renameItem = new MenuItem
        {
            Header = "重命名…",
            Icon = MenuIcon("M20.71,7.04C21.1,6.65 21.1,6 20.71,5.63L18.37,3.29C18,2.9 17.35,2.9 16.96,3.29L15.12,5.12L18.87,8.87M3,17.25V21H6.75L17.81,9.93L14.06,6.18L3,17.25Z"),
        };
        renameItem.Click += async (_, _) =>
        {
            var name = await PromptForNameAsync("重命名对象", item.Name);
            if (!string.IsNullOrWhiteSpace(name))
                vm.RenameHierarchyItem(item, name);
        };
        menu.Items.Add(renameItem);

        menu.Items.Add(new Separator());

        // ── 隐藏 / 显示（GM 控制，不删除对象）──────────────────
        var hideItem = new MenuItem
        {
            Header = item.IsActive ? "隐藏对象" : "显示对象",
            Icon = MenuIcon(item.IsActive
                // eye-off
                ? "M11.83,9L15,12.16C15,12.11 15,12.05 15,12A3,3 0 0,0 12,9C11.94,9 11.89,9 11.83,9M7.53,9.8L9.08,11.35C9.03,11.56 9,11.77 9,12A3,3 0 0,0 12,15C12.22,15 12.44,14.97 12.65,14.92L14.2,16.47C13.53,16.8 12.79,17 12,17A5,5 0 0,1 7,12C7,11.21 7.2,10.47 7.53,9.8M2,4.27L4.28,6.55L4.73,7C3.08,8.3 1.78,10 1,12C2.73,16.39 7,19.5 12,19.5C13.55,19.5 15.03,19.2 16.38,18.66L18.74,21L20,19.73L3.27,3M12,7A5,5 0 0,1 17,12C17,12.64 16.87,13.26 16.64,13.82L19.57,16.75C21.07,15.5 22.27,13.86 23,12C21.27,7.61 17,4.5 12,4.5C10.6,4.5 9.26,4.75 8,5.2L10.17,7.35C10.74,7.13 11.35,7 12,7Z"
                : IconPaths.Eye),
        };
        hideItem.Click += (_, _) =>
        {
            item.IsActive = !item.IsActive;
            vm.RefreshMapRenderableItemsPublic();
        };
        menu.Items.Add(hideItem);

        // ── 锁定 / 解锁（锁定后无法拖拽）─────────────────────
        var lockItem = new MenuItem
        {
            Header = item.IsLocked ? "解锁对象" : "锁定对象",
            Icon = MenuIcon(item.IsLocked
                // lock-open
                ? "M18,20V10H6V20H18M18,8A2,2 0 0,1 20,10V20A2,2 0 0,1 18,22H6C4.89,22 4,21.1 4,20V10A2,2 0 0,1 6,8H15V6A3,3 0 0,0 12,3A3,3 0 0,0 9,6H7A5,5 0 0,1 12,1A5,5 0 0,1 17,6V8H18M12,17A2,2 0 0,1 10,15C10,13.89 10.9,13 12,13A2,2 0 0,1 14,15A2,2 0 0,1 12,17Z"
                // lock
                : "M12,17A2,2 0 0,0 14,15C14,13.89 13.1,13 12,13A2,2 0 0,0 10,15A2,2 0 0,0 12,17M18,8A2,2 0 0,1 20,10V20A2,2 0 0,1 18,22H6A2,2 0 0,1 4,20V10C4,8.89 4.9,8 6,8H7V6A5,5 0 0,1 12,1A5,5 0 0,1 17,6V8H18M12,3A3,3 0 0,0 9,6V8H15V6A3,3 0 0,0 12,3Z"),
        };
        lockItem.Click += (_, _) =>
        {
            item.IsLocked = !item.IsLocked;
        };
        menu.Items.Add(lockItem);

        // ── snap-to-grid 吸附切换 ────────────────────────────
        var isSnap = vm.IsSnapToGrid;
        var snapItem = new MenuItem
        {
            Header = isSnap ? "关闭格子吸附" : "开启格子吸附",
            Icon = MenuIcon(isSnap
                // grid-off
                ? "M2.28,1L1,2.27L3,4.28V20A2,2 0 0,0 5,22H20.72L22,23.27L23.27,22L2.28,1M5,20V15H8.72L10,16.28V20H5M5,13V10H6.72L9,12.28V13H5M11,20V17.28L13.72,20H11M5,8V6.27L6.72,8H5M20,20H17.27L15,17.72V15H20V20M20,13H15V10H20V13M20,8H15V5.27L15.27,5H20V8M13,8H10.27L10,7.72V5H13V8Z"
                // grid
                : "M10,4V8H14V4H10M16,4V8H20V4H16M16,10V14H20V10H16M16,16V20H20V16H16M14,20V16H10V20H14M8,20V16H4V20H8M8,14V10H4V14H8M8,8V4H4V8H8M10,14H14V10H10M4,2H20A2,2 0 0,1 22,4V20A2,2 0 0,1 20,22H4C2.92,22 2,21.1 2,20V4A2,2 0 0,1 4,2Z"),
        };
        snapItem.Click += (_, _) => vm.IsSnapToGrid = !vm.IsSnapToGrid;
        menu.Items.Add(snapItem);

        menu.Items.Add(new Separator());

        // ── 复制 / 粘贴 ─────────────────────────────────────
        var copyItem = new MenuItem
        {
            Header = "复制",
            Icon = MenuIcon("M19,21H8V7H19M19,5H8A2,2 0 0,0 6,7V21A2,2 0 0,0 8,23H19A2,2 0 0,0 21,21V7A2,2 0 0,0 19,5M16,1H4A2,2 0 0,0 2,3V17H4V3H16V1Z"),
        };
        copyItem.Click += (_, _) => vm.CopyHierarchyItem(item);
        menu.Items.Add(copyItem);

        var pasteItem = new MenuItem
        {
            Header = "粘贴",
            IsEnabled = vm.HasHierarchyClipboard,
            Icon = MenuIcon("M19,20H5V4H7V7H17V4H19M12,2A1,1 0 0,1 13,3A1,1 0 0,1 12,4A1,1 0 0,1 11,3A1,1 0 0,1 12,2M19,2H14.82C14.4,0.84 13.3,0 12,0C10.7,0 9.6,0.84 9.18,2H5A2,2 0 0,0 3,4V20A2,2 0 0,0 5,22H19A2,2 0 0,0 21,20V4A2,2 0 0,0 19,2Z"),
        };
        pasteItem.Click += (_, _) => vm.PasteHierarchyToRoot();
        menu.Items.Add(pasteItem);

        var dupItem = new MenuItem
        {
            Header = "创建副本",
            Icon = MenuIcon("M11,17H4A2,2 0 0,1 2,15V3A2,2 0 0,1 4,1H16V3H4V15H11V13L15,16.5L11,20V17M19,21V7H8V13H6V7A2,2 0 0,1 8,5H19A2,2 0 0,1 21,7V21A2,2 0 0,1 19,23H8A2,2 0 0,1 6,21V19H8V21H19Z"),
        };
        dupItem.Click += (_, _) => vm.DuplicateHierarchyItem(item);
        menu.Items.Add(dupItem);

        menu.Items.Add(new Separator());

        // ── 层内排序（同级上下移动，影响绘制顺序）──────────────
        var frontItem = new MenuItem
        {
            Header = "上移一层",
            Icon = MenuIcon("M2,2H11V11H2V2M9,4H4V9H9V4M22,13V22H13V13H22M15,20H20V15H15V20M16,8V11H13V8H16M11,16H8V13H11V16M11,2H22V11H11V2M13,4V9H20V4H13Z"),
        };
        frontItem.Click += (_, _) => vm.MoveItemUp(item);
        menu.Items.Add(frontItem);

        var backItem = new MenuItem
        {
            Header = "下移一层",
            Icon = MenuIcon("M2,2H11V11H2V2M11,13H20V22H11V13M22,2V11H13V2H22M20,4H15V9H20V4M9,15H4V20H9V15M4,13H13V22H4V13Z"),
        };
        backItem.Click += (_, _) => vm.MoveItemDown(item);
        menu.Items.Add(backItem);

        menu.Items.Add(new Separator());

        // ── Token 先攻功能（仅 Token 对象显示）──────────────────
        if (item.HasComponent<MapEngine.Core.Components.TokenComponent>())
        {
            var tokenComp = item.GetComponent<MapEngine.Core.Components.TokenComponent>();
            if (tokenComp != null)
            {
                var initiativeItem = new MenuItem
                {
                    Header = tokenComp.IsInInitiativeTracker ? "移出先攻表" : "加入先攻表",
                    Icon = MenuIcon(tokenComp.IsInInitiativeTracker
                        ? "M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z" // minus
                        : "M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z"), // plus
                };
                initiativeItem.Click += (_, _) =>
                {
                    if (tokenComp.IsInInitiativeTracker)
                        vm.RemoveFromInitiativeTracker(item);
                    else
                        vm.AddToInitiativeTracker(item);
                };
                menu.Items.Add(initiativeItem);
                menu.Items.Add(new Separator());
            }
        }

        // ── 删除 ────────────────────────────────────────────
        var deleteItem = new MenuItem
        {
            Header = "删除",
            Icon = MenuIcon("M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z"),
        };
        deleteItem.Click += (_, _) => vm.DeleteHierarchyItem(item);
        menu.Items.Add(deleteItem);

        menu.Open(anchor);
    }
    private async void AddHierarchyChild_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } parent)
        {
            return;
        }

        _viewModel.SelectedHierarchyItem = parent;
        var created = _viewModel.AddEmptyObject(parent);
        var name = await PromptForNameAsync("新建空对象", created.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _viewModel.RenameHierarchyItem(created, name);
        }
    }

    private async void AddWallObject_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } parent)
        {
            return;
        }

        _viewModel.SelectedHierarchyItem = parent;
        var created = _viewModel.CreateWallObject(parent);
        if (created is not null)
        {
            var name = await PromptForNameAsync("新建墙壁", created.Name);
            if (!string.IsNullOrWhiteSpace(name))
            {
                _viewModel.RenameHierarchyItem(created, name);
            }
        }
    }

    private async void AddRootEmptyObject_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        var created = _viewModel.AddRootEmptyObject();
        if (created is null)
        {
            return;
        }

        var name = await PromptForNameAsync("新建空对象", created.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _viewModel.RenameHierarchyItem(created, name);
        }
    }

    private async void AddRootWallObject_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        var root = _viewModel.HierarchyRoots.FirstOrDefault();
        if (root is null)
        {
            return;
        }

        var created = _viewModel.CreateWallObject(root);
        if (created is null)
        {
            return;
        }

        var name = await PromptForNameAsync("新建墙壁", created.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _viewModel.RenameHierarchyItem(created, name);
        }
    }

    private void CopyHierarchyItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.CopyHierarchyItem(item);
    }

    private void PasteHierarchyItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.SelectedHierarchyItem = item;
        _viewModel.PasteHierarchyItem(item);
    }

    private void PasteHierarchyToRoot_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel?.PasteHierarchyToRoot();
    }

    private void DuplicateHierarchyItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.DuplicateHierarchyItem(item);
    }

    private void MoveHierarchyItemUp_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.MoveItemUp(item);
    }

    private void MoveHierarchyItemDown_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.MoveItemDown(item);
    }

    private void PromoteHierarchyItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.PromoteItem(item);
    }

    private void DemoteHierarchyItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.DemoteItem(item);
    }

    private async void RenameHierarchyItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.SelectedHierarchyItem = item;
        var name = await PromptForNameAsync("重命名层级对象", item.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _viewModel.RenameHierarchyItem(item, name);
        }
    }

    private void DeleteHierarchyItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.DeleteHierarchyItem(item);
    }

    private void AddToInitiativeTracker_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.AddToInitiativeTracker(item);
    }

    private void RemoveFromInitiativeTracker_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.RemoveFromInitiativeTracker(item);
    }
}
