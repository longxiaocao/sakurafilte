-- V24-F13: DictMachine.MachineCategory 列改为 NOT NULL (与实体 string 一致)
-- 先用默认值 'others' 填充现有 NULL 行, 再 ALTER COLUMN SET NOT NULL
UPDATE dict_machine SET machine_category = 'others' WHERE machine_category IS NULL;
ALTER TABLE dict_machine ALTER COLUMN machine_category SET NOT NULL;
ALTER TABLE dict_machine ALTER COLUMN machine_category SET DEFAULT 'others';
