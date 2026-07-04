using FluentValidation;
using SakuraFilter.Api.Controllers;

namespace SakuraFilter.Api.Services.Validators;

/// <summary>
/// 安全加固阶段4: 登录请求验证器
/// 规则:
///   - Username: 非空, 最大 64 字符
///   - Password: 非空, 最大 128 字符 (不强制最小长度, 弱密码由账户锁定防暴力破解)
/// </summary>
public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("用户名不能为空")
            .MaximumLength(64).WithMessage("用户名长度不能超过 64 字符");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("密码不能为空")
            .MaximumLength(128).WithMessage("密码长度不能超过 128 字符");
    }
}
