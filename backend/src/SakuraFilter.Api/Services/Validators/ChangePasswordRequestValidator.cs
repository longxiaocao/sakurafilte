using FluentValidation;
using SakuraFilter.Api.Controllers;

namespace SakuraFilter.Api.Services.Validators;

/// <summary>
/// 安全加固阶段4: 修改密码请求验证器
/// 规则:
///   - OldPassword: 非空
///   - NewPassword: 非空, 长度 8-128, 至少 1 字母 + 1 数字
/// WHY 至少 1 字母 + 1 数字: 与 CreateUserRequestValidator 保持一致, 防纯数字/纯字母弱密码
/// </summary>
public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.OldPassword)
            .NotEmpty().WithMessage("旧密码不能为空");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("新密码不能为空")
            .MinimumLength(8).WithMessage("新密码长度不能少于 8 位")
            .MaximumLength(128).WithMessage("新密码长度不能超过 128 字符")
            .Matches("[a-zA-Z]").WithMessage("新密码必须包含至少 1 个字母")
            .Matches("[0-9]").WithMessage("新密码必须包含至少 1 个数字");
    }
}
