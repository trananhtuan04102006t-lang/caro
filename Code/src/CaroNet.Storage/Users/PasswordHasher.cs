using System.Security.Cryptography;
using System.Text;

namespace CaroNet.Storage.Users;

internal static class PasswordHasher
{
    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = ComputeHash(salt, password);

        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string storedHash)
    {
        string[] parts = storedHash.Split(':', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        try
        {
            byte[] salt = Convert.FromBase64String(parts[0]);
            byte[] expectedHash = Convert.FromBase64String(parts[1]);
            byte[] actualHash = ComputeHash(salt, password);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] ComputeHash(byte[] salt, string password)
    {
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[] input = new byte[salt.Length + passwordBytes.Length];

        Buffer.BlockCopy(salt, 0, input, 0, salt.Length);
        Buffer.BlockCopy(passwordBytes, 0, input, salt.Length, passwordBytes.Length);

        return SHA256.HashData(input);
    }
}
