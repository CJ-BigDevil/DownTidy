using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace DownTidy;

public sealed class OrganizerService : IDisposable
{
    private static readonly HashSet<string> TemporaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".crdownload", ".part", ".partial", ".tmp", ".download", ".opdownload", ".!ut"
    };

    private readonly RuleService _ruleService;
    private readonly string _logPath;
    private readonly ConcurrentDictionary<string, PendingFile> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _logSync = new();
    private readonly object _moveSync = new();
    private readonly FileSystemWatcher _watcher;
    private readonly System.Threading.Timer _timer;
    private volatile bool _paused;
    private bool _disposed;

    public OrganizerService(RuleService ruleService, string downloadRoot, string dataDirectory)
    {
        _ruleService = ruleService;
        DownloadRoot = Path.GetFullPath(downloadRoot);
        Directory.CreateDirectory(DownloadRoot);
        Directory.CreateDirectory(dataDirectory);
        _logPath = Path.Combine(dataDirectory, "operations.jsonl");

        _watcher = new FileSystemWatcher(DownloadRoot)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            InternalBufferSize = 64 * 1024,
            EnableRaisingEvents = false
        };
        _watcher.Created += (_, e) => Track(e.FullPath);
        _watcher.Changed += (_, e) => Track(e.FullPath);
        _watcher.Renamed += (_, e) => Track(e.FullPath);
        _watcher.Error += (_, _) => StatusChanged?.Invoke(this, EventArgs.Empty);
        _timer = new System.Threading.Timer(_ => ProcessPending(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public string DownloadRoot { get; }
    public bool IsPaused => _paused;
    public event EventHandler<OperationEventArgs>? OperationCompleted;
    public event EventHandler? StatusChanged;

    public void Start()
    {
        ThrowIfDisposed();
        _watcher.EnableRaisingEvents = true;
        _timer.Change(1500, 1500);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePause()
    {
        _paused = !_paused;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Track(string path)
    {
        if (_paused || _disposed || !IsRootFile(path) || IsTemporary(path)) return;
        _pending.AddOrUpdate(path,
            _ => new PendingFile(),
            (_, existing) =>
            {
                existing.LastEventUtc = DateTime.UtcNow;
                existing.StableSinceUtc = default;
                return existing;
            });
    }

    private void ProcessPending()
    {
        if (_paused || _disposed) return;
        var now = DateTime.UtcNow;

        foreach (var pair in _pending.ToArray())
        {
            var path = pair.Key;
            var pending = pair.Value;
            if (now - pending.LastEventUtc < TimeSpan.FromSeconds(3)) continue;
            if (!File.Exists(path))
            {
                _pending.TryRemove(path, out _);
                continue;
            }

            long length;
            DateTime lastWrite;
            try
            {
                var info = new FileInfo(path);
                length = info.Length;
                lastWrite = info.LastWriteTimeUtc;
            }
            catch { continue; }

            if (pending.Length != length || pending.LastWriteUtc != lastWrite)
            {
                pending.Length = length;
                pending.LastWriteUtc = lastWrite;
                pending.StableSinceUtc = default;
                continue;
            }

            if (IsFileLocked(path))
            {
                pending.StableSinceUtc = default;
                pending.Attempts++;
                if (pending.Attempts > 40) _pending.TryRemove(path, out _);
                continue;
            }

            if (pending.StableSinceUtc == default)
            {
                pending.StableSinceUtc = now;
                continue;
            }
            if (now - pending.StableSinceUtc < TimeSpan.FromSeconds(2)) continue;

            if (_pending.TryRemove(path, out _))
                MoveFile(path, Guid.NewGuid().ToString("N"), false);
        }
    }

    public Task<OrganizeSummary> OrganizeNowAsync()
    {
        ThrowIfDisposed();
        return Task.Run(() =>
        {
            var summary = new OrganizeSummary { BatchId = Guid.NewGuid().ToString("N") };
            foreach (var path in Directory.EnumerateFiles(DownloadRoot, "*", SearchOption.TopDirectoryOnly).ToList())
            {
                if (IsTemporary(path) || IsFileLocked(path))
                {
                    summary.Skipped++;
                    continue;
                }

                var result = MoveFile(path, summary.BatchId, true);
                if (result == MoveResult.Moved) summary.Moved++;
                else if (result == MoveResult.Failed) summary.Failed++;
                else summary.Skipped++;
            }
            return summary;
        });
    }

    private MoveResult MoveFile(string sourcePath, string batchId, bool isManualScan)
    {
        lock (_moveSync)
        {
            if (!File.Exists(sourcePath) || !IsRootFile(sourcePath) || IsTemporary(sourcePath)) return MoveResult.Skipped;
            var extension = Path.GetExtension(sourcePath);
            var rule = _ruleService.Current.FirstOrDefault(r => r.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
            if (rule == null) return MoveResult.Skipped;

            var destinationDirectory = Path.GetFullPath(Path.Combine(DownloadRoot, rule.TargetFolder));
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
            if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase)) return MoveResult.Skipped;

            try
            {
                Directory.CreateDirectory(destinationDirectory);
                destinationPath = GetUniqueDestination(destinationPath);
                File.Move(sourcePath, destinationPath);
                var entry = NewEntry(batchId, sourcePath, destinationPath, "moved", "");
                AppendLog(entry);
                OperationCompleted?.Invoke(this, new OperationEventArgs { Entry = entry, IsManualScan = isManualScan });
                return MoveResult.Moved;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                var entry = NewEntry(batchId, sourcePath, destinationPath, "failed", ex.Message);
                AppendLog(entry);
                OperationCompleted?.Invoke(this, new OperationEventArgs { Entry = entry, IsManualScan = isManualScan });
                return MoveResult.Failed;
            }
        }
    }

    public Task<UndoSummary> UndoLastAsync()
    {
        ThrowIfDisposed();
        return Task.Run(() =>
        {
            lock (_moveSync)
            {
                var entries = ReadAllEntries();
                var undoneIds = entries.Where(e => e.Status == "undone").Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var last = entries.LastOrDefault(e => e.Status == "moved" && !undoneIds.Contains(e.Id));
                if (last == null) return new UndoSummary { Message = "没有可撤销的整理记录。" };

                var batch = entries.Where(e => e.Status == "moved" && e.BatchId == last.BatchId && !undoneIds.Contains(e.Id))
                    .OrderByDescending(e => e.Timestamp).ToList();
                var result = new UndoSummary();

                foreach (var entry in batch)
                {
                    try
                    {
                        if (!File.Exists(entry.DestinationPath)) throw new FileNotFoundException("目标位置的文件已不存在。", entry.DestinationPath);
                        if (File.Exists(entry.SourcePath)) throw new IOException("原位置已有同名文件，未覆盖。请先移开原位置文件。 ");
                        Directory.CreateDirectory(Path.GetDirectoryName(entry.SourcePath)!);
                        File.Move(entry.DestinationPath, entry.SourcePath);
                        AppendLog(new OperationEntry
                        {
                            Id = entry.Id,
                            BatchId = entry.BatchId,
                            Timestamp = DateTime.Now,
                            SourcePath = entry.SourcePath,
                            DestinationPath = entry.DestinationPath,
                            Status = "undone"
                        });
                        result.Restored++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                    {
                        result.Failed++;
                        result.Message += $"{Path.GetFileName(entry.DestinationPath)}：{ex.Message}\n";
                    }
                }

                result.Message = result.Failed == 0
                    ? $"已恢复 {result.Restored} 个文件。"
                    : $"已恢复 {result.Restored} 个，{result.Failed} 个未能恢复。\n{result.Message}";
                StatusChanged?.Invoke(this, EventArgs.Empty);
                return result;
            }
        });
    }

    public IReadOnlyList<OperationEntry> GetRecent(int count = 100) =>
        ReadAllEntries().OrderByDescending(e => e.Timestamp).Take(count).ToList();

    private List<OperationEntry> ReadAllEntries()
    {
        lock (_logSync)
        {
            if (!File.Exists(_logPath)) return new List<OperationEntry>();
            var result = new List<OperationEntry>();
            foreach (var line in File.ReadLines(_logPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<OperationEntry>(line);
                    if (entry != null) result.Add(entry);
                }
                catch (JsonException) { }
            }
            return result;
        }
    }

    private void AppendLog(OperationEntry entry)
    {
        lock (_logSync)
            File.AppendAllText(_logPath, JsonSerializer.Serialize(entry) + Environment.NewLine, new UTF8Encoding(false));
    }

    private static OperationEntry NewEntry(string batchId, string source, string destination, string status, string error) => new()
    {
        BatchId = batchId,
        Timestamp = DateTime.Now,
        SourcePath = source,
        DestinationPath = destination,
        Status = status,
        Error = error
    };

    private string GetUniqueDestination(string destinationPath)
    {
        if (!File.Exists(destinationPath)) return destinationPath;
        var directory = Path.GetDirectoryName(destinationPath)!;
        var name = Path.GetFileNameWithoutExtension(destinationPath);
        var extension = Path.GetExtension(destinationPath);
        for (var i = 1; i < 10000; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
        throw new IOException("无法生成不重复的目标文件名。");
    }

    private bool IsRootFile(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            return string.Equals(directory?.TrimEnd(Path.DirectorySeparatorChar), DownloadRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool IsTemporary(string path) => TemporaryExtensions.Contains(Path.GetExtension(path));

    private static bool IsFileLocked(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(OrganizerService));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _timer.Dispose();
    }

    private enum MoveResult { Moved, Skipped, Failed }
}
