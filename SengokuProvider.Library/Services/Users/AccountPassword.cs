using System.Security.Cryptography;

namespace SengokuProvider.Library.Services.Users;

public static class AccountPassword
{
    private const int Iterations = 600_000;
    private const string Prefix = "sengoku-pbkdf2-sha256-v1";
    public static string Hash(string password)
    {
        Validate(password);
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"{Prefix}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
    public static bool Verify(string password, string stored)
    {
        if (string.IsNullOrEmpty(password) || password.Length > 1024 || string.IsNullOrEmpty(stored)) return false;
        var parts = stored.Split('$');
        // Do not authenticate legacy plaintext passwords. Existing accounts need a reset.
        if (parts.Length != 3 || parts[0] != Prefix) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            if (salt.Length != 16 || expected.Length != 32) return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
    public static void Validate(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12 || password.Length > 1024)
            throw new ArgumentException("Use a password between 12 and 1024 characters.");
    }
}
