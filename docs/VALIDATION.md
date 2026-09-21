# PortPilot 1.0.0 验证记录

> 本文为 1.0.0 的历史验证记录。历史修复见 [1.0.1 搜索](SEARCH-1.0.1.md)、[1.0.2 回归](FIXES-1.0.2.md)、[1.0.3 图标](FIXES-1.0.3.md)。最新内存与响应验证见 [MEMORY-1.0.4.md](MEMORY-1.0.4.md)。`TestResults/tests.trx` 和界面截图由最新构建更新。

验证日期：2026-09-20。Windows 11 x64（系统版本 10.0.26200），.NET SDK 8.0.425。

## 构建与自动化测试

- `build.ps1` 完整执行 Clean → Restore → Build → Test → Publish。
- Release 构建：**0 warnings / 0 errors**。
- xUnit：**28 passed / 0 failed / 0 skipped**。原始报告：`TestResults/tests.trx`。
- 发布：win-x64、自包含、单文件，业务配置依据 `AppContext.BaseDirectory` 定位。
- 发布 EXE 约 155 MiB；无需散落的运行库 DLL。

测试覆盖：

1. 真实 TCP IPv4 / IPv6 listener 的端口、地址、状态、PID。
2. 真实 UDP IPv4 / IPv6 endpoint 的端口、地址、PID。
3. 本地真实 TCP established 连接的远程端口与所属 PID。
4. 当前进程的路径、启动时间、内存、启动命令、架构、权限标识。
5. 不存在 PID、已退出但内核对象仍被引用的进程、Access Denied。
6. 仅结束测试创建的 cmd.exe；测试框架持有目标引用并在 finally 清理。
7. 伪造旧启动时间时拒绝结束仍存活的测试进程。
8. 核心进程保护、批量 PID 去重、全部端口提示、树快照中的旧父 PID 排除。
9. JSON 原子写入、损坏配置备份恢复、设置归一化、收藏/备注、历史上限。
10. 中文和空格路径、中文工作目录及启动参数、PID/端口/localhost 搜索。
11. CSV 转义、防公式注入、JSON 导出。

## 发布版 UI 与便携性

发布 EXE 以 `--smoke-test` 模式真实创建 WPF 窗口，在 STA UI Dispatcher 上执行原生扫描，遍历所有页面并渲染 PNG。不是 HTML 设计稿或模拟数据。

- Dashboard、Ports、Processes、Connections、Favorites、History、Settings、Search、Process Tree、Inspector、Command Palette 均渲染完成。
- 真实打开临时 TCP 监听器，读取自身 PID 对应的端口与完整启动参数。
- 两次刷新后，行对象身份保持稳定；端点集合和 CollectionView **均无 Reset 事件**。
- 浅色、深色、900×600 最小尺寸均检查；125% / 150% / 175% / 200% 离屏 DPI 渲染完成。
- 复制发布 EXE 到 `.verification/便携 空格/`，从不同工作目录 `.tools` 启动，设置 DOTNET_ROOT 为不存在的路径，全部 UI 冒烟检查通过。
- 新目录 `data/config.json` 等文件成功创建；工作目录 `.tools/data` 未产生，证明业务路径不依赖当前工作目录。
- 原始 UI 结果：`docs/ui-smoke-results.txt`、`docs/portable-smoke-results.txt`。实际截图：`docs/screenshots/`。

## 性能实测与目标差距

工具：`tools/measure-performance.ps1`。默认 Dashboard，2 秒刷新，20 个逻辑处理器，采样 10 秒；当时系统约 500 多个进程、2500 多个网络端点。

| 指标 | 初版采样 | 原生资源采样优化后 |
| --- | ---: | ---: |
| 平均 CPU（按全机逻辑处理器归一化） | 0.89% | 0.54% |
| Working Set | 298.9 MiB | 280.7 MiB |
| Private memory | 229.7 MiB | 220.7 MiB |
| 启动到出现窗口 | 2301 ms | 2366 ms |
| UI Responding | true | true |

优化将常驻采样从大量 `System.Diagnostics.Process` 对象改为 Toolhelp + GetProcessTimes + K32GetProcessMemoryInfo + GetProcessHandleCount，减少分配与扫描开销。

**未达到启动约 1 秒和内存 150 MB 的期望目标。** 上述为当前开发机的短时间观测，不是长期压力测试或所有机器的保证。单文件首启、系统缓存、终端防护和当前进程数均会影响结果。原始采样在 `performance-before.json`、`performance.json`。

## 尚需人工验收 / 明确限制

- UAC 管理员操作流程已实现：提升前确认、提升后重新扫描真实目标并确认、句柄身份和 critical 状态复核、结果回传。**未在自动化中实际确认 Windows 安全桌面的 UAC 提示**。
- 自动测试验证了当前普通用户权限与受保护 PID 的 Access Denied；未穷举所有账户策略、PPL、防护软件或受限企业环境。
- DPI 已做离屏渲染；仍需多显示器实际移动、125%–200% 系统缩放及不同显卡的人工检查。
- 进程模块与环境名称依赖权限；环境变量值刻意不展示。工作目录与环境名称是尽力读取，环境内存并发变化会明确显示不可用。
- 未提取每个程序的独立图标，使用统一线性图标；没有商业代码签名、安装器或自动更新服务。
- 签名离线验证不查询吊销和目录签名。连接时长为首次观测时长，端口提示与风险提示不作确定性安全判断。

以上限制均在 README 和相应界面语义中说明。
