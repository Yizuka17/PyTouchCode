# 验收记录

版本 0.1.5.1。验证日期 2026-10-20，Windows 10 19045 x64，200% 缩放。

| 项目 | 结果 | 记录 |
| --- | --- | --- |
| Release x64 构建 | 0 warning，0 error | `artifacts/revision-0151-package-final.log` |
| 便携版应用测试 | 32 项通过 | `artifacts/revision-0151-ui.log` |
| MSIX 升级及开始菜单激活 | 32 项通过 | `artifacts/revision-0151-msix.log` |

新增检查左下角解释器位置、文件／调试的原生 NavigationView 切换、窄侧栏下刷新按钮边界，以及设置分类切换时保留暂存值、取消不保存。文件树长名称使用省略号。保留文件树动画中间帧及现有编辑、配色、字体、Python、终端和解释器验证。

实际渲染图包括 `native-settings-navigation.png` 和 `navigation-sidebar-debug.png`，保存在 `artifacts/msix-verified-*/`。本次未修改核心业务逻辑，27 项核心测试结果见 0.1.5 验收记录。

Pyboard 硬件连接、Surface 物理触摸和干净 Windows 11 仍待实机验证。
