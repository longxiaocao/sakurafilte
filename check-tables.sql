SELECT column_name, is_nullable, column_default 
FROM information_schema.columns 
WHERE table_schema = 'public' AND table_name = 'dict_machine' AND column_name = 'machine_category';
