# MSIX 启动修复

0.1.3 的安装版在加载主窗口 XAML 时崩溃。安装包缺少以包标识命名的根资源索引，便携版测试未覆盖这个问题。

打包脚本现在分别发布便携版和安装版，为安装版生成 `resources.pri`，检查资源映射名称和 `App.xbf`、`MainWindow.xbf`。同时保留已有包标识与签名发布者，支持直接升级。文件树改用数据模板，修复展开子文件夹时的崩溃。

```powershell
./scripts/Package.ps1
./scripts/Test-MSIX.ps1 -Package ./artifacts/PyTouchCode_0.1.5.0_x64.msix
./scripts/Test-UI.ps1
```

`Test-MSIX.ps1` 安装后通过开始菜单使用的应用标识激活，运行实际 WinUI 窗口、编辑器和 Python 进程测试。报告保存在 `artifacts/msix-verified-*/ui-smoke.json`。启动异常写入 `%TEMP%\PyTouchCode-crash.log`。

资源索引要求参见 [Microsoft UI XAML 手动打包讨论](https://github.com/microsoft/microsoft-ui-xaml/discussions/9121)。
