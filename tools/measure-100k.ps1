# Measures M1's exit criterion on a working copy: cold scan, cold `sv st`, warm `sv st`.
# Usage: pwsh tools/measure-100k.ps1 <working-copy-path> [runs]
param(
    [Parameter(Mandatory = $true)][string]$WorkingCopy,
    [int]$Runs = 3
)

$ErrorActionPreference = 'Stop'
$bin = Join-Path $PSScriptRoot '..\artifacts\bin'
$sv = Join-Path $bin 'sv.exe'
$probe = Join-Path $PSScriptRoot '..\tools\Subverted.Probe\bin\Release\net10.0\Subverted.Probe.exe'

function Measure-Wall([scriptblock]$Action) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $output = & $Action
    $sw.Stop()
    [pscustomobject]@{ Ms = $sw.Elapsed.TotalMilliseconds; Output = ($output -join "`n") }
}

Write-Output "== working copy: $WorkingCopy"

Write-Output "`n== scan only (probe, no daemon, no process-startup exclusion)"
for ($i = 1; $i -le $Runs; $i++) {
    $r = Measure-Wall { & $probe $WorkingCopy 2>&1 | Select-String -Pattern 'entries in' }
    Write-Output ("  run {0}: wall {1:F0} ms   {2}" -f $i, $r.Ms, $r.Output.Trim())
}

Write-Output "`n== sv st, cold (daemon restarted before each run)"
for ($i = 1; $i -le $Runs; $i++) {
    & $sv daemon stop *> $null
    Start-Sleep -Milliseconds 500
    $r = Measure-Wall { & $sv st $WorkingCopy --timing 2>&1 }
    Write-Output ("  run {0}: wall {1:F0} ms   {2}" -f $i, $r.Ms, ($r.Output -split "`n" | Select-Object -Last 1))
}

Write-Output "`n== sv st, warm (same daemon, index already held)"
& $sv st $WorkingCopy *> $null
Start-Sleep -Seconds 2
for ($i = 1; $i -le $Runs; $i++) {
    $r = Measure-Wall { & $sv st $WorkingCopy --timing 2>&1 }
    Write-Output ("  run {0}: wall {1:F0} ms   {2}" -f $i, $r.Ms, ($r.Output -split "`n" | Select-Object -Last 1))
}

Write-Output "`n== svn status, for reference"
for ($i = 1; $i -le $Runs; $i++) {
    $r = Measure-Wall { svn status $WorkingCopy 2>&1 }
    Write-Output ("  run {0}: wall {1:F0} ms" -f $i, $r.Ms)
}
