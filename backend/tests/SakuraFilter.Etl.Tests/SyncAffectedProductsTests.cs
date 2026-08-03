using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SakuraFilter.Etl;
using Xunit;

namespace SakuraFilter.Etl.Tests;

/// <summary>
/// 🔧 fix(自动reindex): SyncAffectedProductsAsync 行为测试
///
/// 测试目标: xrefs/apps 导入完成后自动触达 Meili 增量同步的触发逻辑
///   - 空受影响集合: 直接返回, 不进入 meili-sync 阶段 (无产品需同步)
///   - 非空集合: 设置 stage=meili-sync 并触发后台同步任务 (不 await, fire-and-forget)
///
/// WHY 反射: _stage 是 private 字段, SyncAffectedProductsAsync 是 private 方法
/// 测试边界:
///   - 不验证实际索引写入 (需真实 PG + Meilisearch, 由生产栈演练验证)
///   - 不验证 touch updated_at SQL (需真实 PG)
/// </summary>
public class SyncAffectedProductsTests
{
    private static readonly FieldInfo StageField =
        typeof(EtlProgress).GetField("_stage", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("EtlProgress._stage 字段未找到 (反射失败)");

    private static readonly MethodInfo SyncMethod = FindSyncMethod();

    /// <summary>定位 SyncAffectedProductsAsync (失败时输出全部 Sync 方法名辅助诊断)</summary>
    private static MethodInfo FindSyncMethod()
    {
        var methods = typeof(EtlImportService).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance);
        var sync = methods.Where(m => m.Name.StartsWith("Sync")).Select(m => m.Name).ToList();
        if (sync.Count == 0)
        {
            throw new InvalidOperationException($"EtlImportService 无 Sync 开头方法 (非公共方法总数 {methods.Length})");
        }
        return methods.First(m => m.Name == "SyncAffectedProductsAsync");
    }

    private static EtlImportService BuildService()
    {
        var logger = NullLogger<EtlImportService>.Instance;
        var sp = new ServiceCollection().BuildServiceProvider();
        var options = Options.Create(new EtlOptions());
        // 空集合短路发生在 CreateScope 之前; 非空集合路径的空 SP 会让后台任务在 CreateScope 处抛异常,
        //   被 catch 吞掉并进入 finally (stage 复位), 恰好可验证 finally 竞态防护
        return new EtlImportService("Host=localhost;Database=fake", logger, sp, options);
    }

    private static Task InvokeSync(EtlImportService svc, HashSet<long> ids, string entity)
        => (Task)SyncMethod.Invoke(svc, new object[] { ids, entity, CancellationToken.None })!;

    /// <summary>覆盖: ADR #27 自动 reindex — 空受影响产品集合时短路, 不改 stage</summary>
    [Fact]
    public void SyncAffectedProducts_EmptySet_ShortCircuitsWithoutStageChange()
    {
        var svc = BuildService();
        var progress = svc.Progress;
        progress.Reset();
        progress.Start("/tmp/etl/xrefs.jsonl");
        progress.SetStage("staging");

        var task = InvokeSync(svc, new HashSet<long>(), "xrefs");

        task.IsCompleted.Should().BeTrue("空集合应同步返回");
        StageField.GetValue(progress).Should().Be("staging", "空集合不应改变 stage (不进入 meili-sync)");
    }

    /// <summary>覆盖: ADR #27 自动 reindex — 非空集合进入 meili-sync, 后台任务 finally 复位 idle</summary>
    [Fact]
    public void SyncAffectedProducts_NonEmptySet_EntersMeiliSyncAndResetsIdle()
    {
        var svc = BuildService();
        var progress = svc.Progress;
        progress.Reset();
        progress.Start("/tmp/etl/xrefs.jsonl");
        progress.SetStage("staging");

        var task = InvokeSync(svc, new HashSet<long> { 1L, 2L, 3L }, "xrefs");

        task.IsCompleted.Should().BeTrue("触发方法本身应同步返回 (后台任务 fire-and-forget)");
        StageField.GetValue(progress).Should().Be("meili-sync", "非空集合应进入 meili-sync 阶段");

        // 等待后台任务完成 (空 SP → CreateScope 抛异常 → catch → finally 复位 idle)
        //   条件竞态防护: 后台任务 finally 仅在 stage 仍为 meili-sync 时复位
        Thread.Sleep(500);
        StageField.GetValue(progress).Should().Be("idle", "后台任务 finally 应将 stage 复位 idle");
    }
}
