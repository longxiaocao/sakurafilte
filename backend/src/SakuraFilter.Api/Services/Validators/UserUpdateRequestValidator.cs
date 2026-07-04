using FluentValidation;
using SakuraFilter.Api.Controllers;

namespace SakuraFilter.Api.Services.Validators;

/// <summary>
/// 安全加固阶段4: 修改用户请求验证器
/// 规则 (字段允许空, 空表示不更新):
///   - Role: 非空时必须为 admin/operator/viewer
///   - Email: 非空时合法邮箱格式
///   - FullName: 非空时最大 64 字符
/// 注意: IsActive 是 bool?, 不在此验证 (绑定层保证类型正确)
/// </summary>
public class UserUpdateRequestValidator : AbstractValidator<UpdateUserRequest>
{
    private static readonly string[] ValidRoles = { "admin", "operator", "viewer" };

    public UserUpdateRequestValidator()
    {
        RuleFor(x => x.Role)
            .Must(r => string.IsNullOrEmpty(r) || ValidRoles.Contains(r))
            .WithMessage($"角色必须为 {string.Join("/", ValidRoles)} 之一");

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("邮箱格式不合法")
            .When(x => !string.IsNullOrEmpty(x.Email));

        RuleFor(x => x.FullName)
            .MaximumLength(64).WithMessage("姓名长度不能超过 64 字符")
            .When(x => !string.IsNullOrEmpty(x.FullName));
    }
}
