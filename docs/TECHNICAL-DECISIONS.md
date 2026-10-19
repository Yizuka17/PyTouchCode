# 技术路线与风险

## 原生编辑器选择

选择 WinUIEdit 0.0.5-prerelease：它是 Scintilla 的 WinUI 3 C++ / WinRT 控件，能够放入原生布局、支持原生文本编辑、撤销、选择、IME、折叠、补全菜单和 GPU 绘制。避免 Win32 HWND 子窗口的 airspace / DPI / 触控互操作成本，也避免自写普通文本框编辑器。

它仍为预发布组件，上游明确提示 API 可能变化。依赖锁定版本，对外操作集中在 `Editing.cs` / `AppearanceTouch.cs`，并通过真实控件测试约束升级。

重要验证结果：其 `HighlightingLanguage` 方便属性仅配置少数语言，不包含 Python。仅将属性设为 `python` 不会高亮。项目通过 DLL 导出的 `CreateLexer("python")` 创建原生 Lexilla ILexer5，然后使用 `SCI_SETILEXER` 交给 Scintilla 管理；原生测试验证 `def` 得到 Python keyword style 5。

原生 Scintilla 桌面组件也是可行备选，但直接托管 HWND 会增加 WinUI 布局和输入适配成本。自行实现 Win2D 编辑器需要重新解决文本布局、IME、无障碍和撤销，当前未采用。

## 触控

代码区触点在 8 DIP 方向阈值后锁定：横向为选择，纵向为滚动。原生 gutter 与底部滚动条保持可操作；横向拖动到边缘自动水平滚动。双指调整字体，选择后有两个可拖动手柄，长按显示触控菜单。

手势坐标以 XamlRoot.RasterizationScale 转为 Scintilla 像素坐标。初始窗口尺寸也按缩放比例计算并限制在工作区内；已在 200% 缩放下验证实际编辑区域尺寸。触控惯性只在手势结束后运行临时计时器，尊重系统关闭动画的设置；空闲时不运行惯性计时器。120 Hz 触控、手写笔与真实软键盘行为仍需 Surface 验收。

## 模块与数据

ShellViewModel 使用 INotifyPropertyChanged 暴露解释器、工作区和会话状态；WinUI view 层负责控件呈现和窗口事件；Core 服务不依赖 WinUI。编辑器文档各自保留 Scintilla 控件、撤销栈和保存点。文件保存采用临时文件替换，写入前对原始磁盘 SHA-256 检查，防止已检测到的外部修改被覆盖。配置采用版本化 JSON 与原子替换，损坏配置不会被默认值自动覆盖。

## Python 与补全

随包分发基础 CPython，首次复制至可写用户目录。开发依赖锁定在 `scripts/requirements-runtime.txt`。运行、REPL、补全与调试使用当前解释器，Conda / venv 的进程 PATH 补齐自身目录，支持原生扩展 DLL 查找。

语言进程使用 JSON lines，Jedi 延迟加载；分析编辑内容不执行用户脚本。标准 AST 负责语法诊断，Ruff 负责格式化；没有 Jedi 时仍有关键字与 AST 名称补全，没有 Ruff 时退回保留多行字符串的空白清理。请求取消时终止旧 worker，避免上一请求的响应被下一请求误读。

没有引入独立 LSP 服务端，使用 Jedi API 完成同等核心离线查询；当前没有多文件 LSP 工作区协议、自动导入和语义级类型诊断。

## 调试与设备

CPython 采用 debugpy.adapter + 标准 DAP。协议按 UTF-8 字节计算帧长度，支持异步响应与事件、断点、调用栈、变量、继续、步入 / 步过 / 步出。断点以 Scintilla marker handle 跟踪插入删除后的行位置。

MicroPython 文件与执行复用 mpremote。REPL 是 pyserial 标准串口透传；没有重写 raw REPL 协议。长任务的停止会终止当前主机任务、重连普通 REPL 并发送 Ctrl+C。设备调试不冒充 CPython 断点调试。没有连接真实开发板，因此串口兼容性和设备端行为保持待验证。

## 终端与性能边界

当前集成为管道交互控制台。流式输出使用原生 Scintilla 展示，支持 stdin、输出复制和触控；PowerShell / CMD / 自定义 Shell 各自独立会话。PowerShell 永远默认 -NoLogo -NoProfile。尚无 ConPTY / VT 屏幕仿真，因此全屏 TUI、控制台光标定位、交互式原生控制台程序兼容性不保证。

输出按 50 ms 合批，只在有数据时启动计时器；每通道保留约 1 MB 文本，并限制生产者队列。语言请求延迟 350 ms、自动跳过 2 MB 以上文件，文件树按展开读取且每目录限制 2000 项。服务均按需启动，没有后台更新 / USB / 文件树轮询。

WinUI / Scintilla 使用自身原生渲染。当前没有自定义 GPU / 软件渲染切换，也不安装 CUDA / PyTorch 等计算依赖。不能将操作系统本来使用 GPU 绘制描述为额外完成的硬件加速设置。

## 分发

MSIX 使用 MakeAppx + SignTool，.NET 与 Windows App SDK 自包含。Python 来源 NuGet 官方 python 包，并以官方 catalog SHA-512 校验；补齐 Python Software Foundation 签名的 Tcl/Tk，使 Matplotlib TkAgg 可创建窗口。

Tcl/Tk MSI 仅通过只读 MSI 数据库 API 与 expand.exe 解包，不运行 MSI 安装、自定义动作或注册产品。联网更新按新版本独立目录部署，完成验证与工具安装后才更新解释器设置。
