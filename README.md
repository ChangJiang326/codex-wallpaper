# Codex Wallpaper

Windows 10/11 x64 便携壁纸选择器。将本机 Wallpaper Engine 的壁纸作为 Codex / ChatGPT 桌面客户端的窗口背景。无需管理员权限，无需安装 Python、Node.js 或 .NET SDK，使用 Windows 自带的 .NET Framework 4.8。

这是社区独立项目，与 OpenAI、Valve 或 Wallpaper Engine 无隶属关系。

**[下载便携版](https://github.com/ChangJiang326/codex-wallpaper/releases)** · [验证范围](TESTING.md) · [MIT 许可证](LICENSE)

## 使用

1. 将 `CodexWallpaper-1.0.0-win-x64.zip` 复制到另一台电脑并解压。
2. 在那台电脑上先启动 Wallpaper Engine 和 Codex / ChatGPT 桌面客户端。
3. 双击 `CodexWallpaper.exe`。程序自动查找 Steam 安装位置、Steam 多库目录和本地壁纸。
4. 左侧选择壁纸，右侧选择客户端窗口，点击 **应用到 Codex**。
5. 调整壁纸可见度。默认 24%；数值越高，壁纸越明显、文字越淡。
6. **恢复原样**、**Ctrl+Alt+F10** 或退出软件均可恢复原窗口。最小化会收进系统托盘，双击托盘图标重新打开。

找不到引擎时，点“选择程序”选择 `wallpaper64.exe` 或 `wallpaper32.exe`。找不到壁纸时，点“添加壁纸目录”，选择 `steamapps/workshop/content/431960`、`projects/myprojects` 或一张壁纸的目录。它们必须已经下载到这台电脑。软件不复制、下载或分发壁纸。

## 显示方式与限制

- **完整显示**：等比留边，不拉伸；**铺满窗口**：等比裁切，不拉伸。
- 视频优先读取 Windows 媒体元数据中的原始宽高；不支持该视频格式时，以及 scene/web 壁纸，自动比例按预览图推断。有些创作者上传的是裁切封面，此时需手动选择真实比例。scene 壁纸由引擎按这个比例渲染，并非提取封面当作动态背景。
- 支持扫描 `scene`、`video`、`web` 项目，由 Wallpaper Engine 自身渲染。具体壁纸、插件、音频、交互兼容性仍取决于引擎；不加载 application 可执行程序类型的壁纸。
- 这是 Windows 窗口合成兼容模式：专用壁纸窗被放在客户端后面，客户端整体半透明。**文字、图片和按钮也会一起变淡，无法做到只透明背景、文字完全不变。** 不修改或注入 Codex 安装文件，也不要求调试端口。
- 不会切换桌面壁纸；专用壁纸可能播放它自己的音频，音频设置由 Wallpaper Engine 管理。不会发送影响全部桌面壁纸的全局暂停或静音命令。
- 壁纸由独立的屏幕外引擎窗口渲染，通过 Windows DWM 实时合成，跟随选中的一个客户端窗口。最小化/切换虚拟桌面时隐藏背景，恢复时继续显示。程序每约 80ms 同步窗口位置。
- 引擎的“其他应用最大化/全屏时暂停或停止”设置可能冻结壁纸。遇到冻结，请在 Wallpaper Engine 中检查性能/应用规则。本工具不改这些全局设置。
- 目标为普通 Windows 10/11 x64 桌面会话；远程桌面、显卡驱动、不同客户端版本可能影响 DWM 和透明窗口。ARM 原生、macOS、Linux 未支持。
- Windows 已禁用或删除 .NET Framework 4.8 的精简系统需要先恢复该系统组件。

## 恢复与本地数据

程序自带独立恢复进程：主界面崩溃或被结束时，恢复客户端透明度并关闭本工具打开的壁纸窗口。下次启动还会检查残留恢复记录。恢复前校验窗口所属进程和启动时间，避免误改重用的句柄。

若热键被其他软件占用，使用界面或托盘中的“恢复原样”。退出程序会自动恢复；全部进程被强行结束时，重新启动选择器即可恢复。也可在程序未运行时执行 `CodexWallpaper.exe --restore`。重启 Codex 本身也会重置临时透明度。

设置和诊断日志仅保存在 `%LOCALAPPDATA%/CodexWallpaper`，不联网、不上传。路径按每台电脑自动检测，不包含开发机器的固定盘符。复制软件后需要在新电脑重新选择自己的壁纸。EXE 未做商业代码签名。

## 源码与构建

在 Windows PowerShell 中运行 `./build.ps1`。编译器为系统自带的 .NET Framework C# 编译器，产物位于 `dist/CodexWallpaper`。开发源码采用 C# 5，无第三方运行库。

运行 `./tests/run.ps1` 检查库扫描、损坏项目、目录边界、等比布局、Windows 参数引用及本机 DWM/透明度恢复。

## 依据

- [Wallpaper Engine 官方命令行](https://help.wallpaperengine.io/en/functionality/cli.html)：`openWallpaper -playInWindow` 与按 `location` 关闭独立窗口。
- [Codex / ChatGPT 外观设置](https://learn.chatgpt.com/docs/reference/settings)：官方页面介绍主题颜色与字体；未提供动态壁纸接口。
- [Windows 窗口透明度](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setlayeredwindowattributes) 与 [DWM 实时缩略图](https://learn.microsoft.com/en-us/windows/win32/dwm/thumbnail-ovw)。
