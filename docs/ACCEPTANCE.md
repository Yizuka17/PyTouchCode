# 验收记录

版本 0.1.4，MSIX 0.1.4.1。验证日期 2026-10-20，Windows 10 19045 x64，200% 缩放。

| 项目 | 结果 | 记录 |
| --- | --- | --- |
| Release x64 构建 | 0 warning，0 error | `artifacts/revision-014-package-final.log` |
| 核心测试 | 22 项通过 | `artifacts/revision-014-core.log` |
| 便携版应用测试 | 28 项通过 | `artifacts/revision-014-ui-final.log` |
| MSIX 升级及开始菜单激活 | 28 项通过 | `artifacts/revision-014-msix-final.log` |
| MSIX 签名 | 签名有效，导出证书与签名证书一致 | `artifacts/package-info.json` |

新增验证覆盖文件夹菜单显示、新建文件和文件夹、同名及非法名称拒绝、文件和目录重命名、打开文档及未保存内容保留、根工作区与最近路径更新、空白处触摸定位、斜向滚动时保持选区、旧配置及运行环境迁移。调试测试使用相对解释器路径，验证断点、局部变量和单步执行。

原有验证覆盖配色、字体列表及预览、图标加载、嵌套文件夹展开、保存、编码、撤销、补全、终端中文输入、PowerShell 和 Python REPL。实际控件渲染保存在 `artifacts/msix-verified-*/`。

Surface 物理触摸、真实 Pyboard、干净 Windows 11 和物理键盘 IME 尚待实机验证。当前终端无完整 ConPTY 屏幕仿真。
