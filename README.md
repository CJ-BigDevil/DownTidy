<div align="center">

<img src="icon256.png" width="120" alt="DownTidy 图标"/>

# DownTidy

**下载文件夹自动整理工具**

文件一下载完，自动归位到对应的子文件夹，让"刚下载的文件在哪"不再是个问题。

![License](https://img.shields.io/badge/license-MIT-green) ![Platform](https://img.shields.io/badge/platform-Windows%2010%2B-blue) ![.NET](https://img.shields.io/badge/.NET-8.0-purple) ![Privacy](https://img.shields.io/badge/privacy-100%25%20local-success)

[下载最新版](../../releases/latest) · [功能介绍](#功能) · [让 AI 帮你加规则](#让-ai-帮你加规则)

<!-- 建议在此处插入主界面截图：
<img src="docs/screenshot-main.png" width="640" alt="DownTidy 主界面"/>
-->

</div>

> DownTidy keeps your Downloads folder tidy — new files are automatically moved into categorized subfolders the moment they finish downloading. 100% local, no install needed.

## 为什么做这个

下载文件夹几乎是每个 Windows 用户最乱的地方：安装包、压缩包、文档、图片、视频全混在一起，想找刚下载的文件得按时间排序翻半天。手动整理不难，难的是坚持——整理三次没意思了，第四次就不想动了。

DownTidy 的思路是：**整理这件事不该由人做**。文件一下载完就自动归位，你只需要知道"它去了哪个文件夹"。

## 功能

- **开箱即用**：内置 13 条常见规则，安装包、压缩包、文档、图片、视频、音频、字幕、字体、证书等一装上就开始自动整理，不需要任何配置
- **只动下载完的文件**：正在下载的临时文件（`.crdownload` / `.part` 等）不会被碰，下载完才会归位
- **每次都有交代**：每次移动弹出气泡通知，点通知直接打开文件所在位置；主界面有完整的整理记录，双击也能定位
- **随时反悔**：一键撤销最近一次整理，撤销进行中有防连点保护
- **暂停开关**：不想让它动的时候，一键暂停（主界面或托盘菜单都行）
- **高级规则**：规则就是一个可编辑的 JSON，改完点「校验并应用」，校验失败不会覆盖现有规则；规则只能把文件移动到下载目录内的相对子目录，不可能误删或移出下载文件夹
- **让 AI 帮忙写规则**：内置提示词一键复制（可附上一句大白话需求），发给任意聊天 AI，把回复整段贴回高级规则即可——程序会自动挑出 JSON，校验通过才生效。全程不需要配置任何 API key
- **后台常驻**：关掉窗口后继续在托盘运行，右键托盘图标可暂停、整理、撤销或退出

## 使用

1. 到 [Releases](../../releases) 下载最新的 `DownTidy.exe`（单文件，无需安装，不写注册表）
2. 双击运行，窗口最小化或关闭后程序在托盘继续工作
3. 规则配置保存在 `%APPDATA%\DownTidy\rules.json`

系统要求：Windows 10 1809（17763）或以上。

## 让 AI 帮你加规则

1. 打开「高级规则」→「用 AI 帮忙写规则」，在框里用大白话写一句想怎么整理（比如"把截图和图片单独放一个文件夹"），点「复制全部」
2. 把提示词发给任意聊天 AI（网页版就行），AI 会返回一段完整 JSON
3. 回到「高级规则」，把 AI 的回复**整段**粘贴进来，点「校验并应用」

不需要配置任何 key，工具全程不联网；AI 写得不合适也没关系，校验通过并应用之前不会影响现有规则。

## 隐私

- 所有整理都在本机完成，**不联网、不上传任何数据**
- 规则只能把文件移动到下载目录内部的子文件夹，程序没有删除、重命名或移出下载目录的能力

## 构建

需要 .NET 8 SDK：

```bash
dotnet publish DownTidy.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

产物为单个 `DownTidy.exe`，可直接分发。

## License

[MIT](LICENSE)
