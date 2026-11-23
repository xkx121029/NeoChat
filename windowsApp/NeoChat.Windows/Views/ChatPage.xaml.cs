using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using NeoChat.Windows.Design;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using NeoChat.Windows.Core.Chat;
using NeoChat.Windows.Core.Design;
using NeoChat.Windows.Core.Errors;
using NeoChat.Windows.Core.Storage;
using NeoChat.Windows.Views.Markdown;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace NeoChat.Windows.Views;

/// <summary>
/// The one screen of the M1 closure. Chat logic lives in <see cref="ChatSessionViewModel"/>; this file
/// only does what XAML cannot express on its own: geometry derived from the column width (Y11), scroll
/// following (Y10), and per-row template updates for the streaming cursor (ST01).
/// </summary>
public sealed partial class ChatPage : Page
{
    private readonly Dictionary<FrameworkElement, (ChatMessageItem Item, PropertyChangedEventHandler Handler)> _tracked = [];
    private readonly Dictionary<FrameworkElement, Storyboard> _pulses = [];
    private readonly ProviderStore _store;
    private NeoChatDatabase? _database;
    private SqliteConversationRepository? _repository;
    private ProviderSettings _settings;
    private readonly ScrollFollowState _follow = new(Tokens.Layout.FollowThreshold);
    private bool _syncingSelection;
    private double _bubbleMaxWidth;

    public ChatPage()
    {
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeoChat");
        _store = new ProviderStore(dataDirectory);
        _settings = _store.Resolve();
        var failureLog = new JsonlFailureLog(dataDirectory);
        var client = new HttpChatStreamClient(() => _settings);

        // History lives in %LOCALAPPDATA%\NeoChat\neochat.db. If that file cannot be opened the chat
        // still works for this run; the reason lands in startup.log instead of a silently empty rail.
        _repository = OpenConversationStore();
        Conversations = new ConversationListViewModel(_repository);
        ViewModel = new ChatSessionViewModel(client, failureLog, _settings, _repository);

        InitializeComponent();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.Messages.CollectionChanged += OnMessagesChanged;
        ReadingColumn.SizeChanged += OnReadingColumnSizeChanged;
        MessagesRepeater.SizeChanged += (_, _) => FollowIfPinned();
        Loaded += OnLoaded;
        Unloaded += OnPageUnloaded;
    }

    public ChatSessionViewModel ViewModel { get; }

    public ConversationListViewModel Conversations { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Y34 行高 1.45 无法表达：WinUI 的 TextBox 不暴露 LineHeight（XAML 里写它会让编译器直接崩），
        // 字号 15 仍来自令牌，行高交给控件默认值，已记入偏差清单。
        ThemeRuntime.Changed += OnThemeChanged;
        ApplyTheme();
        UpdateStatusIndicator();
        UpdateSendKey();
        UpdateBubbleWidth();
        RenderErrorBanner();
        RestoreConversations();
        FocusComposer();
    }

    /// <summary>Opens the database, reconciles messages a killed process left streaming, and returns its store.</summary>
    private SqliteConversationRepository? OpenConversationStore()
    {
        try
        {
            _database = NeoChatDatabase.Open();
            var repository = new SqliteConversationRepository(_database);
            var reconciled = repository.AbortOrphanStreaming();
            if (reconciled > 0)
            {
                App.Trace("startup: " + reconciled + " message(s) left streaming were marked aborted");
            }

            return repository;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            App.Trace("conversation store unavailable, history disabled for this run", exception);
            _database?.Dispose();
            _database = null;
            return null;
        }
    }

    /// <summary>Rail first, then the newest conversation: that is what "reopen where I left off" means.</summary>
    private void RestoreConversations()
    {
        Conversations.Refresh();
        if (Conversations.Items.Count == 0)
        {
            ViewModel.StartNewConversation();
            return;
        }

        ViewModel.OpenConversation(Conversations.Items[0].Id);
        SelectRail(Conversations.Items[0].Id);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        ThemeRuntime.Changed -= OnThemeChanged;
        ViewModel.Dispose();
        _repository?.Dispose();
        _repository = null;
        _database = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ChatSessionViewModel.Status):
                UpdateStatusIndicator();
                UpdateSendKey();
                UpdateActionRows();
                FollowIfPinned();

                // A finished turn changes a row's title or time, so the rail is rebuilt once the session
                // settles rather than on every token.
                if (!ViewModel.IsStreaming)
                {
                    Conversations.Refresh(ViewModel.ConversationId);
                    SelectRail(ViewModel.ConversationId);
                }

                break;
            case nameof(ChatSessionViewModel.Error):
                RenderErrorBanner();
                break;
            case nameof(ChatSessionViewModel.ComposerText):
                UpdateSendKey();
                break;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateBubbleWidth();
        UpdateActionRows();
        FollowIfPinned();
    }

    private void OnReadingColumnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateBubbleWidth();
        FollowIfPinned();
    }

    // ---- 用户气泡几何（Y11：可用宽 82%）与乐观态（Y18） -------------------------------

    private void UpdateBubbleWidth()
    {
        var width = ReadingColumn.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        _bubbleMaxWidth = width * Tokens.Layout.BubbleMaxWidthRatio;
        for (var index = 0; index < ViewModel.Messages.Count; index++)
        {
            if (MessagesRepeater.TryGetElement(index) is Border bubble && ViewModel.Messages[index].IsUser)
            {
                bubble.MaxWidth = _bubbleMaxWidth;
            }
        }
    }

    // ---- 消息模板实例化 -----------------------------------------------------------------

    private void OnMessageElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not FrameworkElement element ||
            args.Index < 0 ||
            args.Index >= ViewModel.Messages.Count)
        {
            return;
        }

        var item = ViewModel.Messages[args.Index];
        element.Margin = args.Index == 0
            ? new Thickness(0)
            : new Thickness(0, item.IsUser ? Tokens.Space.BetweenTurns - Tokens.Space.InTurn : Tokens.Space.InTurn, 0, 0);

        if (item.IsUser)
        {
            ApplyUserBubble(element, item);
        }
        else
        {
            ApplyAssistantRow(element, item);
        }

        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName is nameof(ChatMessageItem.Status))
            {
                ApplyUserBubble(element, item);
            }

            if (item.IsUser)
            {
                return;
            }

            ApplyAssistantRow(element, item);
            // 流式时每个 token 都会走到这里：跟随由状态机决定，用户上滚过就不再打扰。
            FollowIfPinned();
        };
        item.PropertyChanged += handler;
        _tracked[element] = (item, handler);
    }

    private void OnMessageElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is not FrameworkElement element)
        {
            return;
        }

        StopAllPulses(element);
        if (_tracked.Remove(element, out var entry))
        {
            entry.Item.PropertyChanged -= entry.Handler;
        }
    }

    private void ApplyUserBubble(FrameworkElement element, ChatMessageItem item)
    {
        if (element is not Border bubble)
        {
            return;
        }

        if (_bubbleMaxWidth > 0)
        {
            bubble.MaxWidth = _bubbleMaxWidth;
        }

        bubble.Opacity = item.Status == MessageStatus.Sending ? Tokens.Layout.OptimisticBubbleOpacity : 1;
    }

    private void ApplyAssistantRow(FrameworkElement root, ChatMessageItem item)
    {
        if (root.FindName("AssistantBody") is MarkdownHost body)
        {
            // 增量解析：文本是追加时只重解析开着的那个块；光标由 MarkdownHost 内部维护（ST01/ST02）。
            body.Text = item.Text;
            body.ShowCursor = item.ShowStreamingCursor;
            if (!item.IsStreaming)
            {
                body.Complete();
            }
        }

        if (root.FindName("WaitingDots") is StackPanel dots)
        {
            dots.Visibility = item.ShowWaitingIndicator ? Visibility.Visible : Visibility.Collapsed;
            if (item.ShowWaitingIndicator)
            {
                StartPulse(dots, Motion.CreateWaitingDots(dots.Children.ToList()));
            }
            else
            {
                StopPulse(dots);
            }
        }

        if (root.FindName("ThinkingDetail") is TextBlock detail)
        {
            detail.Text = item.ReasoningText;
            detail.MaxLines = item.IsReasoningExpanded ? 0 : (int)Tokens.Layout.ThinkingWindowLines;
            detail.TextTrimming = item.IsReasoningExpanded ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        }

        if (root.FindName("ThinkingChevron") is FontIcon chevron)
        {
            chevron.Glyph = item.IsReasoningExpanded ? "\uE70E" : "\uE70D";
        }

    }

    private void OnThinkingRowTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChatMessageItem message })
        {
            message.IsReasoningExpanded = !message.IsReasoningExpanded;
        }
    }

    // ---- 消息操作（Y27/Y28：复制全文 / 重新生成 / 软删除） ------------------------------

    private const string CopyGlyph = "";
    private const string CopiedGlyph = "";

    private void OnAssistantPointerEntered(object sender, PointerRoutedEventArgs e) => SetActionsVisible(sender, true);

    private void OnAssistantPointerExited(object sender, PointerRoutedEventArgs e) => SetActionsVisible(sender, false);

    private static void SetActionsVisible(object sender, bool visible)
    {
        if (sender is FrameworkElement root && root.FindName("MessageActions") is FrameworkElement actions)
        {
            actions.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Y27：一轮只给最后一条正文露出操作行（流式中不给）。其余回复靠悬停取回同一行，
    /// 所以"看得见"不是发现功能的唯一途径；这里每次只留一条可见，旧回合不会堆着按钮。
    /// </summary>
    private void UpdateActionRows()
    {
        for (var index = 0; index < ViewModel.Messages.Count; index++)
        {
            if (MessagesRepeater.TryGetElement(index) is not FrameworkElement element ||
                element.FindName("MessageActions") is not FrameworkElement actions)
            {
                continue;
            }

            var item = ViewModel.Messages[index];
            var isTail = index == ViewModel.Messages.Count - 1 && item.IsAssistant && !item.IsStreaming;
            actions.Visibility = isTail ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnCopyMessageClick(object sender, RoutedEventArgs e)
    {
        if (ItemFor(sender) is not { } item)
        {
            return;
        }

        ClipboardToItem(item);
        ConfirmCopy(RootFor(sender)?.FindName("CopyMessageGlyph") as FontIcon);
    }

    private void OnRegenerateClick(object sender, RoutedEventArgs e)
    {
        if (ItemFor(sender) is { } item)
        {
            _ = ViewModel.RegenerateAsync(item);
        }
    }

    private void OnDeleteMessageClick(object sender, RoutedEventArgs e)
    {
        if (ItemFor(sender) is { } item)
        {
            ViewModel.DeleteMessage(item);
        }
    }

    /// <summary>Right click (or long press) on a reply opens the same four things as a menu.</summary>
    private void OnAssistantRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement root || ItemFor(sender) is not { } item)
        {
            return;
        }

        var flyout = new MenuFlyout();
        flyout.Items.Add(MenuItem("复制全文", () => ClipboardToItem(item)));

        // 这条消息里的每一个代码块都能单独复制，与代码井右上角的复制键同一个结果。
        foreach (var code in MessageContent.CodeBlocks(item.Text))
        {
            var text = code;
            var label = text.Length == 0 ? "（空代码块）" : text;
            flyout.Items.Add(MenuItem("复制代码块：" + Preview(label), () => ClipboardWriter.Write(text)));
        }

        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(MenuItem("重新生成", () => _ = ViewModel.RegenerateAsync(item)));
        flyout.Items.Add(MenuItem("删除", () => ViewModel.DeleteMessage(item)));

        flyout.ShowAt(root, new FlyoutShowOptions { Position = e.GetPosition(root) });
        e.Handled = true;
    }

    private static MenuFlyoutItem MenuItem(string label, Action action)
    {
        var item = new MenuFlyoutItem { Text = label };
        item.Click += (_, _) => action();
        return item;
    }

    private static string Preview(string code)
    {
        var firstLine = code.Split('\n')[0].Trim();
        return firstLine.Length == 0 ? "（空代码块）" : firstLine;
    }

    private static void ClipboardToItem(ChatMessageItem item) => ClipboardWriter.Write(item.Text);

    /// <summary>A-N02：复制成功后图标交叉切换，短暂停留再换回（停留值是推断值）。</summary>
    private void ConfirmCopy(FontIcon? glyph)
    {
        if (glyph is null)
        {
            return;
        }

        glyph.Glyph = CopiedGlyph;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(Tokens.Layout.CopyConfirmHoldMilliseconds);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            glyph.Glyph = CopyGlyph;
            timer.Stop();
        };
        timer.Start();
    }

    private ChatMessageItem? ItemFor(object sender) =>
        RootFor(sender) is { } root && _tracked.TryGetValue(root, out var entry) ? entry.Item : null;

    /// <summary>The repeater row element a handler belongs to, whichever child raised the event.</summary>
    private FrameworkElement? RootFor(object sender)
    {
        for (var current = sender as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement element && _tracked.ContainsKey(element))
            {
                return element;
            }
        }

        return sender as FrameworkElement;
    }

    // ---- 主题（跟随系统，运行中也切换） ------------------------------------------------

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

    /// <summary>
    /// The palette itself is repainted in place by <see cref="ThemeRuntime"/>; what is left here is the
    /// platform half (default control chrome, flyouts, dialogs) and the four state brushes the send key
    /// keeps in its own resource dictionary, which are copies of a Colour token rather than a shared brush.
    /// </summary>
    private void ApplyTheme()
    {
        // 平台自带的那部分（滚动条、右键菜单、对话框）只认 ElementTheme，所以页面主题跟着令牌一起切。
        RequestedTheme = ThemeRuntime.Current == AppTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;

        PaintLocalBrush("ButtonBackgroundPointerOver", "SeedPressed");
        PaintLocalBrush("ButtonBackgroundPressed", "SeedPressed");
        PaintLocalBrush("ButtonBackgroundDisabled", "SendDisabled");
        PaintLocalBrush("ButtonForegroundDisabled", "SendForegroundDisabled");
    }

    private void PaintLocalBrush(string key, string tone)
    {
        if (SendKey.Resources is { } resources && resources[key] is SolidColorBrush brush)
        {
            brush.Color = ThemePainter.Tone(tone);
        }
    }

    // ---- 顶部栏状态（ST12 / ST13） ----------------------------------------------------

    private void UpdateStatusIndicator()
    {
        var role = ViewModel.StatusRole;
        var key = role switch
        {
            "busy" => "BrushSeed",
            "warn" => "BrushWarning",
            "error" => "BrushDanger",
            _ => "BrushOutlineLight",
        };
        StatusDot.Fill = (Brush)Application.Current.Resources[key];
        if (role == "busy")
        {
            StartPulse(StatusDot, Motion.CreateStatusDotBreath(StatusDot));
        }
        else
        {
            StopPulse(StatusDot);
            StatusDot.Opacity = 1;
        }
    }

    // ---- 输入栏（§2.2 Y43–Y46） -------------------------------------------------------

    private void UpdateSendKey()
    {
        var streaming = ViewModel.IsStreaming;
        SendKeyGlyph.Glyph = streaming ? "\uE71A" : "\uE724";
        SendKeyGlyph.FontSize = streaming ? Tokens.Layout.ComposerStopIconSize : Tokens.Layout.ComposerIconSize;
        SendKey.IsEnabled = streaming || ViewModel.CanSend;
        SetAutomationName(SendKey, streaming ? "停止" : "发送");
    }

    private void OnSendKeyClick(object sender, RoutedEventArgs e) => SendOrStop();

    private void OnComposerKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        if ((shift & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down)
        {
            return;
        }

        e.Handled = true;
        SendOrStop();
    }

    private void SendOrStop()
    {
        if (ViewModel.IsStreaming)
        {
            ViewModel.Stop();
            return;
        }

        if (ViewModel.CanSend)
        {
            ViewModel.SendCommand.Execute(null);
        }
    }

    private void FocusComposer() => ComposerInput.Focus(FocusState.Programmatic);

    // ---- 跟随底部（Y10）与回到底部（Y09） ---------------------------------------------

    private void OnMessageScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        _follow.OffsetChanged(MessageScroll.VerticalOffset, MessageScroll.ScrollableHeight - MessageScroll.VerticalOffset);
        UpdateJumpToBottom();
    }

    /// <summary>New content reached the list: stay glued to the bottom only while the reader is there.</summary>
    private void FollowIfPinned()
    {
        if (!_follow.ContentChanged())
        {
            return;
        }

        MessageScroll.ChangeView(null, MessageScroll.ScrollableHeight, null, true);
    }

    private void UpdateJumpToBottom() =>
        JumpToBottomButton.Visibility = _follow.ShowJumpToBottom ? Visibility.Visible : Visibility.Collapsed;

    private void OnJumpToBottomClick(object sender, RoutedEventArgs e)
    {
        _follow.JumpToBottom();
        MessageScroll.ChangeView(null, MessageScroll.ScrollableHeight, null, false);
        UpdateJumpToBottom();
    }

    // ---- 错误横幅（ST27） ---------------------------------------------------------------

    private void RenderErrorBanner()
    {
        var error = ViewModel.Error;
        ErrorActionsHost.Children.Clear();
        if (error is null)
        {
            ErrorTitleText.Text = string.Empty;
            ErrorCausesHost.ItemsSource = null;
            return;
        }

        ErrorTitleText.Text = error.Title;
        ErrorCausesHost.ItemsSource = error.Causes;
        foreach (var action in error.Actions)
        {
            var button = new Button
            {
                Content = action.Label,
                MinWidth = Tokens.Layout.ErrorActionMinSize,
                MinHeight = Tokens.Layout.ErrorActionMinSize,
                Padding = new Thickness(Tokens.Space.Sm),
                Background = (Brush)Application.Current.Resources["BrushTransparent"],
                Foreground = (Brush)Application.Current.Resources["BrushDanger"],
                FontSize = Tokens.Type.LabelLargeSize,
                BorderThickness = new Thickness(0),
            };
            var captured = action;
            button.Click += async (_, _) => await HandleErrorActionAsync(captured);
            ErrorActionsHost.Children.Add(button);
        }
    }

    private void OnDismissErrorClick(object sender, RoutedEventArgs e) => ViewModel.DismissError();

    private async Task HandleErrorActionAsync(ErrorActionItem action)
    {
        switch (action.Code)
        {
            case "retry":
            case "retry_after":
                await ViewModel.RetryAsync();
                break;
            case "edit_provider":
                await ShowProviderDialogAsync(focusBaseUrl: false);
                break;
            case "check_base_url":
                await ShowProviderDialogAsync(focusBaseUrl: true);
                break;
            case "switch_model":
            case "refresh_models":
            case "manage_keys":
            case "open_provider_settings":
            case "check_proxy":
                await ShowProviderDialogAsync(focusBaseUrl: false);
                break;
            case "copy_diagnostics":
                CopyToClipboard(ViewModel.Error?.ToDiagnosticsText() ?? string.Empty);
                break;
            default:
                await ShowUnavailableActionDialogAsync(action);
                break;
        }
    }

    // ---- 提供商设置（内存态；密钥不落盘） ----------------------------------------------

    private async Task ShowProviderDialogAsync(bool focusBaseUrl)
    {
        var baseUrl = new TextBox
        {
            Header = "Base URL",
            Text = _settings.BaseUrl,
            PlaceholderText = "https://api.openai.com/v1",
        };
        var model = new TextBox
        {
            Header = "模型 ID",
            Text = _settings.Model,
            PlaceholderText = "gpt-4o-mini",
        };
        var keyEnv = new TextBox
        {
            Header = "密钥所在的环境变量名（密钥本体不写入磁盘）",
            Text = _store.ActiveProvider()?.KeyEnv ?? ProviderStore.DefaultKeyEnv,
            PlaceholderText = ProviderStore.DefaultKeyEnv,
        };
        var apiKey = new PasswordBox
        {
            Header = "本次会话临时 Key（可选，不写入磁盘）",
            Password = _settings.ApiKey,
            PlaceholderText = "留空则读上面的环境变量",
        };
        var panel = new StackPanel { Spacing = Tokens.Space.Md };
        panel.Children.Add(baseUrl);
        panel.Children.Add(model);
        panel.Children.Add(keyEnv);
        panel.Children.Add(apiKey);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "提供商设置",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _store.Save(new ProviderConfig
        {
            Protocol = _settings.ProtocolLabel,
            BaseUrl = baseUrl.Text.Trim(),
            Model = model.Text.Trim(),
            KeyEnv = keyEnv.Text.Trim().Length == 0 ? ProviderStore.DefaultKeyEnv : keyEnv.Text.Trim(),
        });
        // 环境变量优先，其次 providers.json，最后是本次会话输入的临时 Key。
        var resolved = _store.Resolve();
        _settings = resolved.ApiKey.Length == 0 && apiKey.Password.Length > 0
            ? resolved with { ApiKey = apiKey.Password }
            : resolved;
        ViewModel.ApplyProviderSettings(_settings);
        ViewModel.DismissError();
        UpdateStatusIndicator();
        FocusComposer();
    }

    private async Task ShowUnavailableActionDialogAsync(ErrorActionItem action)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = action.Label,
            Content = "该操作属于后续里程碑（M1 只接入重试、提供商设置与复制诊断）。",
            CloseButtonText = "知道了",
        };
        await dialog.ShowAsync();
    }

    private static void CopyToClipboard(string text) => ClipboardWriter.Write(text);

    // ---- 空态与顶部栏动作 --------------------------------------------------------------

    private void OnStartConversationClick(object sender, RoutedEventArgs e) => FocusComposer();

    private void OnSuggestionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string suggestion })
        {
            ViewModel.ComposerText = suggestion;
        }

        FocusComposer();
    }

    private void OnNewConversationClick(object sender, RoutedEventArgs e)
    {
        ViewModel.StartNewConversation();
        Conversations.Refresh(ViewModel.ConversationId);
        SelectRail(ViewModel.ConversationId);
        UpdateBubbleWidth();
        FocusComposer();
    }

    private void OnConversationSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || ConversationList.SelectedItem is not ConversationListItem item)
        {
            return;
        }

        ViewModel.OpenConversation(item.Id);
        Conversations.Select(item.Id);
        UpdateBubbleWidth();
        FocusComposer();
    }

    /// <summary>Points the rail at the open conversation without re-entering the selection handler.</summary>
    private void SelectRail(string? conversationId)
    {
        _syncingSelection = true;
        try
        {
            Conversations.Select(conversationId);
            ConversationList.SelectedItem = conversationId is null
                ? null
                : Conversations.Items.FirstOrDefault(item => item.Id == conversationId);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private async void OnModelClick(object sender, RoutedEventArgs e) => await ShowProviderDialogAsync(focusBaseUrl: false);

    // ---- 动效句柄 -----------------------------------------------------------------------

    private void StartPulse(FrameworkElement target, Storyboard storyboard)
    {
        if (_pulses.TryGetValue(target, out var existing))
        {
            if (existing.GetCurrentState() == ClockState.Active)
            {
                return;
            }

            existing.Stop();
        }

        _pulses[target] = storyboard;
        storyboard.Begin();
    }

    private void StopPulse(FrameworkElement target)
    {
        if (_pulses.Remove(target, out var storyboard))
        {
            storyboard.Stop();
        }
    }

    private void StopAllPulses(DependencyObject root)
    {
        foreach (var element in _pulses.Keys.ToArray())
        {
            if (ReferenceEquals(element, root) || IsDescendant(root, element))
            {
                StopPulse(element);
            }
        }
    }

    private static bool IsDescendant(DependencyObject root, DependencyObject candidate)
    {
        for (var current = candidate; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }
        }

        return false;
    }

    private static void SetAutomationName(DependencyObject element, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, name);
}
