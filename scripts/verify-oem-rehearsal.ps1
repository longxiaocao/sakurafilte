$ErrorActionPreference = 'Stop'

# 仅校验 OEM 演练库，不连接生产库；新批次导入后可重复执行此门禁。

$sql = @'
WITH checks AS (
    SELECT 'oem_products' AS name, COUNT(*)::bigint AS actual, 49391::bigint AS expected
    FROM catalog.oem_products
    UNION ALL
    SELECT 'cross_references', COUNT(*)::bigint, 529499::bigint
    FROM catalog.oem_cross_references
    UNION ALL
    SELECT 'machine_applications', COUNT(*)::bigint, 709843::bigint
    FROM catalog.oem_machine_applications
    UNION ALL
    SELECT 'active_mr1_mappings', COUNT(*)::bigint, 0::bigint
    FROM catalog.oem_mr1_mappings WHERE ended_at IS NULL
    UNION ALL
    SELECT 'sh56212_xrefs', COUNT(*)::bigint, 281::bigint
    FROM catalog.oem_cross_references x
    JOIN catalog.oem_products p ON p.id = x.oem_product_id
    WHERE p.oem_no_1_normalized = 'SH 56212'
    UNION ALL
    SELECT 'sh56212_applications', COUNT(*)::bigint, 10515::bigint
    FROM catalog.oem_machine_applications a
    JOIN catalog.oem_products p ON p.id = a.oem_product_id
    WHERE p.oem_no_1_normalized = 'SH 56212'
), structural AS (
    SELECT 'empty_oem' AS name, COUNT(*)::bigint AS actual, 0::bigint AS expected
    FROM catalog.oem_products WHERE btrim(oem_no_1_normalized) = ''
    UNION ALL
    SELECT 'duplicate_oem', COUNT(*)::bigint, 0::bigint
    FROM (SELECT oem_no_1_normalized FROM catalog.oem_products GROUP BY oem_no_1_normalized HAVING COUNT(*) > 1) d
    UNION ALL
    SELECT 'duplicate_xref_key', COUNT(*)::bigint, 0::bigint
    FROM (
        SELECT oem_product_id, COALESCE(oem_no_3_key, ''), COALESCE(oem_brand_key, '')
        FROM catalog.oem_cross_references
        GROUP BY 1, 2, 3 HAVING COUNT(*) > 1
    ) d
)
SELECT name || '=' || actual || '/' || expected
FROM (SELECT * FROM checks UNION ALL SELECT * FROM structural) all_checks
WHERE actual <> expected;
'@

$dockerArgs = @('exec', 'sakura-postgres', 'psql', '-U', 'postgres', '-d', 'sakurafilter_oem_rehearsal', '-Atc', $sql)
$result = & docker @dockerArgs
if ($LASTEXITCODE -ne 0) {
    throw '演练库查询失败: sakura-postgres/sakurafilter_oem_rehearsal'
}

if ($result) {
    throw "OEM 演练验收失败:`n$($result -join "`n")"
}

Write-Output 'OEM 演练验收通过: sakura-postgres/sakurafilter_oem_rehearsal'
