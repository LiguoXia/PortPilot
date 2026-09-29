# PortPilot for macOS

PortPilot 1.1.0 新增 macOS 桌面应用，复用 Windows 版的模型、搜索规则、收藏/备注/历史 JSON 持久化与导出服务。Windows WPF 应用继续保留。

## 下载与安装

从 [GitHub Releases](https://github.com/LiguoXia/PortPilot/releases/latest) 下载对应芯片的安装包：

| Mac 芯片 | 安装包 | ZIP 便携分发包 |
| --- | --- | --- |
| Intel | `PortPilot-osx-x64.dmg` | `PortPilot-osx-x64.zip` |
| Apple Silicon（M 系列） | `PortPilot-osx-arm64.dmg` | `PortPilot-osx-arm64.zip` |

要求 macOS 12 或更高版本。两种包都包含 .NET 8 运行时；Apple Silicon 版为原生 ARM64，无需 Rosetta。双击 DMG 后将 PortPilot 拖入 Applications，或解压 ZIP 后移动 `PortPilot.app`。

当前发布包使用 **ad-hoc 本地签名，没有 Apple Developer ID 签名或公证**。首次从网络下载运行时可能被 Gatekeeper 拦截：确认下载来源与 SHA256 后，按 macOS 提示进入“系统设置 → 隐私与安全性 → 仍要打开”。受组织管理的 Mac 可能不允许此操作。不要关闭 Gatekeeper 或系统完整性保护（SIP）。验证下载文件可运行 `shasum -a 256 PortPilot-osx-arm64.dmg`，与同架构 `.sha256` 文件比较。

## 功能

- TCP / UDP、IPv4 / IPv6、监听状态、远程端点、端口与 PID 双向查询。
- 全字段及指定字段搜索，精确文本匹配，Enter 提交；自动刷新沿用已提交条件。
- 端口、进程表格排序、多选，进程 CPU / 内存、父 PID、程序路径、用户与启动时间。
- Inspector：启动命令、工作目录、当前网络端点和可见程序 / 映射文件。
- 收藏端口与进程名、编辑收藏名称和备注、独立端口备注、操作历史。
- 正常结束 SIGTERM、显式强制结束 SIGKILL、进程树二次确认；确认框列出所有受影响端口。
- 选中行或整个当前列表导出 CSV / JSON / TXT；复用 CSV 公式注入防护。
- System / Light / Dark 主题，1 / 2 / 3 / 5 / 10 / 30 秒刷新，⌘F 搜索、⌘R 刷新、Esc 收起详情。

数据保存在 `~/Library/Application Support/PortPilot/data/`。升级 / 替换 `.app` 不会覆盖配置。设置页可在 Finder 中打开数据目录。备份时复制整个 `data`；多个实例不应同时修改同一目录。

## 平台差异与权限

macOS 使用系统 `/usr/sbin/lsof -n -P -i -F0pftPnT` 的 NUL 分隔字段输出，避免依赖展示列宽、DNS 或端口服务名。进程元数据通过系统 `ps`、libproc 和 .NET 读取；不安装守护服务、不自动提权、不调用 shell 拼接用户输入。

系统权限、隐私控制和进程退出都可能造成不可见信息。收藏在未发现占用时显示 **Unknown**，不会将不完整快照宣称为 Free；空列表不保证端口可绑定。CPU 按逻辑处理器数量归一化，首帧为零，内存为 Working Set。

结束操作拒绝系统关键进程、root 进程、PortPilot 自身和无法确认启动时间的目标。发送信号前重新读取身份、所有者和可执行路径，校验 PID + 启动时间 + 路径；身份变化则取消。macOS 的 `kill(pid, signal)` 仍有校验与发送之间极小的系统级竞态窗口，没有 Windows 进程句柄的同等原子身份保证。SIGTERM 只是请求，程序可以忽略；不会自动升级为强杀。树操作只涉及确认时列出的目标。

Windows UAC、WinVerifyTrust、DLL、注册表、Windows 进程参数布局不适用于 Mac。此版本不检测目标程序的实际运行架构、不验证目标程序签名、不读取环境变量。进程关系以父 PID 展示，暂不提供 Windows 版的展开式进程树和命令面板；未承诺 Windows 内存测试数字同样适用于 Mac。

## 源码构建

在 Mac 安装 .NET 8 SDK 和 Xcode Command Line Tools 后运行：

```bash
dotnet test tests/PortPilot.Mac.Tests/PortPilot.Mac.Tests.csproj -c Release
bash build-macos.sh osx-arm64
# Intel：bash build-macos.sh osx-x64
```

输出在 `artifacts/macos/<rid>/`。脚本要求输出目录尚不存在，防止旧文件混入新签名。修改代码可直接 `dotnet run --project src/PortPilot.Mac/PortPilot.Mac.csproj`；Windows 上可以编译检查 UI，但实时扫描与打包需要 macOS。

GitHub Actions 使用 `macos-15-intel` 和 `macos-15` 分别构建并原生测试 x64 / ARM64：真实 TCP/UDP IPv4/IPv6、已建立连接、进程详情、PID 复用拒绝、测试子进程 SIGTERM/SIGKILL、包内 UI 启动、所有页面与主题，以及解压后的签名与启动检查。并行执行 Windows 回归测试。所有任务成功后，`v<版本号>` 标签触发 GitHub Release，附带两架构 DMG/ZIP、Windows EXE/ZIP 和 SHA256。

本次结果与验证边界见 [1.1.0 macOS 验证记录](VALIDATION-MACOS-1.1.0.md)。

参考：[Avalonia macOS 部署](https://docs.avaloniaui.net/docs/deployment/macos)、[GitHub runner 架构](https://github.com/actions/runner-images)、[Apple 首次打开应用说明](https://support.apple.com/102445)、[lsof 字段协议](https://github.com/lsof-org/lsof/blob/master/Lsof.8)。
