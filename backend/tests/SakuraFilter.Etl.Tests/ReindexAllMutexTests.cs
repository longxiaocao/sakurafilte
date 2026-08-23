using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Etl;
using Xunit;

namespace SakuraFilter.Etl.Tests;

/// <summary>
/// V2 Task V17-3.5: ReindexAllAsync 互斥性测试
///
/// 测试目标: 验证 ReindexAllAsync 与 ImportXxxAsync 之间的 _activeCts 互斥机制
///   - AcquireActiveCts: 已有任务运行时抛 InvalidOperationException
///   - ReleaseActiveCts: finally 块释放 _activeCts, 允许下次任务获取锁
///   - CancelledToken: 已取消的 CancellationToken 传播到 ReindexAllAsync
///
/// WHY 反射: _activeCts / _activeTaskEntity 是 private 字段, 测试需通过反射预占用
///   以模拟 "已有 ETL 任务在运行" 场景, 无需真实 DB 连接
///
/// 测试边界:
///   - 仅验证互斥逻辑 (AcquireActiveCts/ReleaseActiveCts), 不验证全量重建数据正确性
///   - 全量重建数据正确性需集成测试 (真实 PG + Meilisearch), 留待 v24+
/// </summary>
public class ReindexAllMutexTests
{
    /// <summary>反射缓存: _activeCts 字段 (private CancellationTokenSource?)</summary>
    private static readonly FieldInfo ActiveCtsField =
        typeof(EtlImportService).GetField("_activeCts",
            BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("EtlImportService._activeCts 字段未找到 (反射失败)");

    /// <summary>反射缓存: _activeTaskEntity 字段 (private string?)</summary>
    private static readonly FieldInfo ActiveTaskEntityField =
        typeof(EtlImportService).GetField("_activeTaskEntity",
            BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("EtlImportService._activeTaskEntity 字段未找到 (反射失败)");

    /// <summary>构造一个 EtlImportService 实例 (用空 IServiceProvider + 默认 EtlOptions)
    ///   WHY 空 IServiceProvider: 互斥测试在 AcquireActiveCts 阶段就会抛异常,
    ///   不会走到 _sp.CreateScope() / GetRequiredService, 无需真实服务注册
    /// </summary>
    private static EtlImportService BuildService(string pgConn = "Host=localhost;Database=fake")
    {
        var logger = NullLogger<EtlImportService>.Instance;
        var sp = new ServiceCollection().BuildServiceProvider();
        var options = Options.Create(new EtlOptions());
        return new EtlImportService(pgConn, logger, sp, options);
    }

    /// <summary>通过反射预占用 _activeCts (模拟已有 ETL 任务在运行)</summary>
    private static void PreOccupyActiveCts(EtlImportService svc, string entity = "products")
    {
        var cts = new CancellationTokenSource();
        ActiveCtsField.SetValue(svc, cts);
        ActiveTaskEntityField.SetValue(svc, entity);
    }

    /// <summary>通过反射读取 _activeCts 字段值</summary>
    private static CancellationTokenSource? GetActiveCts(EtlImportService svc)
        => ActiveCtsField.GetValue(svc) as CancellationTokenSource;

    // ===== 互斥性测试 =====

    [Fact]
    public async Task ReindexAll_WhenActiveCtsExists_ThrowsInvalidOperationException()
    {
        // WHY: AcquireActiveCts 检查 _activeCts != null && !IsCancellationRequested
        //   预占用后调用 ReindexAllAsync, 应在 AcquireActiveCts 阶段抛 InvalidOperationException
        //   异常在 try 块之前抛出, 不会进入 catch/finally
        var svc = BuildService();
        PreOccupyActiveCts(svc, entity: "products");

        var act = async () => await svc.ReindexAllAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("已有 ETL 任务在运行");
        ex.Which.Message.Should().Contain("entity=products");
    }

    [Fact]
    public async Task ReindexAll_WhenActiveCtsExists_PreservesExistingCts()
    {
        // WHY: 互斥失败时, 原有 _activeCts 不应被覆盖 (否则会破坏正在运行的 ETL 任务的取消机制)
        //   AcquireActiveCts 抛异常前不应修改 _activeCts 字段
        var svc = BuildService();
        var originalCts = new CancellationTokenSource();
        ActiveCtsField.SetValue(svc, originalCts);
        ActiveTaskEntityField.SetValue(svc, "xrefs");

        try
        {
            await svc.ReindexAllAsync(CancellationToken.None);
        }
        catch (InvalidOperationException) { /* 预期抛出 */ }

        var currentCts = GetActiveCts(svc);
        currentCts.Should().BeSameAs(originalCts, "互斥失败时不应覆盖原有 _activeCts");
    }

    [Fact]
    public async Task ReindexAll_WhenActiveCtsCancelled_DoesNotThrowMutexException()
    {
        // WHY: AcquireActiveCts 检查 _activeCts != null && !IsCancellationRequested
        //   已取消的 _activeCts 视为已释放, 允许新任务获取锁
        // v24 修复后: 后续 _sp.CreateScope() / GetRequiredService 异常被 catch (Exception) 捕获,
        //   返回 ReindexResult (Error 字段含异常信息), 而非抛异常给调用方
        //   修复前: 这些代码在 try 块外, 异常直接抛出 (测试原期望 ThrowAsync<Exception>)
        var svc = BuildService();
        var cancelledCts = new CancellationTokenSource();
        cancelledCts.Cancel();
        ActiveCtsField.SetValue(svc, cancelledCts);
        ActiveTaskEntityField.SetValue(svc, "products");

        // 互斥检查通过, 但后续 _sp.CreateScope() / GetRequiredService 会失败
        //   空 IServiceProvider 无法解析 ProductDbContext, 抛 InvalidOperationException
        //   v24 修复: catch (Exception) 返回 ReindexResult, 不抛异常给调用方
        var result = await svc.ReindexAllAsync(CancellationToken.None);

        result.Should().NotBeNull();
        result.Error.Should().NotBeNull("空 IServiceProvider 解析 ProductDbContext 失败, 应返回失败结果");
        result.Error.Should().NotContain("已有 ETL 任务在运行", "互斥检查已通过, 失败原因应是其他异常");
    }

    // ===== CancelledToken 传播测试 =====

    [Fact]
    public async Task ReindexAll_WithCancelledToken_DoesNotOccupyActiveCtsAfterCall()
    {
        // WHY: 传入已取消的 CancellationToken, AcquireActiveCts 会创建 linked cts (也已取消)
        // v24 修复: 后续代码 (StartSnapshotTimerIfNeeded/CreateScope/conn.OpenAsync) 移入 try 块,
        //   异常被 catch (OperationCanceledException) 捕获, finally 执行 ReleaseActiveCts,
        //   _activeCts 被释放 (不再泄漏)
        //   修复前: conn.OpenAsync(ct) 在 try 块外抛异常, finally 不执行, _activeCts 不释放
        //   (但因 cts 已取消, 下次 AcquireActiveCts 不阻塞 — 这是当时的"可接受泄漏"折中)
        var svc = BuildService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await svc.ReindexAllAsync(cts.Token);

        // v24 修复: finally 块执行 ReleaseActiveCts, _activeCts 被释放 (为 null, 不再泄漏)
        var activeCts = GetActiveCts(svc);
        activeCts.Should().BeNull("v24 修复: AcquireActiveCts 之后的代码移入 try 块, finally 释放 _activeCts");

        // 验证返回值: 空 IServiceProvider 解析 ProductDbContext 抛 InvalidOperationException,
        //   被 catch (Exception) 捕获, 返回失败 ReindexResult (Error 含异常信息)
        //   注意: 不会走到 conn.OpenAsync(ct), 所以不会抛 OperationCanceledException
        result.Error.Should().NotBeNull("空 IServiceProvider 解析 ProductDbContext 失败, 应返回失败结果");
        result.Error.Should().NotContain("已有 ETL 任务在运行", "互斥检查已通过, 失败原因应是其他异常");
    }

    // ===== ReindexResult 字段映射测试 =====

    [Fact]
    public void ReindexResult_Fields_MapCorrectly()
    {
        // WHY: 验证 ReindexResult record 字段与前端 ReindexResult 接口对齐
        //   前端 types.ts: { message, direct, queued, elapsedMs, error? }
        var success = new ReindexResult("全量重建完成: 直接=100, 入队=0", 100, 0, 5000, null);
        success.Message.Should().Be("全量重建完成: 直接=100, 入队=0");
        success.Direct.Should().Be(100);
        success.Queued.Should().Be(0);
        success.ElapsedMs.Should().Be(5000);
        success.Error.Should().BeNull();

        var failure = new ReindexResult("全量重建失败", 0, 0, 100, "Connection refused");
        failure.Error.Should().Be("Connection refused");

        var cancelled = new ReindexResult("全量重建被取消", 0, 0, 50, "CANCELLED");
        cancelled.Error.Should().Be("CANCELLED");
    }

    [Fact]
    public void ReindexResult_WithDefaultError_HasNullError()
    {
        // WHY: ReindexResult record 的 Error 参数有默认值 null
        //   验证省略 Error 参数时默认为 null (成功场景)
        var result = new ReindexResult("ok", 10, 0, 100);
        result.Error.Should().BeNull();
    }
}
