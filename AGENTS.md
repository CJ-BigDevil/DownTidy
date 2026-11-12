# AI 助手指南（AGENTS.md）

> 这份文件是给**所有 AI 编程助手**（Claude、GitHub Copilot、Cursor、Kimi 等）看的通用指南。
> 如果用户不会用、想改功能、或构建遇到问题，**请先读完这份，再一步步教用户操作**。
> 用户可能会把整个项目文件夹丢给你，并说一句"教我用"或"帮我改 XXX"。

---

## 1. 这是个什么项目

一个 **Windows 下载文件夹自动整理工具**：文件一下载完，自动移动到下载目录内对应的分类子文件夹（安装包、压缩包、文档、图片、视频等）。

- WPF 桌面应用，.NET 8（`net8.0-windows10.0.17763.0`），**零第三方 NuGet 依赖**
- 中文用户，界面和注释都是中文
- 数据目录在 `%APPDATA%\DownTidy`：`rules.json`（规则）、`operations.jsonl`（整理记录日志）
- 下载目录通过注册表 `User Shell Folders` 读取，兼容用户改过位置的 Downloads

---

## 2. 项目结构

```
DownTidy/
├── Program.cs           ← 入口：--self-test 分支 + 单实例 Mutex + 启动 App
├── App.cs               ← Application：组装各服务、托盘图标与菜单、气泡通知、暂停/撤销编排
├── MainWindow.cs        ← 主窗口（状态胶囊、监控目录、整理记录列表、按钮区）
├── RulesWindow.cs       ← 高级规则窗口（JSON 编辑器 + AI 提示词 + 校验并应用）
├── OrganizerService.cs  ← 核心：FileSystemWatcher 监控 + 稳定判定 + 移动 + 撤销 + JSONL 日志
├── RuleService.cs       ← 规则的读写/校验/默认值/AI 提示词/差异对比
├── Models.cs            ← 数据类型（RuleDefinition、OperationEntry、各种 Summary）
├── SelfTest.cs          ← 命令行自检（dotnet run -- --self-test）
├── UiTheme.cs           ← 全局样式（卡片、按钮、配色）
├── DownTidy.csproj      ← 项目文件（WPF + WinForms，PerMonitorV2）
├── app.ico / icon256.png← 图标（编译期嵌入 exe，运行时无需携带）
├── README.md            ← 给人看的说明
├── LICENSE              ← MIT
└── .gitignore
```

关键类职责：

- `Program` — 入口 `Main()`，含**单实例检测**（`Local\DownTidy.SingleInstance` 互斥锁）和 `--self-test` 命令行自检
- `App` — `OnStartup` 里创建 `RuleService` → `OrganizerService` → `MainWindow` → 托盘；气泡通知、暂停、撤销都在这里编排
- `OrganizerService` — 整理核心（见第 4 节）
- `RuleService` — 规则核心（见第 4 节）

---

## 3. 如何构建（最常问！）

需要 **.NET 8 SDK**（`dotnet --version` 确认 ≥ 8.0）：

```bash
# 开发调试
dotnet run

# 命令行自检（不弹界面，验证规则/移动/撤销逻辑）
dotnet run -- --self-test

# 发布单文件 exe
dotnet publish DownTidy.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

产物为单个 `DownTidy.exe`（约 170MB，自包含运行时），可直接分发，用户无需安装 .NET。

**构建的两个注意点：**
1. 这是现代 C#（net8.0），`ImplicitUsings` 关闭、`Nullable` 开启——新文件要显式写 using，注意可空警告。
2. 项目同时启用了 WPF 和 WinForms（托盘用 `System.Windows.Forms.NotifyIcon`）。两个命名空间有同名类（`Application`、`MessageBox` 等），代码里用 `Forms = System.Windows.Forms` 别名或全限定名区分，**新增引用时注意消歧义**。

发布正式版本：在 GitHub 仓库打 tag（如 `v1.0.0`）并创建 Release，把 publish 出的 `DownTidy.exe` 作为附件上传。工具铺网站（personal-tools）的下载链接指向 Release 附件，发布后记得更新版本号。

---

## 4. 各功能的实现位置（改功能先找到这里）

### OrganizerService.cs

| 功能 | 位置 | 说明 |
|------|------|------|
| 监控下载目录 | 构造函数里的 `FileSystemWatcher` + `Start()` | 只监控根目录（`IncludeSubdirectories = false`），不递归 |
| 临时文件排除 | `TemporaryExtensions` 集合 + `IsTemporary()` | `.crdownload/.part/.partial/.tmp/.download/.opdownload/.!ut`，下载中的文件不会被碰 |
| 文件稳定判定 | `ProcessPending()`（1.5 秒定时器） | 三段判定：静默 3 秒 → 大小/修改时间不再变化 → 稳定满 2 秒 → 才移动；文件被占用（`IsFileLocked`）会重置，超过 40 次放弃 |
| 移动文件 | `MoveFile()` | `_moveSync` 串行化；命中第一条匹配规则即停；同名自动加 ` (1)` 后缀（`GetUniqueDestination`） |
| 整理记录 | `AppendLog()` / `ReadAllEntries()` | JSONL 追加写 `%APPDATA%\DownTidy\operations.jsonl`，逐行容错反序列化 |
| 撤销 | `UndoLastAsync()` | 按**批次**撤销：找到最后一条 `moved`，把它所在 BatchId 的整批全部移回；已撤销的记录不再重复撤；原位置有同名文件时不覆盖、报失败 |

### RuleService.cs

| 功能 | 位置 | 说明 |
|------|------|------|
| 默认 13 条规则 | `CreateDefaults()` | 首次运行自动写入 `rules.json` |
| 规则校验 | `ValidateAndNormalize()` | 静态方法，UI 和保存共用；`targetFolder` 必须是下载目录内的相对路径（拒绝空、`.`、`..`、绝对路径、非法字符），扩展名自动补 `.` 并转小写去重 |
| 损坏恢复 | `Load()` 的 catch 分支 | `rules.json` 解析失败时备份为 `.broken-时间戳` 再回退默认规则 |
| 保存 | `Save()` | 先校验，失败抛 `InvalidDataException`；成功后旧文件备份为 `rules.json.backup`，临时文件原子替换 |
| AI 提示词 | `BuildAiPrompt()` | 把约束条件和当前 JSON 拼成提示词，供用户复制给任意聊天 AI |
| AI 回复提取 | `ExtractJsonBlock()` | 静态方法，取最外层 `{...}`，让 AI 前后多余的解释文字不影响粘贴 |
| 规则差异 | `BuildDiff()` | 应用前给用户看 `- 旧 / + 新` 的对比 |

### App.cs / MainWindow.cs / RulesWindow.cs

| 功能 | 位置 | 说明 |
|------|------|------|
| 气泡通知 | `App.ShowBalloon()` | 2 秒节流；`BalloonTipClicked` 点开文件位置（`/select,` 资源管理器定位） |
| 托盘菜单 | `App.CreateTray()` | 打开主窗口/下载目录、暂停、立即整理、撤销、高级规则、退出 |
| 防连点 | `App._organizeBusy` / `_undoBusy` | 整理和撤销进行中重复点击直接忽略 |
| 下载目录定位 | `App.GetDownloadsFolder()` | 读注册表 `User Shell Folders`，失败回退 `%USERPROFILE%\Downloads` |
| 主窗口 | `MainWindow.BuildUi()` | 纯代码建 UI（无 XAML），样式统一走 `UiTheme` |
| 规则编辑器 | `RulesWindow.BuildUi()` | JSON 文本框 + 「校验并应用」「用 AI 帮忙写规则」「恢复默认」 |

---

## 5. 常见"帮我改"需求的处理套路

**"想加一条默认规则 / 改默认归类"** → 改 `RuleService.CreateDefaults()`。注意：**已有用户的 `rules.json` 优先于代码默认值**，改默认值只影响新装用户；要让老用户生效，指导他们在「高级规则」里点「恢复默认」（会覆盖自定义，先提醒备份）或手动加一条。

**"想支持监控子文件夹"** → 改 `OrganizerService` 构造里的 `IncludeSubdirectories`，并同步改 `IsRootFile()` 的判定逻辑。注意评估：子文件夹本来就是整理结果的存放地，递归监控会把归位后的文件再次当成新文件，需要额外排除。

**"想改稳定等待时间"** → `ProcessPending()` 里的 `3 秒静默 + 2 秒稳定`，以及 `Start()` 里定时器 `1500ms` 间隔。调小更灵敏但更可能移动还在写入的文件，别只看注释。

**"想让某种浏览器下载临时文件也被整理"** → 不建议；正确做法是往 `TemporaryExtensions` 里**加**它的临时扩展名，让下载完（改名后）再被整理。

**"想改通知行为"** → `App.OnOperationCompleted()` 决定什么时候弹气泡；`ShowBalloon` 的节流和点击行为在 `App.cs`。

**"想加规则字段（比如按大小归类）"** → `Models.cs` 的 `RuleDefinition` 加字段 → `RuleService.ValidateAndNormalize()` 加校验 → `OrganizerService.MoveFile()` 的匹配逻辑 → `BuildAiPrompt()` 的约束说明 → `BuildDiff()` 的 `Describe()`。四处半都要同步，别漏。

---

## 6. 教用户使用时的话术要点

用户多半不懂技术，**用大白话、给具体步骤**：
1. 双击 exe → 主窗口显示"正在监控下载目录"，托盘出现图标
2. 之后什么都不用管：下载完的文件几秒后自动进对应文件夹，右下角弹通知，点通知直接打开文件位置
3. 整理错了 → 主窗口或托盘菜单点「撤销最近一次整理」
4. 不想它动 → 「暂停自动整理」，托盘菜单也能切
5. 想自定义规则 → 「高级规则」→「用 AI 帮忙写规则」，复制后发给任意聊天 AI，把回复贴回来点「校验并应用」

---

## 7. 故障排查速查

| 现象 | 原因 / 解决 |
|------|------------|
| 双击没反应 | 可能已在后台运行（单实例 Mutex 直接退出）；看托盘有没有图标 |
| 文件下载完很久没动 | 文件还被浏览器/杀软占用（`IsFileLocked` 会一直重置计时）；或扩展名没有命中任何规则（正常，原地不动是设计行为） |
| 下载中的文件被移动了 | 检查该浏览器的临时扩展名是否在 `TemporaryExtensions` 里，不在就加上 |
| 撤销提示"原位置已有同名文件" | 设计行为：撤销不覆盖原位置同名文件，先手动移开 |
| 规则保存报错 | 报错文案来自 `ValidateAndNormalize()`，按提示改；常见是 targetFolder 写了绝对路径或 `..` |
| `rules.json` 变成了 `.broken-xxx` | 文件损坏被自动备份并回退默认规则；从备份里把自定义规则挑出来 |
| 自检失败 | `dotnet run -- --self-test` 看输出；它用临时目录做完整整理/撤销演练，能定位是规则还是移动逻辑的问题 |

---

## 8. 给 AI 的协作原则

- 用户是中文使用者，**全程用中文回复**
- 改完**必须重新构建**并把新 exe 给用户（命令见第 3 节）
- 改默认规则后，**不要删用户的 `%APPDATA%\DownTidy\rules.json`**；已有配置优先是设计行为
- 这个工具的安全底线：**只能移动、不能删除、不能移出下载目录**。任何新功能都不能突破这条线（校验逻辑在 `ValidateAndNormalize()`，别绕过它直接操作文件）
- 移动相关逻辑必须经 `_moveSync` 串行化，撤销依赖 JSONL 日志的批次结构，改日志格式要同步改撤销
- UI 全是代码写的（无 XAML），新窗口/控件沿用 `UiTheme` 的现有风格，别引入新配色体系
- 保持零第三方依赖（csproj 里没有 PackageReference），新需求优先用 .NET 自带能力解决
- 提交代码不打版本标签；发布正式版本才打 tag + GitHub Release（附件传 publish 出的单文件 exe）
