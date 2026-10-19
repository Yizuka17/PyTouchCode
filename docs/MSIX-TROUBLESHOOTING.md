# MSIX XAML startup crash: resource index repair

## Symptom

A portable PyTouchCode EXE starts, but the manually assembled MSIX fails during
`MainWindow.InitializeComponent()` with `XamlParseException` / `0xC000027B`.
The crash log (usually `%TEMP%\PyTouch-crash.log`) has startup phase
`MainWindow XAML`.

## Likely cause

`WindowsPackageType=None` builds a WinUI 3 executable for *unpackaged* activation.
The Windows App SDK unpackaged publish path may omit the project's own `.pri`.
Moreover, a package with identity expects a root `resources.pri` whose primary
resource-map name matches the package `Identity.Name`. Simply invoking
`MakeAppx pack` on an unpackaged publish folder is not sufficient.

References:
- https://github.com/microsoft/WindowsAppSDK/issues/6720
- https://github.com/microsoft/microsoft-ui-xaml/issues/10856
- https://learn.microsoft.com/en-us/windows/uwp/app-resources/resource-management-system

## What the fix changes

- Adds the missing app PRI to ordinary `dotnet publish`.
- Builds a separate MSIX layout with `ProjectPriFileName=resources.pri` and
  `ProjectPriIndexName` equal to the package identity.
- Runs `MakePri dump`, refusing to package if the root resource map is incorrect
  or `App.xbf` / `MainWindow.xbf` cannot be found.
- Leaves the portable folder untouched and retains the existing sign/verify path.
- Increments only the MSIX revision from 0.1.3.0 to 0.1.3.1 so the installed
  test package can be upgraded without uninstalling the old package.

## Build / test on Windows

1. In the repository root, run `./scripts/Package.ps1` under PowerShell.
2. Verify `artifacts/msix-pri-0.1.3.xml` includes
   `ResourceMap name="PyTouchCode.PyTouch"`, `App.xbf` and `MainWindow.xbf`.
3. Install `artifacts/PyTouchCode_0.1.3.1_x64.msix` with the exported development
   certificate; using the existing elevated `Install.ps1` is supported.
4. Launch the installed app **from Start** (not `artifacts/publish-0.1.3`).
   Confirm the main window opens, then create/edit/save a file and run Python.
5. In a fresh attempt, check `%TEMP%\PyTouch-crash.log` and the Application
   event log for new errors. Old crashes from 0.1.3.0 may remain in the logs.
6. Verify the portable build still starts and run `./scripts/Test-UI.ps1`.

## Verification status

This change is a targeted **candidate fix** based on an upstream reproduced
WinUI 3 bug and the supplied crash stack. It has not yet been compiled or
launched on Windows in this environment. Passing the PRI preflight proves
resource inclusion but does **not** replace a real installed-MSIX smoke test.

If the installed app still crashes, capture the *new* WinUI/XAML exception and
inspect the underlying WinRT originate error before changing unrelated code.
