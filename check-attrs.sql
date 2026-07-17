SELECT a.attname, format_type(a.atttypid, a.atttypmod) AS type
FROM pg_attribute a 
JOIN pg_class c ON c.oid = a.attrelid
WHERE c.relname = '__EFMigrationsHistory' AND a.attnum > 0 AND NOT a.attisdropped;
