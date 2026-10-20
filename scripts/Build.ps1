param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug', [switch]$SkipRuntime, [switch]$Test)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$SkipRuntime) { & (Join-Path $PSScriptRoot 'Prepare-Runtime.ps1'); if ($LASTEXITCODE) { throw 'Runtime preparation failed' } }
& (Join-Path $PSScriptRoot 'New-Assets.ps1')
dotnet build (Join-Path $root 'PyTouchCode.sln') -c $Configuration -p:Platform=x64 -v:minimal
if ($LASTEXITCODE) { throw 'Build failed' }
if ($Test) {
    dotnet run --project (Join-Path $root 'tests/PyTouchCode.Tests/PyTouchCode.Tests.csproj') -c $Configuration -- (Join-Path $root 'runtime/python/python.exe')
    if ($LASTEXITCODE) { throw 'Tests failed' }
}
