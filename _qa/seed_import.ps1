# Seed import for production stack: products(full) -> xrefs(upsert) -> apps(upsert)
# NOTE: pure ASCII to avoid PowerShell 5.1 encoding issues
$ErrorActionPreference = 'Continue'
$BASE = 'http://localhost:5148'
$hdr = @{ 'X-Admin-Token' = 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C' }

function Wait-EtlDone {
    param([string]$Label, [int]$MaxSec = 1500)
    $start = Get-Date
    for ($i = 0; $i -lt ($MaxSec / 5); $i++) {
        Start-Sleep 5
        try {
            $st = Invoke-RestMethod -Uri "$BASE/api/etl/status" -Headers $hdr -TimeoutSec 10
            if ($st.status -in @('completed', 'failed', 'cancelled')) {
                $sec = [math]::Round(((Get-Date) - $start).TotalSeconds, 1)
                Write-Host "[$Label] status=$($st.status) stage=$($st.stage) read=$($st.read) inserted=$($st.inserted) updated=$($st.updated) skipped=$($st.skipped) errors=$($st.errors) elapsed=${sec}s"
                if ($st.status -ne 'completed') { Write-Host "  lastError: $($st.lastError)" }
                return ($st.status -eq 'completed')
            }
        } catch {
            Write-Host "[$Label] status query failed: $($_.Exception.Message)"
        }
    }
    Write-Host "[$Label] TIMEOUT"
    return $false
}

Write-Host "=== 1/3 import products (full) ==="
try {
    $r = Invoke-RestMethod -Uri "$BASE/api/admin/etl/trigger" -Method Post -Headers $hdr -ContentType 'application/json' -Body '{"jsonlPath":"/app/import/data0827.xlsx","mode":"full","entityType":"products","dryRun":false,"cascade":true}' -TimeoutSec 30
    $r | ConvertTo-Json -Depth 4
} catch { Write-Host "trigger products FAILED: $($_.Exception.Message)" }
$ok1 = Wait-EtlDone -Label 'products'

Write-Host "=== 2/3 import xrefs (upsert) ==="
try {
    $r = Invoke-RestMethod -Uri "$BASE/api/admin/etl/trigger" -Method Post -Headers $hdr -ContentType 'application/json' -Body '{"jsonlPath":"/app/import/oem0827.xlsx","mode":"upsert","entityType":"xrefs","dryRun":false,"cascade":true}' -TimeoutSec 30
    $r | ConvertTo-Json -Depth 4
} catch { Write-Host "trigger xrefs FAILED: $($_.Exception.Message)" }
$ok2 = Wait-EtlDone -Label 'xrefs'

Write-Host "=== 3/3 import apps (upsert) ==="
try {
    $r = Invoke-RestMethod -Uri "$BASE/api/admin/etl/trigger" -Method Post -Headers $hdr -ContentType 'application/json' -Body '{"jsonlPath":"/app/import/dataa0827.xlsx","mode":"upsert","entityType":"apps","dryRun":false,"cascade":true}' -TimeoutSec 30
    $r | ConvertTo-Json -Depth 4
} catch { Write-Host "trigger apps FAILED: $($_.Exception.Message)" }
$ok3 = Wait-EtlDone -Label 'apps'

Write-Host ""
Write-Host "=========== RESULT: products=$ok1 xrefs=$ok2 apps=$ok3 ==========="
if ($ok1 -and $ok2 -and $ok3) { exit 0 } else { exit 1 }
