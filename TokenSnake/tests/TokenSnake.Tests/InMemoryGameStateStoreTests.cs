using Microsoft.Extensions.Caching.Memory;
using TokenSnake.Game;
using Xunit;

namespace TokenSnake.Tests;

public class InMemoryGameStateStoreTests
{
    private static GameState Game(string id) => T.NewState(1) with { GameId = id };

    private static InMemoryGameStateStore NewStore(TimeSpan? idleExpiry = null) =>
        new(new MemoryCache(new MemoryCacheOptions()), idleExpiry);

    [Fact]
    public void Create_and_get()
    {
        var store = NewStore();
        var g = Game("a");
        store.Create(g);
        Assert.Same(g, store.Get("a"));
        Assert.Null(store.Get("nope"));
    }

    [Fact]
    public void Update_requires_matching_seq_and_unknown_game_fails()
    {
        var store = NewStore();
        var g = Game("a");
        store.Create(g);
        Assert.False(store.TryUpdate(g with { Seq = 6 }, expectedSeq: 5));
        Assert.True(store.TryUpdate(g with { Seq = 1 }, expectedSeq: 0));
        Assert.False(store.TryUpdate(g with { Seq = 1 }, expectedSeq: 0)); // stale
        Assert.False(store.TryUpdate(Game("nope"), 0));
    }

    [Fact]
    public void Over_games_are_final()
    {
        var store = NewStore();
        var g = Game("a");
        store.Create(g);
        Assert.True(store.TryUpdate(g with { Status = GameStatus.Over }, 0));
        Assert.False(store.TryUpdate(g with { Seq = 1 }, 0));
        Assert.Equal(GameStatus.Over, store.Get("a")!.Status);
    }

    [Fact]
    public void Parallel_updates_with_same_seq_have_exactly_one_winner()
    {
        for (var round = 0; round < 50; round++)
        {
            var store = NewStore();
            var g = Game("a");
            store.Create(g);
            var wins = 0;
            Parallel.For(0, 8, _ => { if (store.TryUpdate(g with { Seq = 1 }, 0)) Interlocked.Increment(ref wins); });
            Assert.Equal(1, wins);
        }
    }

    [Fact]
    public async Task Idle_games_expire_active_or_over()
    {
        var store = NewStore(TimeSpan.FromMilliseconds(200));
        var over = Game("over");
        store.Create(over);
        store.TryUpdate(over with { Status = GameStatus.Over }, 0);
        store.Create(Game("abandoned"));

        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.Null(store.Get("over"));
        Assert.Null(store.Get("abandoned"));
    }
}
