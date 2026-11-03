using System.Buffers.Binary;
using System.Security.Cryptography;

namespace TokenSnake.Game;

/// <summary>
/// Derives pellet positions from a secret seed. Same seed + index + body always gives
/// the same pellet, but without the seed nobody can predict the next one.
/// </summary>
public static class PelletGenerator
{
    public static Cell Get(byte[] seed, int index, GameRules rules, IReadOnlyList<Cell> body)
    {
        var occupied = body.ToHashSet();
        if (occupied.Count >= rules.Cells)
            throw new InvalidOperationException("The board is full; there is no free cell for a pellet.");

        // Rehash with a counter until we land on a free cell.
        for (var counter = 0; ; counter++)
        {
            var cell = Candidate(seed, index, counter, rules);
            if (!occupied.Contains(cell)) return cell;
        }
    }

    private static Cell Candidate(byte[] seed, int index, int counter, GameRules rules)
    {
        Span<byte> message = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(message, index);
        BinaryPrimitives.WriteInt32BigEndian(message[4..], counter);

        Span<byte> mac = stackalloc byte[32];
        HMACSHA256.HashData(seed, message, mac);

        var x = BinaryPrimitives.ReadUInt32BigEndian(mac) % (uint)rules.Width;
        var y = BinaryPrimitives.ReadUInt32BigEndian(mac[4..]) % (uint)rules.Height;
        return new Cell((int)x, (int)y);
    }
}
