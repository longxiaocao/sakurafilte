using System.Reflection;
using FluentAssertions;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.DTOs;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// V2 架构迁移后端单测 (Task 5.3.1)
/// 覆盖 spec L285 要求的 *_Validate* / *_Duplicate* / machine_type 枚举校验用例
///
/// 测试目标: AdminProductService.ValidateForm (private static)
///   - MR1_REQUIRED: MR.1 必填
///   - MR1_FORMAT_INVALID: 1-10 位字母数字
///   - MR1 最大长度 10 (非 100)
///   - Oem2 必填
///   - machine_type 枚举校验 (5 类白名单)
///   - 字段长度校验 (ProductName1/2/Type/Media 等超长拒绝)
///
/// WHY 用反射: ValidateForm 是 private static, 不暴露公共入口
///   - 反射调用一次 + 缓存 MethodInfo, 避免每次查找开销
///   - 与 ValidatorTests.cs 风格一致 (用 FluentAssertions + xUnit)
/// </summary>
public class V2ValidatorTests
{
    // 反射缓存: ValidateForm 是 private static, 类型签名固定
    //   WHY 静态字段: 测试类多次实例化时复用 MethodInfo, 避免重复反射查找
    private static readonly MethodInfo ValidateFormMethod =
        typeof(AdminProductService).GetMethod("ValidateForm",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("AdminProductService.ValidateForm 未找到 (反射失败, 可能方法名/签名变更)");

    /// <summary>调用私有 ValidateForm, 返回 (success, errorMessage)</summary>
    private static (bool ok, string? error) InvokeValidate(ProductFormDto form)
    {
        try
        {
            ValidateFormMethod.Invoke(null, new object[] { form });
            return (true, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException ae)
        {
            return (false, ae.Message);
        }
        catch (TargetInvocationException ex)
        {
            // 非 ArgumentException 异常直接重新抛出, 便于调试
            throw ex.InnerException ?? ex;
        }
    }

    /// <summary>构造一个默认合法的 ProductFormDto (所有必填字段已填合法值)</summary>
    private static ProductFormDto BuildValidForm() => new()
    {
        Mr1 = "MR000001",
        Oem2 = "OEM2-001",
        ProductName1 = "Air Filter",
        ProductName2 = "Premium",
        Type = "AIR FILTER",
        IsPublished = true,
        CrossReferences = new List<XrefInput>
        {
            new("Air Filter", "BOSCH", "F0001", "OEM2-X", 0, "commercial", true),
        },
        MachineApplications = new List<MachineAppInput>(),
    };

    // ===== MR1_REQUIRED =====

    [Fact]
    public void Validate_Mr1_Null_Throws_MR1_REQUIRED()
    {
        var form = BuildValidForm() with { Mr1 = null };
        var (ok, err) = InvokeValidate(form);
        ok.Should().BeFalse();
        err.Should().Contain("MR1_REQUIRED");
    }

    [Fact]
    public void Validate_Mr1_Empty_Throws_MR1_REQUIRED()
    {
        var form = BuildValidForm() with { Mr1 = "" };
        var (ok, err) = InvokeValidate(form);
        ok.Should().BeFalse();
        err.Should().Contain("MR1_REQUIRED");
    }

    [Fact]
    public void Validate_Mr1_Whitespace_Throws_MR1_REQUIRED()
    {
        var form = BuildValidForm() with { Mr1 = "   " };
        var (ok, err) = InvokeValidate(form);
        ok.Should().BeFalse();
        err.Should().Contain("MR1_REQUIRED");
    }

    // ===== MR1_FORMAT_INVALID =====

    [Theory]
    [InlineData("MR-001")]        // 含连字符
    [InlineData("MR 001")]        // 含空格
    [InlineData("MR_001")]        // 含下划线
    [InlineData("MR.001")]        // 含点
    [InlineData("MR/001")]        // 含斜杠
    [InlineData("MR001!")]        // 含感叹号
    [InlineData("MR001中文")]      // 含中文
    public void Validate_Mr1_InvalidFormat_Throws_MR1_FORMAT_INVALID(string mr1)
    {
        var form = BuildValidForm() with { Mr1 = mr1 };
        var (ok, err) = InvokeValidate(form);
        ok.Should().BeFalse();
        err.Should().Contain("MR1_FORMAT_INVALID");
    }

    [Theory]
    [InlineData("MR000001")]                              // 8 位字母数字 (合法)
    [InlineData("A1B2C3D4E5")]                            // 10 位字母数字 (最大长度)
    [InlineData("M")]                                     // 1 位 (最小长度)
    [InlineData("mr000001")]                              // 小写 (合法, Trim 后大写化在业务层)
    public void Validate_Mr1_ValidFormat_Passes(string mr1)
    {
        var form = BuildValidForm() with { Mr1 = mr1 };
        var (ok, _) = InvokeValidate(form);
        ok.Should().BeTrue($"MR.1 '{mr1}' 符合 1-10 位字母数字规则");
    }

    // ===== MR1 最大长度 10 (非 100) =====

    [Fact]
    public void Validate_Mr1_Length11_Throws_MR1_FORMAT_INVALID()
    {
        // WHY: V2 spec 要求 MR.1 最大 10 位, 超 10 位应被格式校验拦截
        //   (RegularExpression ^[A-Za-z0-9]{1,10}$ 在长度校验之前先触发)
        var form = BuildValidForm() with { Mr1 = "MR000000001" }; // 11 位
        var (ok, err) = InvokeValidate(form);
        ok.Should().BeFalse();
        err.Should().Contain("MR1_FORMAT_INVALID");
    }

    // ===== Oem2 必填 =====

    [Fact]
    public void Validate_Oem2_Null_Throws()
    {
        var form = BuildValidForm() with { Oem2 = null! };
        var (ok, err) = InvokeValidate(form);
        ok.Should().BeFalse();
        err.Should().Contain("Oem2");
    }

    [Fact]
    public void Validate_Oem2_Empty_Throws()
    {
        var form = BuildValidForm() with { Oem2 = "" };
        var (ok, err) = InvokeValidate(form);
        ok.Should().BeFalse();
        err.Should().Contain("Oem2");
    }

    // ===== 字段长度校验 =====

    [Theory]
    [InlineData(nameof(ProductFormDto.ProductName1), 101)]
    [InlineData(nameof(ProductFormDto.ProductName2), 101)]
    [InlineData(nameof(ProductFormDto.Type), 51)]
    [InlineData(nameof(ProductFormDto.Media), 101)]
    [InlineData(nameof(ProductFormDto.MediaModel), 101)]
    [InlineData(nameof(ProductFormDto.D7Thread), 101)]
    [InlineData(nameof(ProductFormDto.D8Thread), 101)]
    [InlineData(nameof(ProductFormDto.Efficiency1), 101)]
    [InlineData(nameof(ProductFormDto.Efficiency2), 101)]
    [InlineData(nameof(ProductFormDto.SealingMaterial), 101)]
    [InlineData(nameof(ProductFormDto.TempRange), 101)]
    public void Validate_FieldTooLong_Throws(string fieldName, int length)
    {
        // WHY: P2-2 修复 — 字段超长时 PG 报 22001 返回 500, 应在校验层提前拦截返回 400
        var form = BuildValidForm();
        var tooLong = new string('a', length);
        form = fieldName switch
        {
            nameof(ProductFormDto.ProductName1) => form with { ProductName1 = tooLong },
            nameof(ProductFormDto.ProductName2) => form with { ProductName2 = tooLong },
            nameof(ProductFormDto.Type) => form with { Type = tooLong },
            nameof(ProductFormDto.Media) => form with { Media = tooLong },
            nameof(ProductFormDto.MediaModel) => form with { MediaModel = tooLong },
            nameof(ProductFormDto.D7Thread) => form with { D7Thread = tooLong },
            nameof(ProductFormDto.D8Thread) => form with { D8Thread = tooLong },
            nameof(ProductFormDto.Efficiency1) => form with { Efficiency1 = tooLong },
            nameof(ProductFormDto.Efficiency2) => form with { Efficiency2 = tooLong },
            nameof(ProductFormDto.SealingMaterial) => form with { SealingMaterial = tooLong },
            nameof(ProductFormDto.TempRange) => form with { TempRange = tooLong },
            _ => throw new ArgumentException($"未知字段: {fieldName}"),
        };
        var (ok, err) = InvokeValidate(form);
        ok.Should().BeFalse();
        err.Should().Contain(fieldName).And.Contain("不能超过");
    }

    [Fact]
    public void Validate_Mr1_TooLong_Throws_LengthCheck()
    {
        // WHY: V2 Mr1 最大 10 字符, 用 50 字符 (避免正则先拦截, 测试长度校验路径)
        //   注意: 11 位已由 MR1_FORMAT_INVALID 测试覆盖 (正则拦截)
        //   此用例仅作回归: 当正则被误改为 {1,50} 时, 长度校验仍能拦截
        //   但 50 位仍会被正则拒绝 (因为 {1,10}), 所以期望 MR1_FORMAT_INVALID
        var form = BuildValidForm() with { Mr1 = new string('A', 50) };
        var (ok, err) = InvokeValidate(form);
        ok.Should().BeFalse();
        err.Should().Contain("MR1");
    }

    // ===== machine_type 枚举校验 (5 类白名单) =====

    [Theory]
    [InlineData("agriculture")]
    [InlineData("commercial")]
    [InlineData("construction")]
    [InlineData("industrial")]
    [InlineData("others")]
    public void Validate_MachineType_ValidEnum_Passes(string machineType)
    {
        var form = BuildValidForm() with
        {
            CrossReferences = new List<XrefInput>
            {
                new("Air Filter", "BOSCH", "F0001", "OEM2-X", 0, machineType, true),
            },
        };
        var (ok, _) = InvokeValidate(form);
        ok.Should().BeTrue($"machine_type '{machineType}' 在白名单内");
    }

    [Theory]
    [InlineData("Agriculture")]     // 大小写敏感
    [InlineData("AGRICULTURE")]
    [InlineData("agri")]
    [InlineData("mining")]          // 不在白名单
    [InlineData("automotive")]      // 不在白名单
    [InlineData("other")]           // 拼写错误 (others 才合法)
    [InlineData("unknown")]
    public void Validate_MachineType_InvalidEnum_Throws_MACHINE_TYPE_INVALID(string machineType)
    {
        var form = BuildValidForm() with
        {
            CrossReferences = new List<XrefInput>
            {
                new("Air Filter", "BOSCH", "F0001", "OEM2-X", 0, machineType, true),
            },
        };
        var (ok, err) = InvokeValidate(form);
        ok.Should().BeFalse();
        err.Should().Contain("MACHINE_TYPE_INVALID");
    }

    [Fact]
    public void Validate_MachineType_Null_Allowed()
    {
        // WHY: machine_type 可空 (CrossReference.MachineType 是 string?), null 不触发枚举校验
        var form = BuildValidForm() with
        {
            CrossReferences = new List<XrefInput>
            {
                new("Air Filter", "BOSCH", "F0001", "OEM2-X", 0, null, true),
            },
        };
        var (ok, _) = InvokeValidate(form);
        ok.Should().BeTrue("machine_type=null 应允许 (字段可空)");
    }

    [Fact]
    public void Validate_MachineType_Empty_Allowed()
    {
        // WHY: 空字符串视为 "未填写", 与 null 等价 (IsNullOrEmpty 判断)
        var form = BuildValidForm() with
        {
            CrossReferences = new List<XrefInput>
            {
                new("Air Filter", "BOSCH", "F0001", "OEM2-X", 0, "", true),
            },
        };
        var (ok, _) = InvokeValidate(form);
        ok.Should().BeTrue("machine_type='' 应允许 (等价 null)");
    }

    // ===== 完整合法表单 (happy path) =====

    [Fact]
    public void Validate_FullValidForm_Passes()
    {
        // WHY: 端到端 happy path — 所有字段填合法值, 应整体通过
        //   覆盖 V2 全字段: MR.1 / OEM 2 / IsPublished / machine_type / 多 CrossReferences
        var form = BuildValidForm() with
        {
            Mr1 = "MR000001",
            Oem2 = "OEM2-001",
            ProductName1 = "Air Filter",
            ProductName2 = "Premium",
            Type = "AIR FILTER",
            IsPublished = true,
            Remark = "Test product for V2 validation",
            CrossReferences = new List<XrefInput>
            {
                new("Air Filter", "BOSCH", "F0001", "OEM2-A", 10, "commercial", true),
                new("Air Filter", "MANN",  "F0002", "OEM2-B", 20, "industrial", true),
                new("Air Filter", "WIX",   "F0003", "OEM2-C", 30, "others", false), // 不发布
            },
        };
        var (ok, _) = InvokeValidate(form);
        ok.Should().BeTrue("完整合法 V2 表单应通过校验");
    }
}
