SELECT n.nspname, c.relname, c.relkind, c.relowner, pg_get_userbyid(c.relowner) AS owner
FROM pg_class c 
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relname ILIKE '%efmigration%';
