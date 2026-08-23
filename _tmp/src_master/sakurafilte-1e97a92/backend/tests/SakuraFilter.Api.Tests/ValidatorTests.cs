using FluentAssertions;
using FluentValidation.Results;
using SakuraFilter.Api.Controllers;
using SakuraFilter.Api.Services.Validators;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// FluentValidation 验证器单测
/// WHY 改用纯 API 而非 TestHelper: FluentValidation.TestHelper 在 11.x 已被拆分到独立包,
///   依赖管理成本高. 验证 IsValid + 检查 Errors 列表已足够覆盖所有断言场景.
/// 覆盖: LoginRequest, ChangePasswordRequest, CreateUserRequest, UpdateUserRequest
/// </summary>
public class ValidatorTests
{
    // ===== LoginRequestValidator =====

    public class LoginRequestValidatorTests
    {
        private readonly LoginRequestValidator _sut = new();

        [Fact]
        public void Validate_ValidRequest_Passes()
        {
            var req = new LoginRequest { Username = "admin", Password = "pass" };
            var result = _sut.Validate(req);
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void Validate_EmptyUsername_Fails()
        {
            var req = new LoginRequest { Username = "", Password = "pass" };
            var result = _sut.Validate(req);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(LoginRequest.Username));
        }

        [Fact]
        public void Validate_EmptyPassword_Fails()
        {
            var req = new LoginRequest { Username = "admin", Password = "" };
            var result = _sut.Validate(req);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(LoginRequest.Password));
        }

        [Fact]
        public void Validate_TooLongUsername_Fails()
        {
            // WHY: 64 字符上限, 防止缓冲区滥用 + 日志注入
            var req = new LoginRequest { Username = new string('a', 65), Password = "pass" };
            var result = _sut.Validate(req);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(LoginRequest.Username));
        }

        [Fact]
        public void Validate_TooLongPassword_Fails()
        {
            // WHY: 128 字符上限, 防止 bcrypt 之前被 DOS (大密码导致 CPU 100%)
            var req = new LoginRequest { Username = "admin", Password = new string('p', 129) };
            var result = _sut.Validate(req);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(LoginRequest.Password));
        }

        [Theory]
        [InlineData("admin@sakura.dev")]  // email-like username
        [InlineData("user_123")]
        [InlineData("中文用户")]
        [InlineData("user with space")]
        public void Validate_VariousUsernameFormats_Allowed(string username)
        {
            // WHY 登录接口允许任何非空字符串作为 username (输入是 username 或 email 视实现而定)
            //   字符集限制在 CreateUser 那边强制, 登录不做
            var req = new LoginRequest { Username = username, Password = "pass" };
            _sut.Validate(req).IsValid.Should().BeTrue();
        }
    }

    // ===== ChangePasswordRequestValidator =====

    public class ChangePasswordRequestValidatorTests
    {
        private readonly ChangePasswordRequestValidator _sut = new();

        [Fact]
        public void Validate_StrongPassword_Passes()
        {
            var req = new ChangePasswordRequest { OldPassword = "oldPass1", NewPassword = "newPass123" };
            _sut.Validate(req).IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData("")]           // 空
        [InlineData("short")]      // < 8
        [InlineData("nodigit")]    // 无数字
        [InlineData("12345678")]   // 无字母
        public void Validate_WeakNewPassword_Fails(string newPassword)
        {
            var req = new ChangePasswordRequest { OldPassword = "old", NewPassword = newPassword };
            var result = _sut.Validate(req);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(ChangePasswordRequest.NewPassword));
        }

        [Fact]
        public void Validate_EmptyOldPassword_Fails()
        {
            var req = new ChangePasswordRequest { OldPassword = "", NewPassword = "newPass1" };
            var result = _sut.Validate(req);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(ChangePasswordRequest.OldPassword));
        }
    }

    // ===== UserCreateRequestValidator =====

    public class UserCreateRequestValidatorTests
    {
        private readonly UserCreateRequestValidator _sut = new();

        [Fact]
        public void Validate_ValidRequest_Passes()
        {
            var req = new CreateUserRequest
            {
                Username = "newadmin",
                Password = "pass1234",
                Role = "admin",
                Email = "admin@sakura.dev",
                FullName = "管理员"
            };
            _sut.Validate(req).IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData("ab")]                              // 短 (< 3)
        [InlineData("user with space")]                 // 空格
        [InlineData("user@invalid")]                     // @
        [InlineData("user.dot")]                         // .
        [InlineData("user/slash")]                       // /
        [InlineData("用户名中文")]                         // 非 ASCII
        public void Validate_BadUsername_Fails(string username)
        {
            // WHY 字符集限制: 防止用户名作为路径/URL/日志时因特殊字符出错
            var req = new CreateUserRequest { Username = username, Password = "pass1234", Role = "admin" };
            var result = _sut.Validate(req);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateUserRequest.Username));
        }

        [Theory]
        [InlineData("user_123")]
        [InlineData("user-abc")]
        [InlineData("UserName")]
        [InlineData("ABC123")]
        [InlineData("a_b-c")]
        public void Validate_AllowedUsernameFormats_Pass(string username)
        {
            var req = new CreateUserRequest { Username = username, Password = "pass1234", Role = "admin" };
            _sut.Validate(req).IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData("admin")]
        [InlineData("operator")]
        [InlineData("viewer")]
        public void Validate_ValidRole_Passes(string role)
        {
            var req = new CreateUserRequest { Username = "user1", Password = "pass1234", Role = role };
            _sut.Validate(req).IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData("root")]      // 不在白名单
        [InlineData("superadmin")]
        [InlineData("Admin")]     // 大小写敏感
        [InlineData("ADMIN")]
        public void Validate_InvalidRole_Fails(string role)
        {
            // WHY 白名单: 防止 role 注入提权
            var req = new CreateUserRequest { Username = "user1", Password = "pass1234", Role = role };
            var result = _sut.Validate(req);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateUserRequest.Role));
        }

        [Theory]
        [InlineData("notanemail")]
        [InlineData("missing@")]
        [InlineData("@nodomain.com")]
        public void Validate_BadEmail_Fails(string email)
        {
            var req = new CreateUserRequest
            {
                Username = "user1", Password = "pass1234", Role = "admin", Email = email
            };
            var result = _sut.Validate(req);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateUserRequest.Email));
        }

        [Fact]
        public void Validate_NullEmail_Passes()
        {
            // WHY: Email 允许空 (不是必填)
            var req = new CreateUserRequest
            {
                Username = "user1", Password = "pass1234", Role = "admin", Email = null
            };
            _sut.Validate(req).IsValid.Should().BeTrue();
        }

        [Fact]
        public void Validate_TooLongFullName_Fails()
        {
            var req = new CreateUserRequest
            {
                Username = "user1", Password = "pass1234", Role = "admin",
                FullName = new string('x', 65)
            };
            var result = _sut.Validate(req);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateUserRequest.FullName));
        }
    }

    // ===== UserUpdateRequestValidator =====

    public class UserUpdateRequestValidatorTests
    {
        private readonly UserUpdateRequestValidator _sut = new();

        [Fact]
        public void Validate_AllNulls_Passes()
        {
            // WHY: PATCH 风格, 字段不更新时传 null/不传
            var req = new UpdateUserRequest();
            _sut.Validate(req).IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData("admin")]
        [InlineData("operator")]
        [InlineData("viewer")]
        public void Validate_ValidRole_Passes(string role)
        {
            var req = new UpdateUserRequest { Role = role };
            _sut.Validate(req).IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData("root")]
        [InlineData("SUPERADMIN")]
        public void Validate_InvalidRole_Fails(string role)
        {
            var req = new UpdateUserRequest { Role = role };
            var result = _sut.Validate(req);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateUserRequest.Role));
        }

        [Fact]
        public void Validate_BadEmail_Fails()
        {
            var req = new UpdateUserRequest { Email = "notanemail" };
            var result = _sut.Validate(req);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateUserRequest.Email));
        }

        [Fact]
        public void Validate_IsActiveBool_Passes()
        {
            // WHY: IsActive 是 bool? 类型, 任何 true/false/null 都通过
            _sut.Validate(new UpdateUserRequest { IsActive = true }).IsValid.Should().BeTrue();
            _sut.Validate(new UpdateUserRequest { IsActive = false }).IsValid.Should().BeTrue();
            _sut.Validate(new UpdateUserRequest { IsActive = null }).IsValid.Should().BeTrue();
        }
    }
}
