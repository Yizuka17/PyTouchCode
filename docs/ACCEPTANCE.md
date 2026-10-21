# 验收记录

版本 0.1.5.7。验证日期 2026-10-21，Windows 10 19045 x64，200% 缩放。

| 项目 | 结果 | 记录 |
| --- | --- | --- |
| Release x64 构建 | 0 warning，0 error | `artifacts/revision-0157-package.log` |
| 核心测试 | 28 项通过 | `artifacts/revision-0157-build.log` |
| 便携版应用及实机 Pyboard | 38 项通过 | `artifacts/revision-0157-portable.log` |
| MSIX 升级、默认缓存及实机 Pyboard | 38 项通过 | `artifacts/revision-0157-msix.log` |

COM5 实机为 PYBv1.1_CN / STM32F405RG，MicroPython 1.18。通过应用运行菜单执行 USB 盘 H: 上已保存的 main.py，确认首次运行持续执行、状态正确且未弹出错误。另用 RAM 测试程序验证重复运行不终止当前任务、停止后恢复 REPL、重新运行、无输出进程异常退出时显示退出代码、板卡异常保留 traceback，以及终端直接输入得到 42。测试前后 boot.py、main.py 的 SHA256 相同。

执行前将脚本复制到本机缓存，避免软重启影响板载 USB 盘。主动停止与异常退出分别处理，长时间运行时只保留最近的错误输出，设备操作记录在数据目录的 device.log。

Python 与设备终端的退出提示区分运行成功、运行失败和主动停止，并保留退出代码；常见异常附简短原因。应用测试通过真实成功、报错及停止进程验证提示，核心测试验证停止状态不会影响下一次运行。

保留默认缓存 Python 的 DLL 启动、语言服务诊断、原生串口扫描、编辑、终端、字体、配色与动画验证。硬件记录为 pyboard-hardware.json，默认缓存记录为 profile-runtime.json。

硬件测试仅在显式传入 --pyboard-test COM5 或 scripts/Test-MSIX.ps1 -PyboardPort COM5 时执行。检测到 Pyboard 的 USB 盘时会运行已保存的 main.py，再停止执行；不会保存或覆盖板上文件。普通界面测试和 CI 不会连接开发板。
