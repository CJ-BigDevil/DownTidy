using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DownTidy;

public sealed class RuleService
{
    private readonly string _downloadRoot;
    private readonly string _rulesPath;
    private readonly object _sync = new();
    private IReadOnlyList<RuleDefinition> _current = new List<RuleDefinition>();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public RuleService(string dataDirectory, string downloadRoot)
    {
        _downloadRoot = Path.GetFullPath(downloadRoot);
        Directory.CreateDirectory(dataDirectory);
        _rulesPath = Path.Combine(dataDirectory, "rules.json");
        _current = Load();
    }

    public IReadOnlyList<RuleDefinition> Current
    {
        get { lock (_sync) return _current.Select(r => r.Clone()).ToList(); }
    }

    public string DownloadRoot => _downloadRoot;
    public string CurrentJson => Serialize(Current);

    public IReadOnlyList<RuleDefinition> Validate(string json, out string error) =>
        ValidateAndNormalize(json, _downloadRoot, out error);

    public static List<RuleDefinition> CreateDefaults() => new()
    {
        Rule("安装包", "安装包", ".exe", ".msi", ".msix", ".apk", ".deb", ".dmg", ".iso"),
        Rule("压缩包", "压缩包", ".zip", ".rar", ".7z", ".tar", ".gz"),
        Rule("图片", "图片", ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg", ".bmp"),
        Rule("视频", "视频", ".mp4", ".mkv", ".mov", ".webm", ".avi"),
        Rule("文档", "文档", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md", ".epub", ".caj", ".nh", ".ofd"),
        Rule("音频", "音频", ".mp3", ".aac"),
        Rule("字幕", "字幕", ".srt"),
        Rule("字体", "字体", ".ttf"),
        Rule("网页", "网页", ".htm", ".html"),
        Rule("数据导出", "数据导出", ".csv"),
        Rule("数据库导出", "数据库导出", ".sql"),
        Rule("开发文件", "开发文件", ".log", ".php", ".asp", ".asar", ".jar", ".sh", ".yaml", ".yml", ".mermaid"),
        Rule("证书与密钥", "证书与密钥", ".cer", ".pem", ".keystore", ".mobileprovision")
    };

    private static RuleDefinition Rule(string name, string target, params string[] extensions) => new()
    {
        Name = name,
        TargetFolder = target,
        Extensions = extensions.ToList()
    };

    public static string Serialize(IEnumerable<RuleDefinition> rules) =>
        JsonSerializer.Serialize(new RuleDocument { Rules = rules.Select(r => r.Clone()).ToList() }, JsonOptions);

    private IReadOnlyList<RuleDefinition> Load()
    {
        if (!File.Exists(_rulesPath))
        {
            var defaults = CreateDefaults();
            File.WriteAllText(_rulesPath, Serialize(defaults), new UTF8Encoding(false));
            return defaults;
        }

        try
        {
            return ValidateAndNormalize(File.ReadAllText(_rulesPath), _downloadRoot, out _);
        }
        catch
        {
            var backup = _rulesPath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try { File.Copy(_rulesPath, backup, true); } catch { }
            var defaults = CreateDefaults();
            File.WriteAllText(_rulesPath, Serialize(defaults), new UTF8Encoding(false));
            return defaults;
        }
    }

    public void Save(string json)
    {
        var rules = ValidateAndNormalize(json, _downloadRoot, out var error);
        if (!string.IsNullOrEmpty(error)) throw new InvalidDataException(error);

        lock (_sync)
        {
            if (File.Exists(_rulesPath)) File.Copy(_rulesPath, _rulesPath + ".backup", true);
            var temp = _rulesPath + ".tmp";
            File.WriteAllText(temp, Serialize(rules), new UTF8Encoding(false));
            File.Move(temp, _rulesPath, true);
            _current = rules;
        }
    }

    public void RestoreDefaults() => Save(Serialize(CreateDefaults()));

    public string BuildAiPrompt() =>
        "请修改下面的 DownTidy 规则，只返回完整 JSON，不要返回 Markdown 或解释。\n" +
        "规则只能把文件移动到下载目录内的相对子目录；targetFolder 不能为空、不能为 .、不能包含 .. 或绝对路径。\n" +
        "只能修改 name、extensions、targetFolder，不允许删除、覆盖、重命名或其他文件动作。无法识别的扩展名会继续原地不动。\n\n" +
        CurrentJson;

    /// <summary>从 AI 回复等混合文本中提取最外层的 JSON 对象；找不到大括号时原样返回，交给校验报错。</summary>
    public static string ExtractJsonBlock(string raw)
    {
        var text = raw?.Trim() ?? "";
        int first = text.IndexOf('{');
        int last = text.LastIndexOf('}');
        return first >= 0 && last > first ? text.Substring(first, last - first + 1) : text;
    }

    public static IReadOnlyList<RuleDefinition> ValidateAndNormalize(string json, string downloadRoot, out string error)
    {
        error = "";
        try
        {
            var document = JsonSerializer.Deserialize<RuleDocument>(json, JsonOptions);
            if (document?.Rules == null) throw new InvalidDataException("JSON 顶层必须是包含 rules 数组的对象。");
            if (document.Rules.Count == 0) throw new InvalidDataException("至少需要保留一条规则；如不想自动整理，请使用暂停。 ");
            if (document.Rules.Count > 100) throw new InvalidDataException("规则数量不能超过 100 条。");

            var root = Path.GetFullPath(downloadRoot);
            var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var normalized = new List<RuleDefinition>();

            foreach (var source in document.Rules)
            {
                var name = source.Name?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("存在没有 name 的规则。");
                if (!seenNames.Add(name)) throw new InvalidDataException($"规则名称重复：{name}");

                var target = (source.TargetFolder ?? "").Trim().Replace('/', '\\');
                if (string.IsNullOrWhiteSpace(target) || target == ".")
                    throw new InvalidDataException($"规则“{name}”必须指定下载目录内的子文件夹，不能直接移回下载根目录。");
                if (Path.IsPathRooted(target) || target.Split('\\', StringSplitOptions.RemoveEmptyEntries).Any(p => p == ".."))
                    throw new InvalidDataException($"规则“{name}”的目标目录越界：{target}");
                if (target.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                    throw new InvalidDataException($"规则“{name}”的目标目录包含非法字符。");

                var fullTarget = Path.GetFullPath(Path.Combine(root, target));
                if (!fullTarget.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"规则“{name}”的目标目录必须位于下载目录内部。");

                if (source.Extensions == null || source.Extensions.Count == 0)
                    throw new InvalidDataException($"规则“{name}”没有扩展名。");

                var extensions = new List<string>();
                foreach (var raw in source.Extensions)
                {
                    var ext = (raw ?? "").Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(ext)) continue;
                    if (!ext.StartsWith('.')) ext = "." + ext;
                    if (ext.Length < 2 || ext.Any(c => Path.GetInvalidFileNameChars().Contains(c) || c == '\\' || c == '/'))
                        throw new InvalidDataException($"规则“{name}”包含无效扩展名：{raw}");
                    if (!extensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) extensions.Add(ext);
                }
                if (extensions.Count == 0) throw new InvalidDataException($"规则“{name}”没有有效扩展名。");

                normalized.Add(new RuleDefinition { Name = name, Extensions = extensions, TargetFolder = target });
            }

            return normalized;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            error = ex.Message;
            return Array.Empty<RuleDefinition>();
        }
    }

    public static string BuildDiff(IReadOnlyList<RuleDefinition> oldRules, IReadOnlyList<RuleDefinition> newRules)
    {
        var oldLines = oldRules.Select(Describe).ToList();
        var newLines = newRules.Select(Describe).ToList();
        var result = new List<string>();
        foreach (var line in oldLines.Where(l => !newLines.Contains(l, StringComparer.OrdinalIgnoreCase))) result.Add("- " + line);
        foreach (var line in newLines.Where(l => !oldLines.Contains(l, StringComparer.OrdinalIgnoreCase))) result.Add("+ " + line);
        return result.Count == 0 ? "规则没有变化。" : string.Join(Environment.NewLine + Environment.NewLine, result);
    }

    private static string Describe(RuleDefinition rule) =>
        $"{rule.Name}：{string.Join(", ", rule.Extensions)} → {rule.TargetFolder}";
}
