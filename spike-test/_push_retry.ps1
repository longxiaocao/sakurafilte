# P5 push retry (ASCII only for PowerShell 5 parser)
$attempt = 0
$max = 30
Set-Location "d:\projects\sakurafilter"
while ($attempt -lt $max) {
    $attempt++
    $ts = Get-Date -Format "HH:mm:ss"
    Write-Host "[$ts] push try $attempt / $max"
    $output = git push origin master 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -eq 0) {
        Write-Host "[OK] push success (attempt $attempt)"
        $output | ForEach-Object { Write-Host "  $_" }
        exit 0
    } else {
        $firstLine = ($output | Select-Object -First 1)
        Write-Host "[RETRY $attempt] $firstLine"
        Start-Sleep -Seconds 8
    }
}
Write-Host "[FAIL] 30 retries exhausted"
exit 1
