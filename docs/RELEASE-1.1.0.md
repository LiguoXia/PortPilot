PortPilot 1.1.0 新增 macOS Intel 与 Apple Silicon 原生应用，保留 Windows x64 版本。

## 下载

- **Apple Silicon（M 系列）**：`PortPilot-osx-arm64.dmg` 或 `PortPilot-osx-arm64.zip`
- **Intel Mac**：`PortPilot-osx-x64.dmg` 或 `PortPilot-osx-x64.zip`
- **Windows x64**：`PortPilot-win-x64.zip` 或 `PortPilot.exe`

macOS 12+，所有发行包均自包含，无需额外安装 .NET。DMG 打开后拖入 Applications。

## macOS 功能

TCP/UDP IPv4/IPv6 端点扫描、端口/PID/名称/路径搜索、进程详情与资源监控、收藏和备注、操作历史、CSV/JSON/TXT 导出、系统/浅色/深色主题、自动刷新，以及带身份校验与确认的 SIGTERM、SIGKILL、进程树操作。搜索、持久化和导出逻辑与 Windows 共用。

Mac 配置保存于 `~/Library/Application Support/PortPilot/data/`。普通权限下部分系统信息不可见，空结果不能保证端口未被占用。root、系统关键进程及 PortPilot 自身禁止结束。

**签名说明：** Mac 包使用 ad-hoc 签名，尚未通过 Apple Developer ID 签名和公证。首次运行可能需要在“系统设置 → 隐私与安全性”选择“仍要打开”；请先核对来源和 SHA256，不必关闭系统安全功能。Windows EXE 同样未使用商业代码签名。

构建流程在 Intel 与 ARM64 Mac runner 上执行集成测试与 `.app` 启动测试，同时运行 Windows 回归测试。详情、平台差异与源码构建见 [macOS 文档](https://github.com/LiguoXia/PortPilot/blob/main/docs/MACOS.md)。
