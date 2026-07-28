using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Linq;
using MasterClient.Chat.Messages;

namespace MasterClient.Chat.Panels;

public partial class MessageAreaView : UserControl
{
    private MessageAreaViewModel? _boundVm;

    public MessageAreaView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // VM 可能在控件生命周期内更换：解绑旧的、订阅新的滚动请求事件，避免泄漏/错发。
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundVm != null) _boundVm.ScrollToMessageRequested -= OnScrollToMessage;
        _boundVm = DataContext as MessageAreaViewModel;
        if (_boundVm != null) _boundVm.ScrollToMessageRequested += OnScrollToMessage;
    }

    // 把目标消息容器滚动到可视区（搜索窗双击定位）。列表是 ItemsControl，
    // 用可视树查找承载该 MsgId 的 MessageItemView 再 BringIntoView。
    private void OnScrollToMessage(long msgId)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var target = this.GetVisualDescendants()
                .OfType<MessageItemView>()
                .FirstOrDefault(v => v.DataContext is MessageItemViewModel m && m.MsgId == msgId);
            target?.BringIntoView();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        var input = this.FindControl<TextBox>("MessageInput");
        if (input != null)
        {
            input.KeyDown += OnInputKeyDown;
            input.PropertyChanged += OnInputChanged;
        }
        var edit = this.FindControl<TextBox>("EditBox");
        if (edit != null)
            edit.KeyDown += OnEditKeyDown;

        // @提及候选 ListBox：点击选中时插入
        var mentionList = this.FindControl<ListBox>("MentionList");
        if (mentionList != null)
            mentionList.PointerPressed += OnMentionListPointerPressed;
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            if (DataContext is MessageAreaViewModel vm)
                vm.SendMessageCommand.Execute(null);
        }
    }

    // 编辑态：Enter 提交，Esc 取消（Shift+Enter 换行）
    private void OnEditKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MessageAreaViewModel vm) return;
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            vm.CommitEditCommand.Execute(null);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.CancelEditCommand.Execute(null);
        }
    }

    private void OnInputChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == nameof(TextBox.Text) && DataContext is MessageAreaViewModel vm)
        {
            vm.NotifyTyping();
            vm.HandleInputChanged(vm.MessageInput);
        }
    }

    private void OnMentionListPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (DataContext is not MessageAreaViewModel vm) return;
        if (sender is not ListBox list) return;
        if (list.SelectedItem is not MemberItemViewModel member) return;
        vm.InsertMention(member);
        this.FindControl<TextBox>("MessageInput")?.Focus();
    }
}
