param([string]$Package)
$ErrorActionPreference = 'Stop'
if (!$Package) {
    $beside = Join-Path $PSScriptRoot 'PyTouchCode_0.1.3.0_x64.msix'
    $Package = if (Test-Path -LiteralPath $beside) { $beside } else { Join-Path $PSScriptRoot '../artifacts/PyTouchCode_0.1.3.0_x64.msix' }
}
if ($PSVersionTable.PSEdition -eq 'Core') {
    & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -File $PSCommandPath -Package $Package
    if ($LASTEXITCODE) { throw 'MSIX installation failed.' }
    exit 0
}
$certificate = Join-Path (Split-Path $Package -Parent) 'PyTouchCode-Development.cer'
if (!(Test-Path $certificate)) { throw 'Place PyTouchCode-Development.cer beside the MSIX.' }
$cert = [Security.Cryptography.X509Certificates.X509Certificate2]::new((Resolve-Path $certificate).Path)
if ($cert.Subject -ne 'CN=PyTouch Development') { throw 'Unexpected certificate publisher.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this installation script in an elevated PowerShell window. MSIX test certificates must be trusted under LocalMachine\TrustedPeople.'
}
Import-Certificate -FilePath $certificate -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
Add-AppxPackage -Path $Package


