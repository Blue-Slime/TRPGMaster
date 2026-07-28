Write-Host "开始复制依赖模块..." -ForegroundColor Cyan

$imSource = "G:\跑团大师\04-旧项目\跑团大师2026年6月\02-MasterIM"
$mapSource = "G:\跑团大师\04-旧项目\跑团大师2026年6月\地图模块尝试"
$dest = "G:\跑团大师\02-新项目"

Write-Host "`n1. 复制 MasterIM 框架..." -ForegroundColor Yellow
Copy-Item -Path $imSource -Destination "$dest\MasterIM" -Recurse -Force
Write-Host "   ✓ MasterIM 框架已复制" -ForegroundColor Green

Write-Host "`n2. 复制 MapEditor（地图编辑器）..." -ForegroundColor Yellow
Copy-Item -Path $mapSource -Destination "$dest\MapEditor" -Recurse -Force
Write-Host "   ✓ MapEditor 已复制" -ForegroundColor Green

Write-Host "`n=== 依赖模块复制完成 ===" -ForegroundColor Green
Write-Host "项目结构:" -ForegroundColor Cyan
Get-ChildItem -Path $dest -Directory | Select-Object Name | Format-Table
