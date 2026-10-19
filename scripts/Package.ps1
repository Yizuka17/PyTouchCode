param([switch]$Install, [string]$CertificateThumbprint)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$packageVersion = ([xml](Get-Content (Join-Path $root 'packaging/AppxManifest.xml') -Raw)).Package.Identity.Version
$appVersion = ([version]$packageVersion).ToString(3)
& (Join-Path $PSScriptRoot 'Build.ps1') -Configuration Release
$output = Join-Path $root "artifacts/publish-$appVersion"
dotnet publish (Join-Path $root 'src/PyTouch.App/PyTouch.App.csproj') -c Release -p:Platform=x64 -o $output -v:minimal
if ($LASTEXITCODE) { throw 'Publish failed' }
Copy-Item (Join-Path $root 'packaging/AppxManifest.xml') (Join-Path $output 'AppxManifest.xml') -Force
$sdk = Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools/10.0.26100.7627/bin/10.0.26100.0/x64'
$makeappx = Join-Path $sdk 'makeappx.exe'; $signtool = Join-Path $sdk 'signtool.exe'
if (!(Test-Path $makeappx)) { throw 'Restore the Windows SDK BuildTools package first.' }
$package = Join-Path $root "artifacts/PyTouchCode_${packageVersion}_x64.msix"
& $makeappx pack /d $output /p $package /o
if ($LASTEXITCODE) { throw 'MSIX packaging failed' }
if ($CertificateThumbprint) { $certificate = Get-Item "Cert:\CurrentUser\My\$CertificateThumbprint" }
else {
    $certificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq 'CN=PyTouch Development' -and $_.NotAfter -gt (Get-Date).AddYears(16) -and $_.HasPrivateKey } | Select-Object -First 1
    if (!$certificate) {
        $certificate = New-SelfSignedCertificate -Type Custom -Subject 'CN=PyTouch Development' -FriendlyName 'PyTouch MSIX Development (17 years)' -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -KeyUsage DigitalSignature -CertStoreLocation 'Cert:\CurrentUser\My' -NotAfter (Get-Date).AddYears(17) -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    }
}
if ($certificate.Subject -ne 'CN=PyTouch Development') { throw 'Publisher and certificate subject must match.' }
$publicCertificate = Join-Path $root 'artifacts/PyTouchCode-Development.cer'
Export-Certificate -Cert $certificate -FilePath $publicCertificate | Out-Null
& $signtool sign /fd SHA256 /sha1 $certificate.Thumbprint /s My $package
if ($LASTEXITCODE) { throw 'MSIX signing failed' }
[ordered]@{ package = $package; sha256 = (Get-FileHash $package -Algorithm SHA256).Hash; certificate = $certificate.Thumbprint; validFrom = $certificate.NotBefore.ToString('O'); validUntil = $certificate.NotAfter.ToString('O') } | ConvertTo-Json | Set-Content (Join-Path $root 'artifacts/package-info.json') -Encoding utf8
Write-Host "Signed: $package"
Write-Host "Certificate expires: $($certificate.NotAfter.ToString('yyyy-MM-dd'))"
$portable = Join-Path $root "artifacts/PyTouchCode_${appVersion}_portable_x64.zip"
if (Test-Path -LiteralPath $portable) { Remove-Item -LiteralPath $portable }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($output, $portable, [IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host "Portable: $portable"
if ($Install) {
    & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Install.ps1') -Package $package
    if ($LASTEXITCODE) { throw 'Package installation failed' }
    & $signtool verify /pa $package
    if ($LASTEXITCODE) { throw 'Signature verification failed' }
}


