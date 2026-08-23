using Npgsql;

namespace SakuraFilter.Etl;

/// <summary>
/// typeahead_dict 全量重建服务 — ETL 与管理员手动端点共用的实现。
///
/// WHY 独立服务 (P1-交付门禁 2026-08-21):
///   typeahead_dict 是公开自动补全的唯一数据源 (2026-08-20 起 dict 为唯一权威源),
///   但 ETL 导入成功后不会自动刷新它 —— 新导入的 OEM/机型/发动机数据虽已进 PG+Meili,
///   自动补全却长期找不到, 直到有人记得手动调 /api/admin/typeahead/rebuild。
///   本服务把重建逻辑抽为单一来源, 供 ETL 完成路径与 Admin 端点复用:
///     - 保证 SQL 单一来源 (端点与 ETL 不会漂移)
///     - ETL 成功导入后自动刷新快照, 消除数据陈旧风险
///
/// 原子替换 (2026-08-21 实测修正):
///   初版用 TRUNCATE + INSERT, 但百万级数据 (1465 万行 distinct) 实测耗时 3m9s,
///   期间 typeahead_dict 为空表 → 公开自动补全短暂失效。手动调用人挑时机可忍,
///   但 ETL 自动触发会把空窗常态化。改为双表交换:
///     1) 建 typeahead_dict_new (无锁, 旧表继续服务查询)
///     2) 填充 + 建 partial GIN 索引 (旧表无锁)
///     3) 事务内 DROP 旧表 + RENAME 新表 (毫秒级原子切换, 无空窗)
///
/// 放 Etl 项目 (而非 Api): EtlImportService 需要调用, Api 项目已引用 Etl, 方向无循环。
/// 连接: 注入全局 NpgsqlDataSource 单例 (v30-25 P0: 统一连接池, 不自建连接)
/// 失败策略: 重建失败不阻塞 ETL 结果 (调用方记日志), 与 meili-sync 后台任务同模式。
/// </summary>
public class TypeaheadDictRebuildService
{
    /// <summary>
    /// 并发互斥 (2026-08-22 Codex 审查 P1 修复): ETL products/xrefs/apps 导入完成
    /// 各自 fire-and-forget 触发 RebuildAsync, 并发重建会互相 DROP typeahead_dict_new 临时表
    /// (第 1 步 DROP IF EXISTS 会把对方的表删掉 → INSERT 到不存在表报错 / 切换混乱)。
    /// 进程内 SemaphoreSlim 串行化 (单实例部署足够; 多实例需叠加 pg advisory lock)。
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>ETL 合并式触发状态: 1=有重建在执行 (RequestRebuildAsync 路径)</summary>
    private int _rebuildRunning;

    /// <summary>ETL 合并式触发状态: 1=执行期间来了新请求, 当前重建完成后需补跑一次</summary>
    private int _rebuildPending;

    /// <summary>临时表名 (原子替换用; 后缀 _new 避免与业务查询冲突)</summary>
    private const string TempTable = "typeahead_dict_new";

    /// <summary>线上表名 (023 迁移创建)</summary>
    private const string LiveTable = "typeahead_dict";

    /// <summary>
    /// 填充 SQL: 全量 distinct 快照写入临时表 (不碰线上 typeahead_dict)。
    /// 注意: 023 迁移注释称"万行级秒级完成" — 实际百万数据下 1465 万行, 耗时约 3 分钟。
    /// </summary>
    public const string FillSql = """
        INSERT INTO typeahead_dict_new (field, value)
        SELECT 'oem-brand', oem_brand FROM cross_references WHERE oem_brand IS NOT NULL AND oem_brand <> '' GROUP BY oem_brand
        UNION ALL
        SELECT 'oem-no2', oem_2 FROM products WHERE oem_2 IS NOT NULL AND oem_2 <> '' GROUP BY oem_2
        UNION ALL
        SELECT 'oem-no3', oem_no_3 FROM cross_references WHERE oem_no_3 IS NOT NULL AND oem_no_3 <> '' GROUP BY oem_no_3
        UNION ALL
        SELECT 'machine-brand', machine_brand FROM machine_applications WHERE machine_brand IS NOT NULL AND machine_brand <> '' GROUP BY machine_brand
        UNION ALL
        SELECT 'machine-model', machine_model FROM machine_applications WHERE machine_model IS NOT NULL AND machine_model <> '' GROUP BY machine_model
        UNION ALL
        SELECT 'model-name', model_name FROM machine_applications WHERE model_name IS NOT NULL AND model_name <> '' GROUP BY model_name
        UNION ALL
        SELECT 'engine-brand', engine_brand FROM machine_applications WHERE engine_brand IS NOT NULL AND engine_brand <> '' GROUP BY engine_brand
        UNION ALL
        SELECT 'engine-type', engine_type FROM machine_applications WHERE engine_type IS NOT NULL AND engine_type <> '' GROUP BY engine_type;
        """;

    /// <summary>
    /// 索引 SQL: 与 024 迁移一致的 per-field partial GIN (低/中基数字段)。
    ///   oem-no3/engine-type 近唯一 (基数守卫短路) 不建索引, 与 024 对齐。
    /// 命名用 _new 后缀: PG 索引名 schema 级唯一, 线上 typeahead_dict 已有 024 建的
    ///   ix_td_*_trgm, 若在临时表上建同名索引会被 CREATE INDEX IF NOT EXISTS 静默跳过
    ///   (已建过则不建), DROP 旧表时索引随之消失 → 切换后丢索引 (2026-08-21 实测踩坑)。
    ///   切换后 ALTER INDEX RENAME 回正式名 (见 RebuildAsync 步骤 4)。
    /// </summary>
    public const string IndexSql = """
        CREATE INDEX IF NOT EXISTS ix_td_oem_brand_trgm_new ON typeahead_dict_new USING gin (value gin_trgm_ops) WHERE field = 'oem-brand';
        CREATE INDEX IF NOT EXISTS ix_td_oem_no2_trgm_new ON typeahead_dict_new USING gin (value gin_trgm_ops) WHERE field = 'oem-no2';
        CREATE INDEX IF NOT EXISTS ix_td_machine_brand_trgm_new ON typeahead_dict_new USING gin (value gin_trgm_ops) WHERE field = 'machine-brand';
        CREATE INDEX IF NOT EXISTS ix_td_machine_model_trgm_new ON typeahead_dict_new USING gin (value gin_trgm_ops) WHERE field = 'machine-model';
        CREATE INDEX IF NOT EXISTS ix_td_model_name_trgm_new ON typeahead_dict_new USING gin (value gin_trgm_ops) WHERE field = 'model-name';
        CREATE INDEX IF NOT EXISTS ix_td_engine_brand_trgm_new ON typeahead_dict_new USING gin (value gin_trgm_ops) WHERE field = 'engine-brand';
        """;

    private readonly NpgsqlDataSource _dataSource;

    public TypeaheadDictRebuildService(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    /// <summary>执行全量重建 (原子替换, 无空窗)。调用方负责错误处理 (失败记日志, 不阻塞 ETL)。</summary>
    public async Task RebuildAsync(CancellationToken ct = default)
    {
        // 🔧 fix(2026-08-22 Codex 审查 P1): SemaphoreSlim 串行化 — 并发调用排队执行, 防止互删临时表
        await _gate.WaitAsync(ct);
        try
        {
            await RebuildCoreAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 🔧 fix(2026-08-23 Codex 审查): ETL 合并式触发入口 (防抖合并)。
    /// WHY: ETL products/xrefs/apps 三流程连续完成会各触发一次全量重建 (3-5 分钟 × 3,
    ///   重复扫描数千万行 + CPU/IO 峰值延长)。本方法合并短时间内的多个触发:
    ///   - 首个触发者执行重建; 执行期间新触发只置 _rebuildPending 标志;
    ///   - 当前重建完成后若发现 pending 则补跑一次 (确保包含最新数据), 收敛后结束。
    /// 互斥: 内部经 _gate 与手动 RebuildAsync 串行 (防止互删临时表)。
    /// 语义: 重建期间来新请求 → 完成后补跑, 不丢数据。
    /// 注意: 多实例部署需叠加 pg advisory lock (见 RebuildAsync 注释)。
    /// </summary>
    public async Task RequestRebuildAsync(CancellationToken ct = default)
    {
        // 已有重建在执行 → 标记 pending, 由执行者完成后补跑 (自身立即返回)
        if (Interlocked.CompareExchange(ref _rebuildRunning, 1, 0) != 0)
        {
            Interlocked.Exchange(ref _rebuildPending, 1);
            return;
        }
        try
        {
            await _gate.WaitAsync(ct);
            try
            {
                do
                {
                    Interlocked.Exchange(ref _rebuildPending, 0);
                    await RebuildCoreAsync(ct);
                } while (Volatile.Read(ref _rebuildPending) != 0);
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _rebuildRunning, 0);
        }
    }

    private async Task RebuildCoreAsync(CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // 1) 清理上次失败残留的临时表 + 重建 (旧表无锁, 查询不受影响)
        await using (var cmd = new NpgsqlCommand(
            $"""
            DROP TABLE IF EXISTS {TempTable};
            CREATE TABLE {TempTable} (
                field  TEXT NOT NULL,
                value  TEXT NOT NULL,
                PRIMARY KEY (field, value)
            );
            """, conn))
        {
            cmd.CommandTimeout = 120;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 2) 填充全量 distinct (耗时 ~3min, 旧表继续服务)
        await using (var cmd = new NpgsqlCommand(FillSql, conn))
        {
            cmd.CommandTimeout = 600;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 3) 建 partial GIN 索引 (同 024; 旧表仍可查)
        await using (var cmd = new NpgsqlCommand(IndexSql, conn))
        {
            cmd.CommandTimeout = 600;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 4) 原子切换: 事务内 DROP 旧表 + RENAME 新表 (毫秒级, 无空窗)
        //    随后把 _new 后缀索引改回正式名 (ix_td_*_trgm) + pkey 名修正
        //    WHY: 切换瞬间 DROP/RENAME 是元数据操作, 但索引名若保持 _new 后缀,
        //      024 迁移幂等 (CREATE INDEX IF NOT EXISTS 正式名) 会重建重复索引
        //    实现: 单事务内逐条执行, 避免单条多语句串在事务中的兼容性风险
        //      (2026-08-21 实测: psql 多语句 BEGIN/COMMIT 串整体回滚; Npgsql 分步 + 显式事务最稳)
        await using var tx = await conn.BeginTransactionAsync(ct);
        var swapSqls = new[]
        {
            $"DROP TABLE {LiveTable}",
            $"ALTER TABLE {TempTable} RENAME TO {LiveTable}",
            "ALTER INDEX ix_td_oem_brand_trgm_new RENAME TO ix_td_oem_brand_trgm",
            "ALTER INDEX ix_td_oem_no2_trgm_new RENAME TO ix_td_oem_no2_trgm",
            "ALTER INDEX ix_td_machine_brand_trgm_new RENAME TO ix_td_machine_brand_trgm",
            "ALTER INDEX ix_td_machine_model_trgm_new RENAME TO ix_td_machine_model_trgm",
            "ALTER INDEX ix_td_model_name_trgm_new RENAME TO ix_td_model_name_trgm",
            "ALTER INDEX ix_td_engine_brand_trgm_new RENAME TO ix_td_engine_brand_trgm",
            "ALTER INDEX typeahead_dict_new_pkey RENAME TO typeahead_dict_pkey",
        };
        foreach (var sql in swapSqls)
        {
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.CommandTimeout = 120;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }
}
