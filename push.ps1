param(
    [string]$Remote = "origin",
    [string]$Branch = "master",
    [int]$MaxRetries = 800
)

$sleepBase = 3

for ($i = 1; $i -le $MaxRetries; $i++) {
    Write-Host "Attempt $i/$MaxRetries : git push $Remote $Branch"
    
    # 执行 git push，捕获输出和退出码
    $output = & git push $Remote $Branch 2>&1
    $exitCode = $LASTEXITCODE
    
    # 输出完整信息（可选：只显示前几行，但不要影响退出码）
    Write-Host $output
    
    if ($exitCode -eq 0) {
        Write-Host "Push succeeded on attempt $i" -ForegroundColor Green
        exit 0
    } else {
        Write-Host "Push failed with exit code $exitCode" -ForegroundColor Red
        
        if ($i -lt $MaxRetries) {
            $sleepTime = [Math]::Min($sleepBase * $i, 60)
            Write-Host "Retrying in ${sleepTime} seconds..."
            Start-Sleep -Seconds $sleepTime
        }
    }
}

Write-Host "Push failed after $MaxRetries attempts" -ForegroundColor Red
exit 1