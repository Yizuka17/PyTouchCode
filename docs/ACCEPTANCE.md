# 验收记录

版本 0.1.3，验证日期 2026-10-20。开发机为 Windows 10 19045、x64、200% 缩放。

| 项目 | 结果 | 记录 |
| --- | --- | --- |
| Release x64 构建 | 0 warning，0 error | `artifacts/revision-013-build.log` |
| 核心测试 | 20 项通过 | `artifacts/revision-013-core.log` |
| 应用内测试 | 26 项通过 | `artifacts/revision-013-ui.log` |
| 发布版本复验 | 原生界面与真实进程测试 | `artifacts/revision-013-published-ui.log` |
| 配色和字体预览 | 实际 WinUI 控件渲染 | `artifacts/ui-verified-*/palette-settings.png`、`font-settings.png` |
| 文件图标 | 全部随包 SVG 加载，文件夹展开与另存图标刷新 | `artifacts/ui-verified-*/file-icon-gallery.png` |
| MSIX 签名 | 开发证书，RSA 3072 / SHA256 | `artifacts/package-info.json` |

新增测试覆盖 16 套配色、编辑器与终端独立选择、旧配置兼容、自定义方案保存及无效颜色拒绝、本机字体列表、独立字重、行高、文件类型识别、文件夹展开和折叠、另存扩展名后的标签图标刷新。切换外观不改动代码和终端草稿。

原有测试覆盖保存、编码、外部修改保护、撤销、中文搜索、补全、断点、debugpy、终端输入及 PowerShell 回显。联网更新另有 2026-10-19 的 18 项集成测试记录。

尚未完成的验收：Surface 触控、真实 Pyboard、干净 Windows 11 的 MSIX 安装、完整窗口截图与物理键盘 IME 操作。当前终端无完整 ConPTY 屏幕仿真。桌面截图接口存在超时，本文的图片来自应用内控件渲染。
