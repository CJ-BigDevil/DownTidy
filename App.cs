using System.Diagnostics;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace DownTidy;

public sealed class App : System.Windows.Application
{
    private OrganizerService _organizer = null!;
    private RuleService _ruleService = null!;
    private MainWindow _mainWindow = null!;
    private Forms.NotifyIcon _tray = null!;
    private Forms.ToolStripMenuItem _pauseItem = null!;
    private OperationEntry? _balloonEntry;
    private string? _balloonFallbackFolder;
    private DateTime _lastBalloon = DateTime.MinValue;
    private bool _organizeBusy;
    private bool _undoBusy;

    public bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DownTidy");
        var downloadRoot = GetDownloadsFolder();
        _ruleService = new RuleService(dataDirectory, downloadRoot);
        _organizer = new OrganizerService(_ruleService, downloadRoot, dataDirectory);
        _organizer.OperationCompleted += OnOperationCompleted;
        _organizer.StatusChanged += (_, _) => Dispatcher.Invoke(RefreshStatus);

        _mainWindow = new MainWindow(this, _organizer);
        MainWindow = _mainWindow;
        CreateTray();
        _mainWindow.Show();
        _organizer.Start();
        RefreshStatus();
    }

    public async Task OrganizeNowAsync()
    {
        if (_organizeBusy) return;
        _organizeBusy = true;
        try
        {
            var summary = await _organizer.OrganizeNowAsync();
            RefreshOperations();
            ShowBalloon("整理完成", $"已归位 {summary.Moved} 个，跳过 {summary.Skipped} 个，失败 {summary.Failed} 个。", Forms.ToolTipIcon.Info, true,
                fallbackFolder: _organizer.DownloadRoot);
        }
        finally { _organizeBusy = false; }
    }

    public async Task UndoAsync()
    {
        if (_undoBusy) return;
        _undoBusy = true;
        try
        {
            var result = await _organizer.UndoLastAsync();
            RefreshOperations();
            var icon = result.Failed == 0 ? Forms.ToolTipIcon.Info : Forms.ToolTipIcon.Warning;
            ShowBalloon("撤销整理", result.Message, icon, true,
                fallbackFolder: _organizer.DownloadRoot);
            if (result.Failed > 0)
                MessageBox.Show(_mainWindow, result.Message, "部分文件未恢复", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _undoBusy = false; }
    }

    public void TogglePause()
    {
        _organizer.TogglePause();
        RefreshStatus();
    }

    public void OpenRules()
    {
        var window = new RulesWindow(_ruleService) { Owner = _mainWindow };
        window.ShowDialog();
    }

    public void OpenDownloads() => OpenFolder(_organizer.DownloadRoot);

    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    public static void OpenEntryLocation(OperationEntry entry)
    {
        var path = entry.Status == "moved" && File.Exists(entry.DestinationPath)
            ? entry.DestinationPath
            : entry.SourcePath;
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            return;
        }
        var directory = Path.GetDirectoryName(path);
        if (directory != null && Directory.Exists(directory)) OpenFolder(directory);
        else MessageBox.Show("文件和所在目录都已不存在。", "无法打开位置", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void ShowMainWindow()
    {
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
        _mainWindow.RefreshOperations();
    }

    public void ExitApplication()
    {
        IsExiting = true;
        _organizer.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        Shutdown();
    }

    private void OnOperationCompleted(object? sender, OperationEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _mainWindow.RefreshOperations();
            if (e.IsManualScan) return;
            if (e.Entry.Status == "moved")
            {
                var folder = Path.GetFileName(Path.GetDirectoryName(e.Entry.DestinationPath));
                ShowBalloon("文件已归位", $"{Path.GetFileName(e.Entry.DestinationPath)} → {folder}（点此打开位置）", Forms.ToolTipIcon.Info,
                    entry: e.Entry);
            }
            else if (e.Entry.Status == "failed")
            {
                ShowBalloon("有文件未能整理", $"{Path.GetFileName(e.Entry.SourcePath)}：{e.Entry.Error}", Forms.ToolTipIcon.Warning, true,
                    entry: e.Entry);
            }
        });
    }

    private void RefreshStatus()
    {
        _pauseItem.Text = _organizer.IsPaused ? "恢复自动整理" : "暂停自动整理";
        _tray.Text = _organizer.IsPaused ? "DownTidy（已暂停）" : "DownTidy（正在监控下载目录）";
        _mainWindow.RefreshStatus();
    }

    private void RefreshOperations() => _mainWindow.RefreshOperations();

    private void ShowBalloon(string title, string text, Forms.ToolTipIcon icon, bool force = false,
        OperationEntry? entry = null, string? fallbackFolder = null)
    {
        if (!force && (DateTime.Now - _lastBalloon).TotalSeconds < 2) return;
        _lastBalloon = DateTime.Now;
        _balloonEntry = entry;
        _balloonFallbackFolder = fallbackFolder;
        if (text.Length > 240) text = text[..237] + "...";
        _tray.ShowBalloonTip(3500, title, text, icon);
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开 DownTidy", null, (_, _) => ShowMainWindow());
        menu.Items.Add("打开下载目录", null, (_, _) => OpenDownloads());
        menu.Items.Add(new Forms.ToolStripSeparator());
        _pauseItem = new Forms.ToolStripMenuItem("暂停自动整理", null, (_, _) => TogglePause());
        menu.Items.Add(_pauseItem);
        menu.Items.Add("立即整理一次", null, async (_, _) => await OrganizeNowAsync());
        menu.Items.Add("撤销最近一次整理", null, async (_, _) => await UndoAsync());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("高级规则", null, (_, _) => OpenRules());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());

        _tray = new Forms.NotifyIcon
        {
            Text = "DownTidy",
            Visible = true,
            ContextMenuStrip = menu
        };
        try
        {
            using var iconStream = GetResourceStream(new Uri("pack://application:,,,/app.ico")).Stream;
            _tray.Icon = new System.Drawing.Icon(iconStream);
        }
        catch
        {
            _tray.Icon = System.Drawing.SystemIcons.Application;
        }
        _tray.DoubleClick += (_, _) => ShowMainWindow();
        _tray.BalloonTipClicked += (_, _) =>
        {
            var entry = _balloonEntry;
            if (entry != null) OpenEntryLocation(entry);
            else if (!string.IsNullOrEmpty(_balloonFallbackFolder)) OpenFolder(_balloonFallbackFolder);
        };
    }

    private static string GetDownloadsFolder()
    {
        try
        {
            const string key = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";
            const string name = "{374DE290-123F-4565-9164-39C4925E467B}";
            if (Microsoft.Win32.Registry.GetValue(key, name, null) is string value && !string.IsNullOrWhiteSpace(value))
                return Environment.ExpandEnvironmentVariables(value);
        }
        catch { }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }
}
