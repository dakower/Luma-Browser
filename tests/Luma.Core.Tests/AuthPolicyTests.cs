using Luma.Core; using Xunit;
namespace Luma.Core.Tests;
public sealed class AuthPolicyTests
{
    [Theory]
    [InlineData("abc12", false)]
    [InlineData("abcdef", false)]
    [InlineData("123456", false)]
    [InlineData("abc123", true)]
    [InlineData("ABC123", true)]
    [InlineData("Пароль1", true)]
    public void PasswordRules_RequireSixCharactersLettersAndDigits(string value, bool expected) => Assert.Equal(expected, AuthPolicy.IsStrongPassword(value));
    [Theory] [InlineData("A", false)] [InlineData("Luma User", true)] public void DisplayNameRules_AreEnforced(string value, bool expected) => Assert.Equal(expected, AuthPolicy.IsDisplayNameValid(value));
}
