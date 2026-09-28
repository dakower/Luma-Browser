namespace Luma.Core;
public static class AuthPolicy
{
    public static bool IsDisplayNameValid(string? value) { var length = (value ?? "").Trim().Length; return length is >= 2 and <= 40; }
    public static bool IsStrongPassword(string? value) => !string.IsNullOrEmpty(value) && value.Length is >= 6 and <= 128 && value.Any(char.IsLetter) && value.Any(char.IsDigit);
}
