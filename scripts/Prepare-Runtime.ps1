param([string]$Version = '3.13.16')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $root 'runtime/python'
if ((Test-Path (Join-Path $target 'pytouch-runtime.json')) -and (Test-Path (Join-Path $target 'DLLs/_tkinter.pyd')) -and ((Get-Content (Join-Path $target 'pytouch-runtime.json') -Raw | ConvertFrom-Json).revision -eq 2)) { Write-Host 'Bundled runtime already prepared.'; exit 0 }
$cache = Join-Path $root 'artifacts/downloads'
New-Item -ItemType Directory -Force -Path $cache, $target | Out-Null
$archive = Join-Path $cache "python.$Version.nupkg"
$url = "https://api.nuget.org/v3-flatcontainer/python/$Version/python.$Version.nupkg"
Invoke-WebRequest $url -OutFile $archive
$registration = Invoke-RestMethod "https://api.nuget.org/v3/registration5-gz-semver2/python/$Version.json"
$catalog = Invoke-RestMethod $registration.catalogEntry
if ($catalog.packageHashAlgorithm -ne 'SHA512') { throw 'Unsupported package hash.' }
$expected = $catalog.packageHash
$sha = [Security.Cryptography.SHA512]::Create()
try { $actual = [Convert]::ToBase64String($sha.ComputeHash([IO.File]::ReadAllBytes($archive))) } finally { $sha.Dispose() }
if ($actual -cne $expected) { throw 'Python download SHA-512 mismatch.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($entry in $zip.Entries) {
        if (!$entry.FullName.StartsWith('tools/') -or $entry.FullName.EndsWith('/')) { continue }
        $destination = [IO.Path]::GetFullPath((Join-Path $target $entry.FullName.Substring(6)))
        if (!$destination.StartsWith([IO.Path]::GetFullPath($target) + [IO.Path]::DirectorySeparatorChar)) { throw 'Invalid archive path.' }
        New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $true)
    }
} finally { $zip.Dispose() }
$python = Join-Path $target 'python.exe'
& $python -m ensurepip --upgrade
if ($LASTEXITCODE) { throw 'ensurepip failed' }
& $python -m pip install --disable-pip-version-check -r (Join-Path $PSScriptRoot 'requirements-runtime.txt')
if ($LASTEXITCODE) { throw 'Runtime dependency installation failed' }
$tcltk = Join-Path $cache "tcltk-$Version.msi"
Invoke-WebRequest "https://www.python.org/ftp/python/$Version/amd64/tcltk.msi" -OutFile $tcltk
$signature = Get-AuthenticodeSignature $tcltk
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike 'CN=Python Software Foundation,*') { throw 'Tcl/Tk signature validation failed.' }
& $python (Join-Path $PSScriptRoot 'extract_tcltk.py') $tcltk $target (Join-Path $cache "tcltk-$Version")
if ($LASTEXITCODE) { throw 'Tcl/Tk extraction failed.' }
$manifest = @{ version = $Version; revision = 2; source = $url; sha512 = $expected; tcltkSha256 = (Get-FileHash $tcltk -Algorithm SHA256).Hash; preparedUtc = [DateTime]::UtcNow.ToString('O') }
$manifest | ConvertTo-Json | Set-Content (Join-Path $target 'pytouch-runtime.json') -Encoding utf8
& $python -m pip freeze | Set-Content (Join-Path $target 'requirements.lock.txt') -Encoding utf8
Write-Host "Prepared runtime: $target"
