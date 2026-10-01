# Re-run apps import only
$ErrorActionPreference = 'Continue'
$BASE = 'http://localhost:5148'
$hdr = @{ 'X-Admin-Token' = 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C' }

Write-Host "=== apps status before ==="
try { Invoke-RestMethod -Uri "$BASE/api/etl/status" -Headers $hdr -TimeoutSec 10 | ConvertTo-Json -Depth 3 } catch { Write-Host "query failed: $($_.Exception.Message)" }

Write-Host "=== trigger apps ==="
try {
    $r = Invoke-RestMethod -Uri "$BASE/api/admin/etl/trigger" -Method Post -Headers $hdr -ContentType 'application/json' -Body '{"jsonlPath":"/app/import/dataa0827.xlsx","mode":"upsert","entityType":"apps","dryRun":false,"cascade":true}' -TimeoutSec 300
    $r | ConvertTo-Json -Depth 4
} catch { Write-Host "trigger FAILED: $($_.Exception.Message)" }

$start = Get-Date
for ($i = 0; $i -lt 240; $i++) {
    Start-Sleep 5
    try {
        $st = Invoke-RestMethod -Uri "$BASE/api/etl/status" -Headers $hdr -TimeoutSec 10
        if ($st.status -in @('completed', 'failed', 'cancelled')) {
            $sec = [math]::Round(((Get-Date) - $start).TotalSeconds, 1)
            Write-Host "[apps] status=$($st.status) read=$($st.read) inserted=$($st.inserted) updated=$($st.updated) skipped=$($st.skipped) errors=$($st.errors) elapsed=${sec}s"
            if ($st.status -ne 'completed') { Write-Host "  lastError: $($st.lastError)" }
            if ($st.status -eq 'completed') { exit 0 } else { exit 1 }
        }
    } catch { Write-Host "query failed: $($_.Exception.Message)" }
}
Write-Host "[apps] TIMEOUT"
exit 1
