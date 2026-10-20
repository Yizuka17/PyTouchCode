# Third-party components

PyTouchCode redistributes the following components in its self-contained development
distribution. Their respective licenses and notices continue to apply.

| Component | Distribution / notice |
| --- | --- |
| .NET runtime | Microsoft .NET MIT license and bundled runtime notices |
| Windows App SDK / WinUI | Microsoft Windows App SDK NuGet packages and accompanying notices; selection animation based on NavigationView, MIT notice in `ThirdPartyNotices/WinUI.txt` |
| WinUIEdit 0.0.5-prerelease | Permission notice in `ThirdPartyNotices/WinUIEdit.txt` |
| Scintilla | Permission notice in `ThirdPartyNotices/Scintilla.txt` |
| Lexilla | Permission notice in `ThirdPartyNotices/Lexilla.txt` |
| CPython 3.13.16 | Python Software Foundation license in `runtime/python/LICENSE.txt` |
| Tcl/Tk | CPython official Tcl/Tk distribution, notices under `runtime/python/tcl` |
| Jedi, Parso | MIT license, respective `runtime/python/Lib/site-packages/*.dist-info` directories |
| debugpy | MIT and vendored debugger notices in its package / dist-info directories |
| mpremote | MIT, notice in its package metadata |
| pyserial | BSD license, notice in package metadata |
| Ruff | MIT, notice under `ruff-*.dist-info/licenses` |
| platformdirs | MIT, notice in its dist-info directory |

The runtime preparation process keeps the complete upstream package contents,
including licenses, rather than copying only executable files. Machine learning
libraries installed by the user are managed by their selected Python environment
and are not redistributed in the base application package.

Upstream projects: [WinUIEdit](https://github.com/BreeceW/WinUIEdit),
[Scintilla](https://www.scintilla.org/), [Lexilla](https://github.com/ScintillaOrg/lexilla),
[CPython](https://www.python.org/), [Jedi](https://github.com/davidhalter/jedi),
[debugpy](https://github.com/microsoft/debugpy),
[mpremote](https://github.com/micropython/micropython/tree/master/tools/mpremote),
[pyserial](https://github.com/pyserial/pyserial), [Ruff](https://github.com/astral-sh/ruff).

Microsoft Fluent UI System Icons (MIT) supplies the original 20 px interface icon paths. See [upstream](https://github.com/microsoft/fluentui-system-icons), Assets/Icons/SOURCE.txt for the pinned revision and ThirdPartyNotices/FluentSystemIcons-LICENSE.txt for the full license. DocumentDark.svg changes only the foreground color for dark mode.


Windows Terminal provides the built-in color schemes, pinned at 6967ba1c0714a4fe1d10eec3fc062b216feb718e, under MIT. See ThirdPartyNotices/WindowsTerminal-LICENSE.txt and https://github.com/microsoft/terminal .
Material Icon Theme provides file-type SVG icons, pinned at 13025adc5ad10a5cc649528a2c1edec93069f1b9, under MIT. See ThirdPartyNotices/MaterialIconTheme-LICENSE.txt and https://github.com/PKief/vscode-material-icon-theme . Folder icons come from Microsoft Fluent System Icons and adapt the foreground color.

