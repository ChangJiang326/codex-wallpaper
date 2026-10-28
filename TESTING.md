# 验证记录

2026-10-29，在当前 Windows x64 电脑完成验证。

- 16 项自动检查通过：横屏/竖屏留边、等比裁切、中文路径和文件名、重复目录、损坏 JSON、跳过 application 壁纸、自动与手动画面比例、预览路径边界、命令行引用、实际 Win32 透明度设置与读回、原样恢复、DWM 合成、进程身份校验、Windows 真实视频宽高读取（本机视频为 16:9）。
- 本机真实壁纸库扫描得到 38 张支持的壁纸，搜索和封面显示可用；自动检测到 Wallpaper Engine 安装路径及 Codex / ChatGPT 客户端窗口。
- 使用真实 Wallpaper Engine 的 `Alone` scene 壁纸完成原生渲染窗口启动、调整测试目标窗口大小、最小化/恢复和透明度复原检查。
- 使用 `2AM Cyberpunk High Rise Apartment` video 壁纸，在独立测试窗口中目视确认实时合成画面、完整显示时的等比留边与半透明文字效果。

测试窗口用于验证合成机制；不是替代的聊天客户端。软件本身会绑定界面中选择的真实 Codex / ChatGPT 窗口。

未覆盖：真实 Codex 窗口上的最终视觉效果（桌面自动化输入未成功触发应用按钮）、另一台实体电脑、所有 scene/web 壁纸、远程桌面、多显卡、多显示器混合 DPI、全部虚拟桌面切换场景和所有客户端版本。独立恢复进程已实现，但极端系统崩溃无法保证即时恢复，重新启动客户端可复原。

重跑：`tests/run.ps1` 执行 15 项通用检查，`tests/run.ps1 -Local` 额外检查本机视频元数据；Wallpaper Engine 已运行时可执行 `tests/live.ps1` 或 `tests/live.ps1 -Video`。实机测试应在选择器未应用壁纸时单独运行，约 65 秒后自动结束，结果写入 `tests/live-results.txt`。
