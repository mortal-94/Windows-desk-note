# 需求追踪

对应《Windows 桌面单页速记板——需求草案 v0.1》。状态中的“已实现”表示代码与可执行构建已具备；Windows Shell 场景的发布前人工矩阵见测试报告。

| 需求 | 状态 | 实现位置/验证 |
|---|---|---|
| 单一连续笔记 | 已实现 | `MainWindow.NoteEditor`；无页面、标签或工作区模型 |
| 极简 TODO 文本 | 已实现/已测试 | WPF `TextBox` + `TodoTextService`；非空行 `☐`/`☑`，空行无标记，主内容仍是 UTF-8 文本 |
| 全局字体、字号、行距、背景、透明度 | 已实现 | `SettingsWindow`、`DeskNoteSettings`、`ApplySettings` |
| 固定窗口、内部滚动、无标题栏/任务栏/Alt+Tab | 已实现 | `MainWindow.xaml`、`WS_EX_TOOLWINDOW`、设置持久化 |
| 桌面层且非置顶 | 已实现/已集成验证 | `DesktopLayerService` 使用可见 `SHELLDLL_DefView` + `SetParent`；探针断言父层、可见性、DWM cloak 与非 Topmost |
| 可配置全局快捷键 | 已实现 | `HotkeyGesture`、`GlobalHotkeyService`、隐藏消息 HWND |
| 再次启动唤出已有实例 | 已实现/已验证 | 命名自动重置事件通知首个进程解除桌面嵌入并进入编辑状态 |
| 再按快捷键或 Esc 返回桌面 | 已实现 | `ToggleEditingModeAsync`、`ExitEditingModeAsync` |
| 每次显示直接定位末尾 | 已实现 | 启动、快捷键编辑、退出到桌面层及 Explorer 重嵌入统一调用末尾滚动；编辑进入时光标也置于末尾 |
| 点击 TODO 方框切换状态 | 已实现/核心已测试 | 编辑状态命中行首标记后切换 `☐`/`☑` 并自动保存；字符状态转换由 `TodoTextServiceTests` 覆盖 |
| 中文输入法、选择、剪贴板 | 控件支持 | 原生 WPF 文本控件；发布前需按目标 IME 人工复核 |
| 停止输入数秒后保存 | 已实现/已测试 | `DebouncedTextSaver` |
| 状态切换、退出前保存 | 已实现 | `FlushAsync` 和可取消退出流程 |
| 安全写入与少量历史备份 | 已实现/已测试 | `AtomicFileWriter`、`NoteRepository` |
| 历史查看、任意编辑、撤销/重做 | 已实现 | `TextBox` 原生交互与上下文菜单 |
| Ctrl+F、上/下一个、大小写、跳转 | 已实现/已测试 | `TextSearchService` 与搜索面板 |
| 主内容 UTF-8、设置独立、可恢复 | 已实现/已测试 | `%LOCALAPPDATA%\WindowsDeskNote` 数据布局 |
| 空闲 CPU 接近零、无高频轮询/全局钩子 | 已实现 | `RegisterHotKey` 和 Shell/显示/主题/电源事件驱动；30 秒一次低成本健康检查，异常时才重嵌入 |
| 自动保存不阻塞输入 | 已实现/已测试 | UI 仅提交字符串快照，异步串行文件写入 |
| 常见个人规模流畅 | 已验证探针 | 5,242,902 字符载入布局 130 ms，本机完整性通过 |
| Win+D / Shell 显示桌面 | 已实现/已验证同机制 | Shell `MinimizeAll` 已确认普通窗口最小化后笔记保持可见、未 cloaked、父层不变；应用控制安全限制不允许自动发送 Windows 键组合 |
| Explorer 重启恢复 | 已实现/待人工矩阵 | 隐藏顶层消息 HWND 接收 `TaskbarCreated`，延迟一次重新嵌入 |
| 数 GB 专业大文件 | 按需求不做 | README 明确边界，无分页/虚拟文本结构 |
| Markdown、多笔记、云、附件、AI 等 | 按需求不做 | 未引入相关模型或依赖 |
