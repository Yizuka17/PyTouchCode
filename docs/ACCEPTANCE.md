# 验收记录

版本 0.1.5.4。验证日期 2026-10-21，Windows 10 19045 x64，200% 缩放。

| 项目 | 结果 | 记录 |
| --- | --- | --- |
| Release x64 构建 | 0 warning，0 error | `artifacts/revision-0154-package.log` |
| 核心测试 | 28 项通过 | `artifacts/revision-0154-build.log` |
| 便携版应用测试 | 37 项通过 | `artifacts/revision-0154-ui.log` |
| MSIX 升级、默认缓存及实机 Pyboard | 38 项通过 | `artifacts/revision-0154-msix.log` |

默认数据目录改为 MSIX LocalCache 的真实物理目录，继续读取原有配置与运行环境。此前保存的应用内部解释器路径会映射到同一缓存的真实路径，外部解释器路径保持原值。新增默认缓存 Python 的 DLL 启动及语言服务诊断请求检查，避免只验证工作区临时环境。

COM5 实机为 PYBv1.1_CN / STM32F405RG，MicroPython 1.18。安装版使用默认缓存 Python，通过 mpremote 查询板卡信息、在 RAM 运行本地测试脚本并得到 42，再经应用的设备 REPL 输入命令并得到 42。测试前后 boot.py、main.py 的 SHA256 相同。硬件记录保存在 `pyboard-hardware.json`，默认缓存记录为 `profile-runtime.json`。

硬件测试仅在显式传入 `scripts/Test-MSIX.ps1 -PyboardPort COM5` 时执行；普通界面测试和 CI 不会连接开发板。保留串口扫描、编辑、终端、字体、配色与动画验证。
