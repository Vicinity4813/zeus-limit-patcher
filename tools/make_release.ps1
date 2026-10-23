# Builds the patcher and packs the release zip: dist\Zeus-Limit-Patcher-<version>.zip
# (patcher exe + Zeus Save Check + notices + source\; no game file, no development tool exe).
# Compile le patcheur et prepare le zip de la release.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& cmd /c "`"$root\build.cmd`""
if ($LASTEXITCODE -ne 0) { throw 'build failed' }
$exe = Join-Path $root 'dist\Zeus Limit Patcher.exe'
$version = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
$stage = Join-Path $root 'dist\stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory "$stage\source\src", "$stage\source\tools" | Out-Null
Copy-Item -LiteralPath $exe, (Join-Path $root 'dist\Zeus Save Check.exe') -Destination $stage
Copy-Item -LiteralPath "$root\docs\LISEZMOI.txt", "$root\docs\README.txt", "$root\LICENSE", "$root\LICENSE-zeus_mapper.txt" -Destination $stage
Copy-Item -Path "$root\src\*" -Destination "$stage\source\src"
Copy-Item -Path "$root\tools\*.cs" -Destination "$stage\source\tools"
Copy-Item -LiteralPath "$root\build.cmd", "$root\LICENSE-zeus_mapper.txt" -Destination "$stage\source"
$zip = Join-Path $root "dist\Zeus-Limit-Patcher-$version.zip"
Compress-Archive -Path "$stage\*" -DestinationPath $zip -CompressionLevel Optimal -Force
Remove-Item $stage -Recurse -Force
"Release: $zip"
