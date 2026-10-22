# DeskNote

DeskNote 是一张常驻 Windows 桌面的单页 TODO 草稿纸。它只有一个连续文档：点击笔记或按全局快捷键进入编辑，点击笔记外部、再次按快捷键或 `Esc` 自动保存并回到桌面层。

当前版本：`0.2.6`。

从 [GitHub Releases](https://github.com/mortal-94/Windows-desk-note/releases/latest) 下载 Windows x64 便携版：

- `DeskNote-v0.2.6-win-x64.zip`：包含 .NET 运行时，解压后运行 `DeskNote.exe`；
- `DeskNote-v0.2.6-win-x64-framework-dependent.zip`：精简版，需要预先安装 .NET 10 Desktop Runtime x64；
- `SHA256SUMS.txt`：下载文件的 SHA-256 校验值。

升级前在旧版笔记中点击“退出”，确认保存成功后再启动新版。请完整解压 ZIP；笔记保存在用户数据目录，升级时保留该目录。发布包不包含任何个人笔记、设置或备份。

## 已实现功能

- 单一连续纯文本笔记；每个非空行自动成为 `☐`/`☑` TODO，空行不显示方框；
- 编辑状态下点击行首方框即可完成/取消完成，仍支持选择、剪切/复制/粘贴和撤销/重做；
- 桌面嵌入状态下点击笔记即可进入编辑；TODO 规范化等待中文 IME 提交后执行；
- 编辑窗口失去激活（例如点击桌面或其它应用）时先保存再嵌入桌面；设置对话框、右键菜单操作期间保持编辑，保存失败则保留编辑内容并提示；
- 启动、快捷键进入编辑以及返回桌面层时始终直接滚动到笔记末尾；
- Explorer 可见桌面图标层嵌入，不使用“始终置顶”模拟；普通窗口可遮挡，Shell“显示桌面”后仍保留；
- 默认 `Ctrl+Alt+N` 全局快捷键，支持在设置中修改；
- 再次启动 `DeskNote.exe` 会直接唤出已有实例，适合窗口被普通应用遮挡时恢复；
- `Ctrl+F` 全文搜索、上一个/下一个、大小写选项和循环查找；
- 停止输入后防抖自动保存，退出编辑状态和程序退出前强制保存；
- UTF-8 无 BOM 主文件、同目录原子替换、1–20 份可配置历史备份；
- 字体、字号、行距、背景、文字颜色、窗口不透明度设置；
- 无标题栏、无任务栏按钮、默认不进入 `Alt+Tab`，恢复窗口位置、大小和光标；
- 单实例运行；Explorer 重启和显示设置变化后按事件重新尝试嵌入，不做高频桌面轮询；
- 长时间运行时每 30 秒做一次低成本桌面层健康检查；休眠恢复、显示/主题/Shell 变化后自动修复，连续失败则回到可见编辑状态；

## 技术栈选择

本项目采用 **.NET 10 + WPF + 少量 Win32 P/Invoke**，不引入第三方运行时依赖。

.NET 10 在当前环境中是合适的选择：它是处于 Active 支持阶段的 LTS 版本，官方支持至 2028-11-14；本机已安装 `10.0.301` SDK 和 Windows Desktop Runtime。WPF 是 .NET 内置的 Windows 桌面 UI 框架，能直接使用成熟文本控件、中文输入法和窗口生命周期，同时通过 `RegisterHotKey`、`SetParent` 等 Win32 API 完成本产品特有的系统集成。

相比之下：

- Electron 会为一个低空闲占用的小型纯文本工具引入浏览器运行时和更高基线内存；
- WinUI 3 可以胜任普通界面，但本项目核心工作集中在经典 HWND/Explorer 互操作，WPF 的工程复杂度更低；
- .NET Framework 4.8 能实现同样能力，但缺少现代 .NET 的工具链、语言和部署体验。

参考：[.NET 支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy)、[WPF 概览](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)、[RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)、[SetParent](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent)。

## 使用

1. 启动 `DeskNote.exe`。笔记默认位于桌面层。
2. 按 `Ctrl+Alt+N` 进入编辑状态。窗口会定位到文档末尾；顶部区域可以拖动窗口，边缘可以调整尺寸。
3. 直接输入；非空行会自动带上 TODO 方框，点击方框切换完成状态，停止输入 2 秒后自动保存。
4. 按 `Ctrl+F` 搜索；按 `Esc` 先关闭搜索，再按一次退出编辑。
5. 点击笔记外部，或再按 `Ctrl+Alt+N` / `Esc`，立即保存并回到桌面层。切换到其它应用也会触发同样行为。
6. 编辑状态下点击“设置”修改外观、自动保存、备份数量或快捷键。

如果快捷键已被其他软件占用，DeskNote 会保持在可见编辑状态并提示更换，不会把一个无法唤回的窗口留在桌面层。

## 数据与恢复

默认数据目录：`%LOCALAPPDATA%\WindowsDeskNote\`

| 文件/目录 | 用途 |
|---|---|
| `note.txt` | UTF-8 无 BOM 的完整主笔记，可用记事本等直接打开 |
| `settings.json` | 独立设置文件 |
| `backups\note-*.txt` | 保存前的有限历史版本 |
| `desknote.log` | 启动、桌面层和异常诊断日志 |
| `settings.invalid-*.json` | 无法解析的设置原件，应用会安全回退默认设置 |

`note.txt` 仍是可直接阅读和编辑的文本：非空行以 `☐ `（未完成）或 `☑ `（已完成）开头，空行保持为空。首次打开旧版本笔记时，普通非空行会安全迁移为未完成项；常见的 `- [ ] ` / `- [x] ` 也会转换，原文件保留在备份目录中。

恢复时先退出 DeskNote，从 `backups` 选取需要的版本并复制为 `note.txt`。请不要在应用运行时同时用其他程序改写 `note.txt`。

## 开发与构建

要求：Windows 10/11、.NET 10 SDK。仓库的 `global.json` 指定 `10.0.301` 并允许同特性带更新版本。

```powershell
dotnet restore WindowsDeskNote.sln
dotnet build WindowsDeskNote.sln --configuration Release --no-restore
dotnet test WindowsDeskNote.sln --configuration Release --no-build
dotnet run --project src\DeskNote.App\DeskNote.App.csproj
```

一键测试并发布框架依赖版（不受 PowerShell 执行策略影响）：

```powershell
.\scripts\build.cmd
```

生成包含 .NET 运行时的自包含版：

```powershell
.\scripts\build.cmd Release true
```

也提供等价的 `scripts\build.ps1`；在允许执行本地 PowerShell 脚本的环境中可使用 `-SelfContained` 开关。

发布结果位于 `artifacts\publish\win-x64\`。

## 工程结构

```text
src/DeskNote.Core/               平台无关的 TODO 文本、设置、搜索、快捷键解析和安全存储
src/DeskNote.App/                WPF 界面、窗口状态机、Win32/Explorer 互操作
tests/DeskNote.Tests/            MSTest 自动化测试
tests/DeskNote.App.Tests/        隔离数据目录的 WPF STA 失焦与保存回归测试
tools/DeskNote.PerformanceProbe/ WPF 大文本载入与搜索探针
tools/DeskNote.DesktopProbe/     桌面父层、Z 序与“显示桌面”集成探针
scripts/build.cmd / build.ps1    还原、测试、发布入口
docs/                            架构、测试和需求追踪文档
```

进一步阅读：[架构说明](docs/ARCHITECTURE.md)、[测试报告](docs/TESTING.md)、[需求追踪](docs/REQUIREMENTS_TRACEABILITY.md)。

## 已知边界

- WPF `TextBox` 面向常见个人笔记规模，不承诺数 GB 专业大文件编辑；
- Explorer 的桌面窗口组织属于 Shell 实现细节，不是稳定的业务层 API。实现按可见 `SHELLDLL_DefView` 定位并验证父层，包含 `TaskbarCreated` 恢复，但仍应在每个目标 Windows 大版本上做发布前验证；
- 第一版不包含需求文档已排除的 Markdown、多笔记、云同步、附件、AI 和磁盘分页编辑。
