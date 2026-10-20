param([string]$Executable = (Join-Path $PSScriptRoot '../artifacts/publish-0.1.4/PyTouchCode.exe'), [int]$Repeat = 3)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
for ($iteration = 1; $iteration -le $Repeat; $iteration++) {
    $directory = Join-Path $root ("artifacts/ui-verified-$iteration-" + [Guid]::NewGuid().ToString('N'))
    $arguments = @('--smoke-test', ('"' + $directory + '"'))
    $process = Start-Process -FilePath $Executable -ArgumentList $arguments -PassThru -WindowStyle Normal
    if (!$process.WaitForExit(120000)) { $process.Kill(); throw 'UI smoke test timed out.' }
    $report = Get-Content (Join-Path $directory 'ui-smoke.json') -Raw | ConvertFrom-Json
    Write-Output "UI run ${iteration}: $($report.passed) passed, $($report.failed) failed"
    if ($report.failed -ne 0) { $report.results | Where-Object { !$_.passed } | ConvertTo-Json -Depth 5; throw 'UI smoke tests failed.' }
}


