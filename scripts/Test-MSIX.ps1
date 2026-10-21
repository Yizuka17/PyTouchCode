param([string]$Package, [string]$PyboardPort)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') {
    $arguments = @('-NoLogo', '-NoProfile', '-File', $PSCommandPath)
    if ($Package) { $arguments += @('-Package', $Package) }
    if ($PyboardPort) { $arguments += @('-PyboardPort', $PyboardPort) }
    & "$env:WINDIR/System32/WindowsPowerShell/v1.0/powershell.exe" @arguments
    if ($LASTEXITCODE) { throw 'Installed MSIX test failed.' }
    return
}
if ($Package) { Add-AppxPackage -Path (Resolve-Path -LiteralPath $Package).Path }
$installed = Get-AppxPackage 'PyTouchCode.PyTouch'
if (!$installed) { throw 'PyTouchCode is not installed.' }
$root = Split-Path $PSScriptRoot -Parent
$directory = Join-Path $root ('artifacts/msix-verified-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PackageActivation {
    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IActivation {
        void ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.LPWStr)] string args, uint options, out uint processId);
    }
    public static uint Launch(string id, string args) {
        var manager = (IActivation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")));
        uint processId; manager.ActivateApplication(id, args, 0, out processId); return processId;
    }
}
'@
$activationArguments = '--smoke-test "' + $directory + '"'
if ($PyboardPort) {
    if ($PyboardPort -notmatch '^COM[1-9][0-9]*$') { throw 'Invalid Pyboard COM port.' }
    $activationArguments += ' --pyboard-test ' + $PyboardPort
}
$processId = [PackageActivation]::Launch(($installed.PackageFamilyName + '!App'), $activationArguments)
$process = Get-Process -Id $processId -ErrorAction SilentlyContinue
if ($process -and !$process.WaitForExit(120000)) { throw "Installed smoke test timed out: $directory" }
$report = Get-Content -LiteralPath (Join-Path $directory 'ui-smoke.json') -Raw | ConvertFrom-Json
Write-Output "Installed $($installed.Version): $($report.passed) passed, $($report.failed) failed"
Write-Output $directory
if ($report.failed) { $report.results | Where-Object { !$_.passed } | ConvertTo-Json -Depth 5; throw 'Installed UI smoke tests failed.' }
