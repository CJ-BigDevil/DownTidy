using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DownTidy;

public sealed class MainWindow : Window
{
    private readonly App _app;
    private readonly OrganizerService _organizer;

    private readonly Border _statusPill = new();
    private readonly System.Windows.Shapes.Ellipse _statusDot = new() { Width = 8, Height = 8 };
    private readonly TextBlock _statusPillText = new() { FontSize = 12 };
    private readonly TextBlock _pathText = new() { FontSize = 12 };
    private readonly ListView _list = new();
    private readonly TextBlock _emptyText = new();
    private readonly TextBlock _countText = new();
    private Button _pauseButton = new();
    private bool _trayNoticeShown;

    public MainWindow(App app, OrganizerService organizer)
    {
        _app = app;
        _organizer = organizer;
        Title = "DownTidy · 下载文件自动归位";
        Width = 880;
        Height = 640;
        MinWidth = 700;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UiTheme.Apply(this);
        try
        {
            Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/icon256.png"));
        }
        catch { }
        BuildUi();
        RefreshStatus();
        RefreshOperations();
    }

    private Border Card() => UiTheme.Card();

    private void BuildUi()
    {
        var root = new Grid { Margin = new Thickness(28, 26, 28, 20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;

        // ===== 顶部卡片：品牌 + 状态胶囊 + 监控目录 =====
        var headerCard = Card();
        headerCard.Margin = new Thickness(0, 0, 0, 18);
        var headerGrid = new Grid { Margin = new Thickness(20, 18, 20, 18) };
        headerCard.Child = headerGrid;
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 头部直接用应用图标本体（与 exe / 托盘 / 任务栏同一形象）
        var logo = new Image
        {
            Width = 46,
            Height = 46,
            VerticalAlignment = VerticalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        try
        {
            logo.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/icon256.png"));
        }
        catch { }
        headerGrid.Children.Add(logo);

        var info = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(info, 1);
        headerGrid.Children.Add(info);

        var brandRow = new StackPanel { Orientation = Orientation.Horizontal };
        brandRow.Children.Add(new TextBlock
        {
            Text = "DownTidy",
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiTheme.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center
        });

        _statusPill.CornerRadius = new CornerRadius(11);
        _statusPill.Padding = new Thickness(10, 3, 10, 3);
        _statusPill.Margin = new Thickness(10, 0, 0, 0);
        _statusPill.VerticalAlignment = VerticalAlignment.Center;
        var pillStack = new StackPanel { Orientation = Orientation.Horizontal };
        _statusDot.VerticalAlignment = VerticalAlignment.Center;
        pillStack.Children.Add(_statusDot);
        _statusPillText.Margin = new Thickness(6, 0, 0, 0);
        _statusPillText.VerticalAlignment = VerticalAlignment.Center;
        pillStack.Children.Add(_statusPillText);
        _statusPill.Child = pillStack;
        brandRow.Children.Add(_statusPill);
        info.Children.Add(brandRow);

        var pathRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        pathRow.Children.Add(new TextBlock
        {
            Text = "\uE8B7", // 文件夹图标
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 12,
            Foreground = UiTheme.TextHint,
            VerticalAlignment = VerticalAlignment.Center
        });
        _pathText.Foreground = UiTheme.TextSecondary;
        _pathText.Margin = new Thickness(6, 0, 0, 0);
        _pathText.TextTrimming = TextTrimming.CharacterEllipsis;
        _pathText.VerticalAlignment = VerticalAlignment.Center;
        pathRow.Children.Add(_pathText);
        info.Children.Add(pathRow);
        root.Children.Add(headerCard);

        // ===== 按钮区 =====
        var buttons = new WrapPanel { Margin = new Thickness(2, 0, 2, 18) };
        Grid.SetRow(buttons, 1);
        var organizeButton = MakeButton("立即整理一次", (_, _) => { }, primary: true);
        organizeButton.Click += async (_, _) => await RunAction(organizeButton, "整理中…", _app.OrganizeNowAsync);
        buttons.Children.Add(organizeButton);
        _pauseButton = MakeButton("暂停自动整理", (_, _) => _app.TogglePause());
        buttons.Children.Add(_pauseButton);
        var undoButton = MakeButton("撤销最近一次", (_, _) => { });
        undoButton.Click += async (_, _) => await RunAction(undoButton, "撤销中…", _app.UndoAsync);
        buttons.Children.Add(undoButton);
        buttons.Children.Add(MakeButton("打开下载目录", (_, _) => _app.OpenDownloads()));
        buttons.Children.Add(MakeButton("高级规则", (_, _) => _app.OpenRules()));
        root.Children.Add(buttons);

        // ===== 记录卡片 =====
        var listCard = Card();
        Grid.SetRow(listCard, 2);
        var listLayout = new Grid();
        listCard.Child = listLayout;
        listLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        listLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        listLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var caption = new Grid { Margin = new Thickness(18, 14, 18, 12) };
        caption.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        caption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _countText.Text = "最近整理记录";
        _countText.FontSize = 13.5;
        _countText.FontWeight = FontWeights.SemiBold;
        _countText.Foreground = UiTheme.NeutralFg;
        caption.Children.Add(_countText);
        var hint = new TextBlock
        {
            Text = "双击记录，打开文件所在位置",
            FontSize = 11.5,
            Foreground = UiTheme.TextHint,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(hint, 1);
        caption.Children.Add(hint);
        listLayout.Children.Add(caption);

        var line = new System.Windows.Shapes.Rectangle { Height = 1, Fill = UiTheme.Divider };
        Grid.SetRow(line, 1);
        listLayout.Children.Add(line);

        var listHost = new Grid { Margin = new Thickness(0, 2, 0, 10) };
        Grid.SetRow(listHost, 2);
        listLayout.Children.Add(listHost);

        _list.Background = Brushes.Transparent;
        _list.BorderThickness = new Thickness(0);
        _list.SelectionMode = SelectionMode.Single;
        _list.MouseDoubleClick += (_, _) => OpenSelected();
        _list.ItemContainerStyle = MakeItemStyle();
        _list.ItemTemplate = MakeRowTemplate();
        listHost.Children.Add(_list);

        _emptyText.Text = "还没有整理记录。下载几个文件试试，或点上方「立即整理一次」。";
        _emptyText.Foreground = UiTheme.TextHint;
        _emptyText.FontSize = 12.5;
        _emptyText.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyText.VerticalAlignment = VerticalAlignment.Center;
        _emptyText.Visibility = Visibility.Collapsed;
        listHost.Children.Add(_emptyText);
        root.Children.Add(listCard);

        // ===== 底部提示 =====
        var footer = new TextBlock
        {
            Text = "关闭窗口后 DownTidy 仍在后台运行，可从任务栏托盘图标暂停或退出。",
            FontSize = 11.5,
            Foreground = UiTheme.TextHint,
            Margin = new Thickness(4, 14, 0, 0)
        };
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
    }

    private Style MakeItemStyle()
    {
        var style = new Style(typeof(ListViewItem));
        var tpl = new ControlTemplate(typeof(ListViewItem));
        var border = new FrameworkElementFactory(typeof(Border), "bd");
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 7, 6, 7));
        border.AppendChild(content);
        tpl.VisualTree = border;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, UiTheme.RowHover, "bd"));
        tpl.Triggers.Add(hover);
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, UiTheme.RowSelected, "bd"));
        tpl.Triggers.Add(selected);

        style.Setters.Add(new Setter(Control.TemplateProperty, tpl));
        style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
        return style;
    }

    private DataTemplate MakeRowTemplate()
    {
        var tpl = new DataTemplate { DataType = typeof(OperationRow) };

        var dock = new FrameworkElementFactory(typeof(DockPanel));
        dock.SetValue(FrameworkElement.MarginProperty, new Thickness(14, 8, 14, 8));

        // 右：状态胶囊 + 时间（停靠右侧）
        var right = new FrameworkElementFactory(typeof(StackPanel));
        right.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        right.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        right.SetValue(DockPanel.DockProperty, Dock.Right);
        right.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 0, 0, 0));

        var chip = new FrameworkElementFactory(typeof(Border));
        chip.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("StatusBg"));
        chip.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        chip.SetValue(Border.PaddingProperty, new Thickness(10, 3, 10, 3));
        chip.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var chipText = new FrameworkElementFactory(typeof(TextBlock));
        chipText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("StatusText"));
        chipText.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("StatusFg"));
        chipText.SetValue(TextBlock.FontSizeProperty, 11.5);
        chipText.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        chip.AppendChild(chipText);
        right.AppendChild(chip);

        var time = new FrameworkElementFactory(typeof(TextBlock));
        time.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Time"));
        time.SetValue(TextBlock.FontSizeProperty, 12.0);
        time.SetValue(TextBlock.ForegroundProperty, UiTheme.TextHint);
        time.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 0, 2, 0));
        time.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        right.AppendChild(time);

        // 左：文件名 + 所在目录（填充剩余空间）
        var left = new FrameworkElementFactory(typeof(StackPanel));
        left.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("FileName"));
        name.SetValue(TextBlock.FontSizeProperty, 13.0);
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        name.SetValue(TextBlock.ForegroundProperty, UiTheme.TextPrimary);
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        left.AppendChild(name);

        var folderRow = new FrameworkElementFactory(typeof(StackPanel));
        folderRow.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        folderRow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 0));
        var folderGlyph = new FrameworkElementFactory(typeof(TextBlock));
        folderGlyph.SetValue(TextBlock.TextProperty, "\uE8B7");
        folderGlyph.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets"));
        folderGlyph.SetValue(TextBlock.FontSizeProperty, 11.0);
        folderGlyph.SetValue(TextBlock.ForegroundProperty, UiTheme.TextHint);
        folderGlyph.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        folderRow.AppendChild(folderGlyph);
        var folder = new FrameworkElementFactory(typeof(TextBlock));
        folder.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Folder"));
        folder.SetValue(TextBlock.FontSizeProperty, 12.0);
        folder.SetValue(TextBlock.ForegroundProperty, UiTheme.TextSecondary);
        folder.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        folder.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0));
        folderRow.AppendChild(folder);
        left.AppendChild(folderRow);

        dock.AppendChild(right);
        dock.AppendChild(left);

        tpl.VisualTree = dock;
        return tpl;
    }

    public void RefreshStatus()
    {
        bool paused = _organizer.IsPaused;
        _statusPill.Background = paused ? UiTheme.PillPauseBg : UiTheme.AccentLight;
        _statusDot.Fill = paused ? UiTheme.PauseDot : UiTheme.RunDot;
        _statusPillText.Foreground = paused ? UiTheme.PillPauseFg : UiTheme.PillRunFg;
        _statusPillText.Text = paused ? "已暂停：新文件不会被移动" : "自动整理中";
        _pathText.Text = "监控目录：" + _organizer.DownloadRoot;
        _pauseButton.Content = paused ? "恢复自动整理" : "暂停自动整理";
    }

    public void RefreshOperations()
    {
        var selected = (_list.SelectedItem as OperationRow)?.Entry.Id;
        var rows = _organizer.GetRecent().Select(e => new OperationRow(e)).ToList();
        _list.ItemsSource = rows;
        if (selected != null) _list.SelectedItem = rows.FirstOrDefault(r => r.Entry.Id == selected);
        _emptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _countText.Text = rows.Count == 0 ? "最近整理记录" : $"最近整理记录（{rows.Count}）";
    }

    private void OpenSelected()
    {
        if (_list.SelectedItem is OperationRow row) App.OpenEntryLocation(row.Entry);
        else MessageBox.Show(this, "请先选择一条整理记录。", "打开位置", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task RunAction(Button button, string busyText, Func<Task> action)
    {
        var original = button.Content;
        button.IsEnabled = false;
        button.Content = busyText;
        try { await action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally
        {
            button.IsEnabled = true;
            button.Content = original;
        }
    }

    private Button MakeButton(string text, RoutedEventHandler handler, bool primary = false)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 12, 0) };
        button.Style = primary ? UiTheme.PrimaryButtonStyle() : UiTheme.SecondaryButtonStyle();
        button.Click += handler;
        return button;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_app.IsExiting)
        {
            e.Cancel = true;
            Hide();
            if (!_trayNoticeShown)
            {
                _trayNoticeShown = true;
                MessageBox.Show(this, "窗口已关闭，DownTidy 仍在托盘后台运行。右键托盘图标可以暂停或退出。", "仍在后台运行", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        base.OnClosing(e);
    }

    private sealed class OperationRow
    {
        public OperationRow(OperationEntry entry) => Entry = entry;
        public OperationEntry Entry { get; }
        public string Time => Entry.Timestamp.ToString("MM-dd HH:mm:ss");
        public string FileName => Path.GetFileName(Entry.Status == "undone" ? Entry.SourcePath : Entry.DestinationPath);
        public string StatusText => Entry.Status switch { "moved" => "已归位", "undone" => "已撤销", "failed" => "失败", _ => Entry.Status };
        public Brush StatusBg => Entry.Status switch { "moved" => UiTheme.AccentLight, "undone" => UiTheme.Divider, "failed" => Frozen(Color.FromRgb(253, 232, 232)), _ => UiTheme.Divider };
        public Brush StatusFg => Entry.Status switch { "moved" => UiTheme.SuccessFg, "undone" => UiTheme.TextSecondary, "failed" => UiTheme.ErrorFg, _ => UiTheme.TextSecondary };
        public string Folder => Path.GetFileName(Path.GetDirectoryName(Entry.Status == "undone" ? Entry.SourcePath : Entry.DestinationPath)) ?? "";

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
