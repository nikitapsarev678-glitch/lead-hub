# Скачивает sing-box для Windows и кладёт в tools\sing-box\
# Запуск: powershell -ExecutionPolicy Bypass -File tools\fetch-singbox.ps1
$ErrorActionPreference = "Stop"

$repo = "SagerNet/sing-box"
$dest = Join-Path $PSScriptRoot "sing-box"

Write-Host "Узнаю последний релиз sing-box..."
$release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest"
$asset = $release.assets | Where-Object { $_.name -match "windows-amd64\.zip$" } | Select-Object -First 1
if (-not $asset) { throw "Не нашёл windows-amd64 zip в последнем релизе" }

$zipPath = Join-Path $env:TEMP $asset.name
Write-Host "Скачиваю $($asset.name)..."
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipPath

Write-Host "Распаковываю в $dest..."
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
Expand-Archive -Path $zipPath -DestinationPath $dest -Force

# exe лежит во вложенной папке вида sing-box-1.x.x-windows-amd64 — поднимем наверх
$inner = Get-ChildItem $dest -Directory | Select-Object -First 1
if ($inner) {
    Get-ChildItem $inner.FullName | Move-Item -Destination $dest -Force
    Remove-Item $inner.FullName -Recurse -Force -ErrorAction SilentlyContinue
}
Remove-Item $zipPath -Force -ErrorAction SilentlyContinue

$exe = Join-Path $dest "sing-box.exe"
if (Test-Path $exe) {
    Write-Host "Готово: $exe"
    & $exe version
} else {
    throw "sing-box.exe не найден после распаковки — посмотри содержимое $dest"
}
