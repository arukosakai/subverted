# Puts a working copy into the state that defeats SVN's metadata fast path: every recorded mtime
# stale, every byte unchanged. This is what a tree looks like after a real session of work — files
# opened and saved, a revert, a branch switch, an asset re-export that rewrote identical bytes.
# Both `svn status` and Subverted must then compare content to answer at all.
param([Parameter(Mandatory = $true)][string]$WorkingCopy)

$ErrorActionPreference = 'Stop'
$stamp = (Get-Date).AddMinutes(-1)
$moved = 0

foreach ($file in [System.IO.Directory]::EnumerateFiles($WorkingCopy, '*', 'AllDirectories')) {
    if ($file -like '*\.svn\*') { continue }
    [System.IO.File]::SetLastWriteTime($file, $stamp)
    $moved++
}

Write-Output "moved $moved mtimes to $stamp"
