Write-Host "=== 开始更新项目 ===" -ForegroundColor Cyan

# 1. 更新 Avalonia 到最新版本
Write-Host "`n1. 更新 Avalonia 包..." -ForegroundColor Yellow
$projects = @(
    "MasterClient\MasterClient.csproj",
    "MasterServerUI\MasterServerUI.csproj", 
    "MasterStarter\MasterStarter.csproj"
)

foreach ($proj in $projects) {
    Write-Host "   更新 $proj..." -ForegroundColor Gray
    dotnet add $proj package Avalonia --version 11.2.2
    dotnet add $proj package Avalonia.Desktop --version 11.2.2
    dotnet add $proj package Avalonia.Themes.Fluent --version 11.2.2
    dotnet add $proj package Avalonia.Fonts.Inter --version 11.2.2
}
Write-Host "   ✓ Avalonia 包已更新到 11.2.2" -ForegroundColor Green

Write-Host "`n=== 完成 ===" -ForegroundColor Green
