using TokenSnake.IdentityServer;
using Xunit;

namespace TokenSnake.Tests;

public class NicknameTests
{
    [Fact]
    public void Trims_and_collapses_whitespace()
    {
        Assert.True(Nickname.TryCreate("  Ada   Lovelace ", out var n, out var err));
        Assert.Equal("Ada Lovelace", n);
        Assert.Null(err);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("A", false)]
    [InlineData("Ab", true)]
    [InlineData("12345678901234567890", true)]   // 20
    [InlineData("123456789012345678901", false)] // 21
    public void Length_boundaries(string? input, bool ok) =>
        Assert.Equal(ok, Nickname.TryCreate(input, out _, out _));

    [Theory]
    [InlineData("Ada:1")]
    [InlineData("<b>Ada")]
    [InlineData("Ada>")]
    [InlineData("Ad\"a")]
    [InlineData("Ada😀")]
    public void Bad_characters_are_rejected(string input)
    {
        Assert.False(Nickname.TryCreate(input, out _, out var err));
        Assert.NotNull(err);
    }

    [Theory]
    [InlineData("Ada_L-1.x")]
    [InlineData("a b")]
    public void Allowed_characters_are_accepted(string input) =>
        Assert.True(Nickname.TryCreate(input, out _, out _));

    [Theory]
    [InlineData("admin")]
    [InlineData("SuperADMIN99")]
    public void Blocklisted_words_are_rejected_case_insensitively(string input)
    {
        Assert.False(Nickname.TryCreate(input, out _, out var err, ["admin", " "]));
        Assert.NotNull(err);
        Assert.True(Nickname.TryCreate("Grace", out _, out _, ["admin", " "]));
    }

    [Fact]
    public void FromSubject_splits_on_last_colon()
    {
        Assert.Equal("Ada L", Nickname.FromSubject("Ada L:0123456789abcdef0123456789abcdef"));
        Assert.Equal("plain", Nickname.FromSubject("plain"));
    }
}
