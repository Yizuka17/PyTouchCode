param([switch]$Install, [string]$CertificateThumbprint)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$manifest = [xml](Get-Content (Join-Path $root 'packaging/AppxManifest.xml') -Raw)
$packageVersion = $manifest.Package.Identity.Version
$packageName = $manifest.Package.Identity.Name
$appVersion = ([version]$packageVersion).ToString(3)
$project = Join-Path $root 'src/PyTouch.App/PyTouch.App.csproj'

& (Join-Path $PSScriptRoot 'Build.ps1') -Configuration Release

# Keep a truly unpackaged build for portable users.
$portableOutput = Join-Path $root "artifacts/publish-$appVersion"
dotnet publish $project -c Release -p:Platform=x64 -o $portableOutput -v:minimal
if ($LASTEXITCODE) { throw 'Portable publish failed' }
if (!(Test-Path (Join-Path $portableOutput 'PyTouchCode.pri'))) {
    throw 'Portable publish did not include PyTouchCode.pri. The WinUI XAML index is required.'
}

# Rebuild the resource index for *package identity*. Simply packing the above
# unpackaged publish output with MakeAppx gives the package no root resources.pri.
# MSIX activation resolves XAML through that package PRI, not PyTouchCode.pri.
$msixOutput = Join-Path $root "artifacts/msix-layout-$appVersion"
if (Test-Path $msixOutput) { Remove-Item $msixOutput -Recurse -Force }
dotnet publish $project -c Release -p:Platform=x64 `
    "-p:ProjectPriFileName=resources.pri" `
    "-p:ProjectPriIndexName=$packageName" `
    "-p:WindowsAppSDKBootstrapAutoInitializeOptions_OnPackageIdentity_NoOp=true" `
    "-p:WindowsAppSdkUndockedRegFreeWinRTInitialize=false" `
    -o $msixOutput -v:minimal
if ($LASTEXITCODE) { throw 'MSIX publish failed' }

$pri = Join-Path $msixOutput 'resources.pri'
if (!(Test-Path $pri)) {
    throw 'MSIX publish did not produce resources.pri. Refusing to sign a package that cannot load MainWindow.xaml.'
}

$sdk = Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windows.sdk.buildtools/10.0.26100.7627/bin/10.0.26100.0/x64'
$makeappx = Join-Path $sdk 'makeappx.exe'
$makepri = Join-Path $sdk 'makepri.exe'
$signtool = Join-Path $sdk 'signtool.exe'
foreach ($tool in @($makeappx, $makepri, $signtool)) {
    if (!(Test-Path $tool)) { throw "Windows SDK tool not found: $tool" }
}

# Validate the root PRI *before* making and signing the MSIX. The resource map
# must have the exact Identity.Name from AppxManifest.xml, and contain our XBF.
$priDump = Join-Path $root "artifacts/msix-pri-$appVersion.xml"
& $makepri dump /if $pri /of $priDump /o
if ($LASTEXITCODE) { throw 'Unable to dump package resources.pri' }
[xml]$priXml = Get-Content $priDump -Raw
$map = $priXml.SelectSingleNode('/*[local-name()="PriInfo"]/*[local-name()="ResourceMap"]')
if ($null -eq $map -or $map.GetAttribute('name') -cne $packageName) {
    throw "MSIX PRI resource map must be '$packageName' but was '$($map.GetAttribute('name'))'."
}
$priText = Get-Content $priDump -Raw
foreach ($resource in @('App.xbf', 'MainWindow.xbf')) {
    if (!$priText.Contains($resource)) { throw "MSIX resource index is missing $resource" }
}

Copy-Item (Join-Path $root 'packaging/AppxManifest.xml') (Join-Path $msixOutput 'AppxManifest.xml') -Force
$package = Join-Path $root "artifacts/PyTouchCode_${packageVersion}_x64.msix"
& $makeappx pack /d $msixOutput /p $package /o
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
[IO.Compression.ZipFile]::CreateFromDirectory($portableOutput, $portable, [IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host "Portable: $portable"
if ($Install) {
    & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Install.ps1') -Package $package
    if ($LASTEXITCODE) { throw 'Package installation failed' }
    & $signtool verify /pa $package
    if ($LASTEXITCODE) { throw 'Signature verification failed' }
}
