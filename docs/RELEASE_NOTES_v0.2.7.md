# DeskNote v0.2.7

修复点击待办框后光标跳回原位置的问题。勾选或取消勾选后，光标留在该行方框后的正文起点，可以直接继续输入。

- **DeskNote-v0.2.7-win-x64.zip**：Windows x64 自包含便携版，完整解压后运行 `DeskNote.exe`。
- **DeskNote-v0.2.7-win-x64-framework-dependent.zip**：精简版，需预先安装 .NET 10 Desktop Runtime x64。
- **SHA256SUMS.txt**：两个 ZIP 的 SHA-256 校验值。

升级前请正常退出旧版，再运行新版。笔记、设置和备份保存在 `%LOCALAPPDATA%\WindowsDeskNote`，升级时请保留该目录。

格式检查通过，Release 构建零警告、零错误，35 项现有自动化测试通过。测试未覆盖真实鼠标点击和滚动，手动复核步骤见[测试报告](https://github.com/mortal-94/Windows-desk-note/blob/v0.2.7/docs/TESTING.md#发布前人工检查清单)。
