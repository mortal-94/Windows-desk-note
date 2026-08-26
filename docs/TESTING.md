# 测试报告

最近验证：2026-08-19，Windows `10.0.26200`，.NET SDK `10.0.301`。

## 自动化结果

执行命令：

```powershell
dotnet test WindowsDeskNote.sln --configuration Release --no-restore
```

结果：**29 通过，0 失败，0 跳过**，本机耗时约 0.3 秒。

覆盖范围：

| 区域 | 验证内容 |
|---|---|
| 快捷键 | 规范化、字母/功能键/命名键、重复修饰键、无安全修饰键、F12 保留键 |
| 搜索 | 向前/向后、循环、大小写、空查询和无结果 |
| 主文件 | UTF-8 无 BOM、精确往返、未变化跳过、旧内容备份、备份裁剪 |
| 设置 | JSON 往返、越界归一化、非法快捷键/颜色回退、损坏文件留档 |
| 自动保存 | 连续输入合并为最新快照、退出刷新取消等待并立即保存 |
| TODO 文本 | 普通行/空行规范化、完成状态、Markdown 迁移、混合换行、光标映射和标记切换 |
| 文档规模 | 超过 300 万字符的存储往返和完整性 |

完整 Release 构建同时以“警告即错误”通过，0 警告、0 错误。

## WPF 大文本探针

工具使用与正式界面相同的 WPF `TextBox` 设置，载入、布局并全文搜索生成的连续文本：

```powershell
dotnet run --project tools\DeskNote.PerformanceProbe\DeskNote.PerformanceProbe.csproj `
  --configuration Release --no-build -- 5
```

本机一次结果：

```json
{
  "requestedMiCharacters": 5,
  "characters": 5242902,
  "editorLoadAndLayoutMilliseconds": 130,
  "fullTextSearchMilliseconds": 0,
  "managedMemoryDeltaMiB": 10.55,
  "integrityPassed": true
}
```

这不是跨机器性能承诺；它用于发现控件或算法退化。搜索显示 `0 ms` 代表低于毫秒计时分辨率。

## 2026-08-11 可视化冒烟测试

| 场景 | 结果 | 观察 |
|---|---|---|
| 启动 | 通过 | 进程存活，可见笔记不作为普通可定位顶层窗口暴露，已进入桌面层 |
| 全局快捷键进入编辑 | 通过 | `Ctrl+Alt+N` 后窗口到前台，编辑工具栏和状态栏出现 |
| 全局快捷键退出编辑 | 通过 | 再次按下后工具栏消失，窗口离开普通应用前台并回到桌面层 |
| 普通应用遮挡 | 通过（2026-08-12 回归） | 笔记挂在 `Progman → SHELLDLL_DefView` 子树；Explorer/Codex 正常覆盖笔记相交区域，未使用 Topmost |
| 显示桌面的最小化机制 | 通过（2026-08-12 回归） | Shell `MinimizeAll` 确认普通顶层窗口已最小化；笔记父窗口仍为可见 `SHELLDLL_DefView`，保持 visible、未 DWM cloaked、未 Topmost；随后 `UndoMinimizeALL` 恢复 |
| 中文输入、搜索框和设置对话框 | 本轮未手工输入 | WPF 编译通过；搜索/配置核心由自动化测试覆盖，发布验收仍建议真人操作一次 IME |
| 主动重启 Explorer 后恢复 | 未执行 | 为避免干扰当前桌面会话未主动终止 Explorer；消息恢复路径已实现但需发布前验证 |
| 多显示器/DPI 切换 | 未执行 | 当前会话未改变显示配置；实现响应 `WM_DISPLAYCHANGE` |

## 2026-08-19 TODO 与末尾定位回归

| 场景 | 结果 | 验证方式 |
|---|---|---|
| 非空行自动带方框、空行无方框 | 通过 | `TodoTextServiceTests` 覆盖 CRLF/LF、空白行和已有状态 |
| 旧笔记和 Markdown 任务迁移 | 通过 | 自动化断言 `☐`/`☑` 结果；加载迁移走带备份的仓储路径 |
| 输入后光标映射 | 通过 | 自动化覆盖跨行插入前缀后的字符索引映射 |
| 实际启动、旧笔记迁移与备份 | 通过 | 发布程序进入桌面层，日志记录成功；`note.txt` 已规范化且迁移前文件进入 `backups` |
| 点击方框切换 | 代码完成、待人工复核 | 编辑器仅命中行首方框区域，切换一个标记字符并触发自动保存；GUI 控制授权超时，未虚报为已点击 |
| 再次启动唤出已有实例 | 通过（v0.2.2） | 命名事件跨进程激活；实际窗口已进入前台编辑状态并显示旧内容 |
| 编辑状态定位末尾 | 通过（v0.2.2） | GUI 截图确认滚动条在底部且最后一行可见；非空行显示方框、空行不显示 |

## 2026-08-25 长时间桌面层恢复修复

| 场景 | 结果 | 验证方式 |
|---|---|---|
| 原有数据保护 | 通过 | 修改前从真实桌面用户目录复制主笔记、设置、日志和最新 5 份备份到 `artifacts/recovery/20260825-longrun-fix`；主笔记 931 字节 |
| 健康状态成本 | 通过（代码审查） | 30 秒一次，仅执行窗口句柄、父层、可见性、DWM cloak、当前 `SHELLDLL_DefView` 和有限兄弟 Z 序检查；健康时不写文件、不调整窗口 |
| Shell/显示/主题/电源事件 | 已实现 | `TaskbarCreated`、`WM_DISPLAYCHANGE`、`WM_SETTINGCHANGE`、`WM_THEMECHANGED`、`WM_DWMCOMPOSITIONCHANGED` 和恢复电源广播均防抖后检查 |
| 异常恢复策略 | 已实现 | 仅异常或事件触发时调用 `TryEmbed`；2 秒间隔重试，连续三次失败后进入可见编辑状态，不让笔记静默失联 |
| 长时间实机运行 | 待观察 | 自动化无法压缩数小时/数天的 Explorer 生命周期；建议实际运行后查看日志中的“桌面层健康检查已恢复笔记显示” |

## 发布前人工检查清单

- [ ] 在目标 Windows 10 与 Windows 11 各验证一次桌面层和 `Win+D`；
- [ ] 使用目标中文输入法输入、选词、撤销/重做和跨应用粘贴；
- [ ] 修改快捷键并验证冲突提示和旧快捷键回滚；
- [ ] 拖动到每台显示器，修改 DPI 后检查位置和尺寸；
- [ ] 在测试机器重启 Explorer，确认 1 秒内重新进入桌面层；
- [ ] 人工制造只读数据目录，确认保存失败时应用取消退出并保留内存内容；
- [ ] 从备份恢复 `note.txt` 并用记事本确认 UTF-8 内容。

桌面层回归探针（会临时最小化全部普通窗口并自动恢复）：

```powershell
dotnet run --project tools\DeskNote.DesktopProbe\DeskNote.DesktopProbe.csproj `
  --configuration Release -- <DeskNote进程ID> --show-desktop-check
```

2026-08-12 本机结果：`PASS: desktop-layer invariants survived Minimize All.`
