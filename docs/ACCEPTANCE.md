# 验收记录

版本 0.1.5.2。验证日期 2026-10-20，Windows 10 19045 x64，200% 缩放。

| 项目 | 结果 | 记录 |
| --- | --- | --- |
| Release x64 构建 | 0 warning，0 error | `artifacts/revision-0152-package.log` |
| 核心测试 | 28 项通过 | `artifacts/revision-0152-build.log` |
| 便携版应用测试 | 34 项通过 | `artifacts/revision-0152-ui.log` |
| MSIX 升级及开始菜单激活 | 34 项通过 | `artifacts/revision-0152-msix.log` |

关闭根容器的自动快捷键提示，避免触摸操作弹出 Ctrl+N。快捷键注册和菜单提示文字保持可用。

新增检查菜单内部按钮的字体、字号、字重及主题切换；材质控制器透明度实时更新且内容不变淡；设置取消后恢复材质。透明度配置验证包含持久化、旧配置默认值、范围约束和非有限数值拒绝。保留原有编辑、终端、解释器、文件树和动画验证。

实际渲染图包括 `menu-font-*.png`、`native-settings-navigation.png` 和 `navigation-sidebar-debug.png`，保存在 `artifacts/msix-verified-*/`。本机支持的 Acrylic 已连接实际控制器；Mica 的壁纸混合效果仍需 Windows 11 实机确认。

Pyboard 硬件连接、Surface 物理触摸和干净 Windows 11 仍待实机验证。
