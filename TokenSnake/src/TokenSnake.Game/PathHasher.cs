using System.Security.Cryptography;
using System.Text;

namespace TokenSnake.Game;

/// <summary>Chains move strings so each token proves the whole path that led to it.</summary>
public static class PathHasher
{
    public const string Genesis = "";

    /// <summary>SHA-256(previousHash + moves), hex encoded.</summary>
    public static string Next(string previousHash, string moves) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(previousHash + moves)));
}
