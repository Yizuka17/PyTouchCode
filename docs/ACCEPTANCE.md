# 验收记录

版本 0.1.5.3。验证日期 2026-10-21，Windows 10 19045 x64，200% 缩放。

| 项目 | 结果 | 记录 |
| --- | --- | --- |
| Release x64 构建 | 0 warning，0 error | `artifacts/revision-0153-package.log` |
| 核心测试 | 28 项通过 | `artifacts/revision-0153-build.log` |
| 便携版应用测试 | 36 项通过 | `artifacts/revision-0153-ui.log` |
| MSIX 升级及开始菜单激活 | 36 项通过 | `artifacts/revision-0153-msix.log` |

串口枚举改用 Windows SetupAPI 和系统串口映射，不启动 Python，也不打开串口。保留设备名称、硬件 ID、端口去重和排序。设备面板支持扫描取消，并统一使用结构化端口数据，避免从显示文字解析端口。

界面测试实际执行解释器入口的扫描按钮及设备面板的打开扫描，并把两个入口的 Python 路径设为不存在的文件，确认扫描不依赖解释器。扫描记录 `serial-discovery.json`，本机结果为 COM1、COM3、COM4、COM5；渲染图包括 `interpreter-micropython.png` 和 `device-serial-scan.png`。保留原有编辑、终端、配色、字体及动画检查。

本次未向开发板发送指令或写入文件。Surface 物理触摸和 Windows 11 Mica 效果仍待实机验证。
