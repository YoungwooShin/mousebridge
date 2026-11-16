param([string]$MakeNsis = 'makensis.exe')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$payload = Join-Path $root 'artifacts/publish'
$out = Join-Path $root 'artifacts/MouseBridge-Setup-win-x64.exe'
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
New-Item $payload -ItemType Directory -Force | Out-Null
dotnet publish "$root/src/MouseBridge.App" -c Release -r win-x64 --self-contained true -o $payload
if ($LASTEXITCODE) { throw 'Publish failed' }
$lines = @('; Generated uninstall inventory')
Get-ChildItem $payload -File -Recurse | ForEach-Object {
    $relative = $_.FullName.Substring($payload.Length + 1)
    $lines += 'Delete "$INSTDIR\' + $relative + '"'
}
Get-ChildItem $payload -Directory -Recurse | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
    $relative = $_.FullName.Substring($payload.Length + 1)
    $lines += 'RMDir "$INSTDIR\' + $relative + '"'
}
$lines | Set-Content "$root/packaging/UninstallFiles.nsh" -Encoding UTF8
& $MakeNsis "/DPAYLOAD=$payload" "/DOUTPUT=$out" "$root/packaging/Installer.nsi"
if ($LASTEXITCODE) { throw 'NSIS failed' }
Write-Host "Created $out (unsigned)"
