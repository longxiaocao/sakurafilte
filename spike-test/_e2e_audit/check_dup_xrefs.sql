-- 检查 cross_references 重复 (oem_brand, oem_no_3) 数据规模
SELECT 'total_rows' AS metric, COUNT(*) AS value FROM cross_references
UNION ALL
SELECT 'dup_groups', COUNT(*) FROM (
    SELECT oem_brand, oem_no_3 FROM cross_references
    WHERE is_discontinued = false
    GROUP BY oem_brand, oem_no_3 HAVING COUNT(*) > 1
) t
UNION ALL
SELECT 'dup_rows', SUM(cnt) FROM (
    SELECT COUNT(*) AS cnt FROM cross_references
    WHERE is_discontinued = false
    GROUP BY oem_brand, oem_no_3 HAVING COUNT(*) > 1
) t;

-- 查看重复数据样例
SELECT oem_brand, oem_no_3, COUNT(*) AS cnt, array_agg(id) AS ids
FROM cross_references
WHERE is_discontinued = false
GROUP BY oem_brand, oem_no_3
HAVING COUNT(*) > 1
ORDER BY cnt DESC
LIMIT 10;
