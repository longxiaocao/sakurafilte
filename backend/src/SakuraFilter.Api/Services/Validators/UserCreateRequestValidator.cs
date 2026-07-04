using FluentValidation;
using SakuraFilter.Api.Controllers;

namespace SakuraFilter.Api.Services.Validators;

/// <summary>
/// 安全加固阶段4: 创建用户请求验证器
/// 规则:
///   - Username: 非空, 3-64 字符, 仅允许 a-zA-Z0-9_-
///   - Password: 非空, 8-128 字符, 至少 1 字母 + 1 数字
///   - Role: 非空, 必须为 admin/operator/viewer
///   - Email: 合法邮箱格式 (允许空)
///   - FullName: 最大 64 字符 (允许空)
/// WHY Username 限制字符集: 防止注入特殊字符, 后续日志/审计路径不会因特殊字符出错
/// </summary>
public class UserCreateRequestValidator : AbstractValidator<CreateUserRequest>
{
    private static readonly string[] ValidRoles = { "admin", "operator", "viewer" };

    public UserCreateRequestValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("用户名不能为空")
            .MinimumLength(3).WithMessage("用户名长度不能少于 3 字符")
            .MaximumLength(64).WithMessage("用户名长度不能超过 64 字符")
            .Matches("^[a-zA-Z0-9_-]+$").WithMessage("用户名仅允许字母、数字、下划线和连字符");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("密码不能为空")
            .MinimumLength(8).WithMessage("密码长度不能少于 8 位")
            .MaximumLength(128).WithMessage("密码长度不能超过 128 字符")
            .Matches("[a-zA-Z]").WithMessage("密码必须包含至少 1 个字母")
            .Matches("[0-9]").WithMessage("密码必须包含至少 1 个数字");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("角色不能为空")
            .Must(r => ValidRoles.Contains(r)).WithMessage($"角色必须为 {string.Join("/", ValidRoles)} 之一");

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("邮箱格式不合法")
            .When(x => !string.IsNullOrEmpty(x.Email));

        RuleFor(x => x.FullName)
            .MaximumLength(64).WithMessage("姓名长度不能超过 64 字符")
            .When(x => !string.IsNullOrEmpty(x.FullName));
    }
}
