$ErrorActionPreference = 'Stop'
$env:LC_ALL = 'C'
$root = Join-Path $env:TEMP 'subverted-history'
if (Test-Path $root) { throw "$root already exists" }
New-Item -ItemType Directory $root | Out-Null
svnadmin create "$root\repo"
$url = 'file:///' + ("$root\repo" -replace '\\', '/')
svn checkout -q $url "$root\wc"
Set-Location "$root\wc"

function Commit($message) { svn commit -q -m $message; svn update -q }

New-Item -ItemType Directory art, docs | Out-Null
[IO.File]::WriteAllText("$root\wc\a.txt", "one`ntwo`nthree`n")
[IO.File]::WriteAllBytes("$root\wc\art\hero.png", [byte[]](0x89, 0x50, 0x4E, 0x47, 0, 1, 2, 3))
[IO.File]::WriteAllText("$root\wc\docs\readme.txt", "readme`n")
[IO.File]::WriteAllText("$root\wc\icon@2x.png.txt", "retina`n")
svn add -q a.txt art docs "icon@2x.png.txt@"
Commit 'r1: initial layout'

[IO.File]::WriteAllText("$root\wc\a.txt", "one`nTWO`nthree`n")
Commit 'r2: edit a.txt'

svn move -q a.txt b.txt
Commit 'r3: rename a.txt to b.txt'

svn propset -q svn:eol-style native docs/readme.txt
[IO.File]::WriteAllText("$root\wc\docs\readme.txt", "readme`nmore`n")
Commit "r4: readme gets a property and a line`n`nsecond paragraph of the message"

[IO.File]::WriteAllBytes("$root\wc\art\hero.png", [byte[]](0x89, 0x50, 0x4E, 0x47, 9, 9, 9, 9))
Commit 'r5: new hero'

svn delete -q docs/readme.txt
Commit 'r6: drop readme'

[IO.File]::WriteAllText("$root\wc\b.txt", "one`nTWO`nthree`nfour")
[IO.File]::WriteAllText("$root\wc\icon@2x.png.txt", "retina2`n")
Commit 'r7: b.txt without trailing newline, retina edit'

[IO.File]::WriteAllText("$root\wc\100% done file.txt", "a`n")
svn add -q "100% done file.txt"
Commit 'r8: odd name'

[IO.File]::WriteAllText("$root\wc\100% done file.txt", "a`nb`n")
Commit 'r9: edit odd name'

# A second checkout left behind HEAD and mixed: root at r3, art at r5.
svn checkout -q -r 3 $url "$root\wc-behind"
Set-Location "$root\wc-behind"
svn update -q -r 5 art
"built $root"
