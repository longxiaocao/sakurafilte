-- idempotent 脚本, 可重复执行: 039 机型-MR.1 批量绑定关系表 (machine_mr1_bindings)
-- 039: machine_mr1_bindings 建表 (2026-10-03)
--   WHY: 实体 SakuraFilter.Core/Entities/Product.cs::MachineMr1Binding + ProductDbContext L283-295
--        已声明该表 (Task 2「批量绑定 MR.1 到机型」), 但 EF 迁移与 SQL 迁移**均无建表脚本**,
--        且实体未进 ProductDbContextModelSnapshot -> 生产库/CI 空库都缺表。
--        运行时 MachineDictService.BatchBindAsync (L278/L291/L306) 会 SELECT/INSERT 该表,
--        调用 POST /api/admin/machine-apps/batch-bind 直接 42P01 relation does not exist。
--        前端 AdminMachinesView.vue L210 调用该端点 -> 机型批量绑定功能整体不可用。
--   结构对齐实体: id BIGSERIAL PK / machine_id BIGINT NOT NULL / mr_1 VARCHAR(10) NOT NULL /
--        created_at TIMESTAMPTZ NOT NULL DEFAULT now()
--   索引对齐 ProductDbContext 配置: uq_machine_mr1_bindings(machine_id, mr_1) UNIQUE
--        (幂等保证, 等价 ON CONFLICT DO NOTHING) + idx_machine_mr1_bindings_machine(machine_id)
-- **idempotent 脚本, 可重复执行**

CREATE TABLE IF NOT EXISTS machine_mr1_bindings (
    id          BIGSERIAL PRIMARY KEY,
    machine_id  BIGINT       NOT NULL,
    mr_1        VARCHAR(10)  NOT NULL,
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_machine_mr1_bindings
    ON machine_mr1_bindings (machine_id, mr_1);

CREATE INDEX IF NOT EXISTS idx_machine_mr1_bindings_machine
    ON machine_mr1_bindings (machine_id);
