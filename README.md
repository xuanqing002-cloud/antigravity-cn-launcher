# Antigravity 一键汉化工具 (Antigravity CN Launcher)

基于 Windows 原生 `.NET Framework 4.x` 构建的轻量级、零依赖一键汉化启动器。双击运行即可自动同步 GitHub 开源汉化包，完成 Antigravity 客户端的中文界面部署。

## 功能特性

- **双击即用，开箱自动执行**：打开程序后自动检测本机 Antigravity 安装目录与汉化状态，2 秒倒计时后自动开始一键汉化（倒计时期间点击任意选项或“取消自动”即可中断）。
- **零依赖自动运行**：无需预先安装 Node.js 或 Python；若系统缺少 Node.js，首次运行将自动从官方源（或国内镜像）下载并缓存便携版 Node.js v22 LTS 至 `%LOCALAPPDATA%\AntigravityCN\runtime\node.exe`。
- **自动同步 GitHub 最新汉化包**：每次运行自动比对上游仓库最新提交记录，有更新时自动拉取并缓存至 `%LOCALAPPDATA%\AntigravityCN\repo\`，断网环境下自动回退使用本地缓存。
- **灵活定制与无损还原**：支持切换 **简体中文 / 繁體中文**、自定义左上角品牌显示方式（保留 `Antigravity` / 隐藏 / 中文品牌名），并支持 **一键还原官方英文原版**。
- **自动进程管理**：自动关闭运行中的客户端、完成注入后按需自动重新启动。

## 快速使用

1. 前往本仓库右侧 **[Releases](../../releases)** 页面，下载最新的可执行文件（例如 `Antigravity-CN-Launcher-v1.0.0.exe`）。
2. 将程序放置在桌面或任意目录，**双击运行**即可自动完成汉化。
3. 如后续 Antigravity 官方自动更新覆盖了汉化，只需再次双击运行本程序即可瞬间恢复中文。

## 项目结构

- `Program.cs` — 工具主程序（WinForms 图形界面 + CLI 模式 + 下载/缓存/引擎调度）
- `make_icon.ps1` / `app.ico` — 程序多分辨率图标生成脚本与图标文件
- `build.ps1` — 使用 Windows 内置 .NET Framework 4.x `csc.exe` 编译单文件 `.exe`
- `LICENSE` — 本启动器项目的 MIT 开源协议
- `.gitignore` — Git 忽略规则

## 从源码编译

Windows 10 / 11 自带 `.NET Framework 4.x` 编译器，无需额外安装 Visual Studio 或 .NET SDK：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

编译产物将输出至 `dist\Antigravity一键汉化.exe`。

## 命令行参数（可选）

- `--no-auto`：打开界面时不自动倒计时开始
- `--cli-install` / `--cli-restore`：无界面静默执行汉化或还原
- `--install-dir <路径>`：手动指定 Antigravity 安装目录
- `--tw`：使用繁體中文字典
- `--brand-title <english|hidden|translated>`：左上角品牌名显示模式
- `--no-kill`：不自动结束运行中的 `Antigravity.exe`
- `--no-launch`：汉化完成后不自动启动软件
- `--log <文件>`：在 CLI 模式下将 UTF-8 日志同步写入指定文件

## 上游项目致谢与许可说明

- 本仓库自身源码（`Program.cs`、`build.ps1`、`make_icon.ps1`、`app.ico` 等）为独立编写的 Windows 图形化启动器与调度程序，采用 **MIT License** 开源。
- **本仓库不包含也不二次分发任何第三方代码、汉化词典或 Node.js 二进制文件。**
- 汉化核心注入引擎与词典数据均在用户本地运行时直接从 **[qqxpee/antigravity2-cn](https://github.com/qqxpee/antigravity2-cn)** 官方 GitHub 仓库实时获取；Node.js 运行时从 **[nodejs.org](https://nodejs.org/)**（或镜像源）获取。上游汉化引擎与词典的版权均归原作者所有，感谢 `qqxpee` 及社区贡献者的辛勤维护。若上游作者对作为默认下载源有任何异议，欢迎通过 Issue 联系，我们将第一时间配合移除或调整。

## 免责声明

- 本项目为非官方开源社区工具，与 **Google** 或 **Antigravity** 官方无任何关联。
- 本工具仅用于个人学习、研究与使用便利，使用过程中对本地软件资源文件的修改及由此产生的任何风险均由使用者自行承担。
