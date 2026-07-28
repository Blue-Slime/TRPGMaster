Write-Host "开始复制旧版UI到新项目..." -ForegroundColor Cyan

$source = "G:\跑团大师\04-旧项目\跑团大师2026年6月\08-新项目\TRPGMaster"
$dest = "G:\跑团大师\02-新项目"

Write-Host "`n1. 复制 MasterClient..." -ForegroundColor Yellow
Copy-Item -Path "$source\MasterClient" -Destination "$dest\" -Recurse -Force
Write-Host "   ✓ MasterClient 已复制" -ForegroundColor Green

Write-Host "`n2. 复制 MasterServerUI..." -ForegroundColor Yellow
Copy-Item -Path "$source\MasterServerUI" -Destination "$dest\" -Recurse -Force
Write-Host "   ✓ MasterServerUI 已复制" -ForegroundColor Green

Write-Host "`n3. 复制 MasterStarter..." -ForegroundColor Yellow
Copy-Item -Path "$source\MasterStarter" -Destination "$dest\" -Recurse -Force
Write-Host "   ✓ MasterStarter 已复制" -ForegroundColor Green

Write-Host "`n4. 复制 MasterServer..." -ForegroundColor Yellow
Copy-Item -Path "$source\MasterServer" -Destination "$dest\" -Recurse -Force
Write-Host "   ✓ MasterServer 已复制" -ForegroundColor Green

Write-Host "`n5. 复制 TRPGMaster.Assets..." -ForegroundColor Yellow
Copy-Item -Path "$source\TRPGMaster.Assets" -Destination "$dest\" -Recurse -Force
Write-Host "   ✓ TRPGMaster.Assets 已复制" -ForegroundColor Green

Write-Host "`n=== 复制完成 ===" -ForegroundColor Green
Write-Host "已复制的项目:" -ForegroundColor Cyan
Get-ChildItem -Path $dest -Directory | Format-Table Name
