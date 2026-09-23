# Builds a working copy whose files are the size a game studio's actually are, so scan timings
# measure throughput rather than per-file overhead. The bytes are random on purpose: real assets
# are already-compressed PNG/audio/mesh data, and compressible filler would make both the pristine
# store and SVN's delta encoding unrealistically cheap to read back.
#
# Usage: pwsh tools/make-asset-fixture.ps1 [-Name subverted-assets] [-Files 1000] [-Directories 20]
param(
    [string]$Name = 'subverted-assets',
    [int]$Files = 1000,
    [int]$Directories = 20
)

$ErrorActionPreference = 'Stop'

$root = Join-Path $env:TEMP $Name
$repo = Join-Path $root 'repo'
$wc = Join-Path $root 'wc'

if (Test-Path $root) { throw "$root already exists — remove it first, or pass -Name" }

# A spread rather than one size: a studio tree is a mix of small meshes and large textures, and a
# uniform size would hide whether the parallel compare is bound on file count or on bytes.
$sizesKb = @(256, 512, 768, 1024, 1536, 2048, 3072, 4096)

New-Item -ItemType Directory $root | Out-Null
svnadmin create $repo
if ($LASTEXITCODE -ne 0) { throw 'svnadmin create failed' }

$url = 'file:///' + ($repo -replace '\\', '/')
svn checkout $url $wc --quiet
if ($LASTEXITCODE -ne 0) { throw 'svn checkout failed' }

Write-Output "generating $Files files across $Directories directories..."
$rng = [System.Random]::new(20260921)
$written = 0L
for ($d = 0; $d -lt $Directories; $d++) {
    $dir = Join-Path $wc ("assets{0:D3}" -f $d)
    New-Item -ItemType Directory $dir | Out-Null
}
for ($i = 0; $i -lt $Files; $i++) {
    $dir = Join-Path $wc ("assets{0:D3}" -f ($i % $Directories))
    $bytes = New-Object byte[] ($sizesKb[$i % $sizesKb.Length] * 1024)
    $rng.NextBytes($bytes)
    [System.IO.File]::WriteAllBytes((Join-Path $dir ("asset{0:D5}.bin" -f $i)), $bytes)
    $written += $bytes.Length
}
Write-Output ("wrote {0:N0} bytes ({1:F2} GiB)" -f $written, ($written / 1GB))

Push-Location $wc
try {
    svn add assets* --quiet
    if ($LASTEXITCODE -ne 0) { throw 'svn add failed' }
    Write-Output 'committing (this is the slow part)...'
    svn commit -m 'asset fixture' --quiet
    if ($LASTEXITCODE -ne 0) { throw 'svn commit failed' }
    # A commit leaves the root a revision behind; without this a later propset on `.` is out of date.
    svn update --quiet
    if ($LASTEXITCODE -ne 0) { throw 'svn update failed' }
}
finally { Pop-Location }

Write-Output "done: $wc"
