# PyTouchCode

原生 Windows Python IDE，基于 WinUI 3 和 Scintilla。

[下载 v0.1.5.9](https://github.com/Yizuka17/PyTouchCode/releases/tag/v0.1.5.9)

- 默认使用内置 Python 3.13.16，包含 pip、Jedi、debugpy、mpremote、Ruff 和 Tcl/Tk。
- 其他 Python 环境和串口由用户手动扫描，解释器选择支持 MicroPython / Pyboard。
- 串口扫描直接读取 Windows 设备列表，无需启动 Python。
- 16 套配色，编辑器与终端分别设置深浅方案，支持自定义颜色。
- 界面、编辑器、终端分别选择本机字体、字号和字重；编辑器和终端支持行高设置。
- Mica、Mica Alt 和 Acrylic 材质支持透明度调节及预览。
- 文件树和标签页按类型显示图标，支持右键或长按新建、重命名文件和文件夹；选中项使用 WinUI 导航伸缩动画。
- 侧栏和设置分类使用原生 NavigationView，解释器入口位于左下角。
- 多标签编辑、补全、运行、调试、终端输入和 MicroPython 设备操作。
- 编辑器与终端支持单指纵向滚动、横向选字、双指全向滚动和捏合缩放；长行使用底部滚动条，折叠箭头及范围线跟随代码缩进。

支持 Windows 10 2004 及以上，x64。便携包解压后运行 `PyTouchCode.exe`。

MSIX 为开发证书签名。将 MSIX、`PyTouchCode-Development.cer` 和 `Install.ps1` 放在同一目录，以管理员身份运行安装脚本。证书有效期至 2043-10-19。

## 构建

安装 .NET SDK 9，在项目目录执行：

```powershell
./scripts/Build.ps1 -Configuration Release -Test
./scripts/Package.ps1
```

输出位于 `artifacts/publish-0.1.5`，便携包和 MSIX 位于 `artifacts`。运行环境由构建脚本获取。

## 测试

```powershell
dotnet run --project tests/PyTouchCode.Tests -c Release -- ./runtime/python/python.exe
./scripts/Test-UI.ps1
./scripts/Test-MSIX.ps1 -Package ./artifacts/PyTouchCode_0.1.5.9_x64.msix
```

[验收记录](docs/ACCEPTANCE.md) | [第三方许可](THIRD_PARTY_NOTICES.md)

当前终端使用管道输入输出，暂不支持完整 ConPTY 屏幕仿真。Pyboard 已通过 COM5 实测，Surface 触控仍待实机验证。
