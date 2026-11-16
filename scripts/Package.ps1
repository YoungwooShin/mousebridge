param([string]$CertificateThumbprint)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$stage = Join-Path $root 'artifacts/stage'
New-Item $stage -ItemType Directory -Force | Out-Null
# Clean to avoid stale files in an updated package.
Get-ChildItem $stage | Remove-Item -Recurse -Force
dotnet publish "$root/src/MouseBridge.App" -c Release -r win-x64 --self-contained true -o $stage
if ($LASTEXITCODE) { throw 'Publish failed' }
Copy-Item "$root/packaging/AppxManifest.xml" $stage
Copy-Item "$root/packaging/Assets" $stage -Recurse
$kitRoot = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots').KitsRoot10
$kit = Get-ChildItem (Join-Path $kitRoot 'bin') -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'x64/makeappx.exe') } | Sort-Object Name -Descending | Select-Object -First 1
if (!$kit) { throw 'Windows SDK MakeAppx/SignTool required' }
$tools = Join-Path $kit.FullName 'x64'
$out = Join-Path $root 'artifacts/MouseBridge.msix'
& "$tools/makeappx.exe" pack /d $stage /p $out /o
if ($LASTEXITCODE) { throw 'MakeAppx failed' }
if (!$CertificateThumbprint) { throw 'Unsigned MSIX created. Pass a trusted code-signing certificate thumbprint (Subject CN=MouseBridgeDev) to sign.' }
& "$tools/signtool.exe" sign /fd SHA256 /sha1 $CertificateThumbprint /s My $out
if ($LASTEXITCODE) { throw 'Signing failed' }
Write-Host "Created $out"
