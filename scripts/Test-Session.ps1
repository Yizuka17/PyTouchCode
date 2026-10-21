param([string]$Executable = (Join-Path $PSScriptRoot '../artifacts/publish-0.1.5/PyTouchCode.exe'), [switch]$Installed)
$ErrorActionPreference = 'Stop'
if ($Installed -and $PSVersionTable.PSEdition -ne 'Desktop') {
    & "$env:WINDIR/System32/WindowsPowerShell/v1.0/powershell.exe" -NoLogo -NoProfile -File $PSCommandPath -Installed
    if ($LASTEXITCODE) { throw 'Installed workspace restart test failed.' }
    return
}
if ($Installed) {
    $package = Get-AppxPackage 'PyTouchCode.PyTouch'
    if (!$package) { throw 'PyTouchCode is not installed.' }
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SessionActivation {
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
}
$root = Split-Path $PSScriptRoot -Parent
$directory = Join-Path $root ('artifacts/session-verified-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
foreach ($phase in @('empty-first', 'seed', 'restore', 'empty-after')) {
    $arguments = @('--smoke-test', ('"' + $directory + '"'), '--session-phase', $phase)
    if ($Installed) {
        $processId = [SessionActivation]::Launch(($package.PackageFamilyName + '!App'), ($arguments -join ' '))
        $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
    } else { $process = Start-Process -FilePath $Executable -ArgumentList $arguments -PassThru -WindowStyle Hidden }
    if ($process -and !$process.WaitForExit(120000)) { $process.Kill(); throw "Session test timed out: $phase" }
    $report = Get-Content -LiteralPath (Join-Path $directory "session-$phase.json") -Raw | ConvertFrom-Json
    if (!$report.passed) { throw $report.error }
    Write-Output "PASS workspace restart $phase"
    if ($phase -eq 'seed') {
        [IO.File]::AppendAllText((Join-Path $directory 'first.py'), "# external change`n", [Text.UTF8Encoding]::new($false))
        Remove-Item -LiteralPath (Join-Path $directory 'missing.py')
    }
}
Write-Output $directory
