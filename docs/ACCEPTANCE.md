# 验收记录

版本 0.1.5，MSIX 0.1.5.0。验证日期 2026-10-20，Windows 10 19045 x64，200% 缩放。

| 项目 | 结果 | 记录 |
| --- | --- | --- |
| Release x64 构建 | 0 warning，0 error | `artifacts/revision-015-package-release.log` |
| 核心测试 | 27 项通过 | `artifacts/revision-015-core.log` |
| 便携版应用测试 | 31 项通过 | `artifacts/revision-015-ui-release.log` |
| MSIX 升级及开始菜单激活 | 31 项通过 | `artifacts/revision-015-msix.log` |
| 选中动画 | 实际渲染的中间帧拉长，结束帧恢复 | `artifacts/msix-verified-*/selection-animation.json` |

新增回归覆盖缺失 Python DLL 的候选环境、内置缓存修复、默认内置解释器及明确选择的记忆策略、退出时并发读取进程状态、终端输入、语言请求取消与重启、串口枚举和设备配置保存。打开解释器面板只列出内置环境，扫描通过用户操作启动。

界面测试检查 MicroPython / Pyboard 与串口入口、取消选择不改变当前环境，以及文件树指示条的主题色和伸缩动画。安装版的动画色块高度从 31 像素经过中间帧 55 像素，再恢复为 31 像素；原始帧保存在测试目录。

保留原有的文件操作、字体、配色、图标、编辑、补全、debugpy、Python 输入、Shell 和 REPL 验证。Pyboard 硬件连接、Surface 物理触摸和干净 Windows 11 仍待实机验证。
