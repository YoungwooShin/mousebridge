$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$stage = Join-Path $root 'artifacts/portable'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item $stage -ItemType Directory -Force | Out-Null
dotnet publish "$root/src/MouseBridge.App" -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $stage
if ($LASTEXITCODE) { throw 'Publish failed' }
$exe = Join-Path $stage 'MouseBridge.exe'
if (!(Test-Path $exe)) { throw 'Single executable is missing' }
$extra = @(Get-ChildItem $stage -File -Recurse | Where-Object { $_.FullName -ne $exe })
if ($extra.Count) { throw 'Unexpected external publish files. Review them before packaging; do not remove runtime dependencies blindly.' }
$zip = Join-Path $root 'artifacts/MouseBridge-Portable-win-x64.zip'
Compress-Archive -Path $exe -DestinationPath $zip -Force -CompressionLevel Optimal
Get-FileHash $zip -Algorithm SHA256
Write-Host "Created $zip — extract and run MouseBridge.exe. Bundled runtime extracts to the Windows temporary .NET cache on first launch."
