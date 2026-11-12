using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Error;
using Maple2.Model.Game.Pvp;
using Maple2.Server.World.Containers;

namespace Maple2.Server.Tests.World;

// The World's rank duel matchmaker with the channel calls replaced: the match field made (or not) and the
// notices sent to the characters.
public class PvpLookupTests {
    private readonly List<(long CharacterId, bool Matched)> notices = [];
    private readonly List<(int MatchId, long[] Characters)> made = [];
    private long tick;

    [SetUp]
    public void Reset() {
        notices.Clear();
        made.Clear();
        tick = 0;
    }

    private PvpLookup Lookup(MigrationError error = MigrationError.ok) {
        return new PvpLookup((matchId, characterIds) => {
            made.Add((matchId, characterIds.ToArray()));
            return (error, 1, 65010002, error == MigrationError.ok ? 900 + matchId : 0);
        }, (characterId, matched) => notices.Add((characterId, matched)), () => tick);
    }

    [Test]
    public void Pair_MakesTheFieldThenTellsBoth() {
        PvpLookup lookup = Lookup();
        Assert.That(lookup.Register(1), Is.EqualTo(RankDuelError.none));
        Assert.That(notices, Is.Empty);
        Assert.That(lookup.Register(2), Is.EqualTo(RankDuelError.none));
        lookup.LastStep.Wait();

        Assert.That(made.Single().Characters, Is.EqualTo(new long[] { 1, 2 }));
        Assert.That(notices, Is.EqualTo(new[] { (1L, true), (2L, true) }));
        Assert.That(lookup.Join(2, out int channel, out int mapId, out int roomId), Is.EqualTo(RankDuelError.none));
        Assert.That((channel, mapId, roomId), Is.EqualTo((1, 65010002, 900 + made.Single().MatchId)));
    }

    // No field: both are told the match failed, and both are free to register again.
    [Test]
    public void NoField_TellsBothAndFreesThem() {
        PvpLookup lookup = Lookup(MigrationError.s_move_err_FailCreateDungeon);
        lookup.Register(1);
        lookup.Register(2);
        lookup.LastStep.Wait();
        Assert.That(notices, Is.EqualTo(new[] { (1L, false), (2L, false) }));
        Assert.That(lookup.MatchOf(1), Is.Null);
        Assert.That(lookup.Register(1), Is.EqualTo(RankDuelError.none));
    }

    // The field's end frees both; one that never joined is told the match is over.
    [Test]
    public void End_TellsWhoNeverJoined() {
        PvpLookup lookup = Lookup();
        lookup.Register(1);
        lookup.Register(2);
        lookup.LastStep.Wait();
        notices.Clear();
        lookup.Join(1, out _, out _, out _);
        lookup.End(made.Single().MatchId);
        Assert.That(notices, Is.EqualTo(new[] { (2L, false) }));
        Assert.That(lookup.MatchOf(1), Is.Null);
        Assert.That(lookup.MatchOf(2), Is.Null);
    }

    [Test]
    public void NobodyJoined_Expires() {
        PvpLookup lookup = Lookup();
        lookup.Register(1);
        lookup.Register(2);
        lookup.LastStep.Wait();
        notices.Clear();
        tick = RankDuelQueue.NoShowMs;
        lookup.Expire();
        Assert.That(notices, Is.EqualTo(new[] { (1L, false), (2L, false) }));
        Assert.That(lookup.MatchOf(1), Is.Null);
    }

    [Test]
    public void Logout_LeavesTheQueue() {
        PvpLookup lookup = Lookup();
        lookup.Register(1);
        lookup.Remove(1);
        Assert.That(lookup.IsQueued(1), Is.False);
        lookup.Register(2);
        Assert.That(made, Is.Empty);
    }

    // The match channel went down: the match ends at once, and who never joined is told.
    [Test]
    public void ChannelDown_EndsTheMatch() {
        PvpLookup lookup = Lookup();
        lookup.Register(1);
        lookup.Register(2);
        lookup.LastStep.Wait();
        notices.Clear();
        lookup.Join(1, out _, out _, out _);
        lookup.EndChannel(1);
        Assert.That(notices, Is.EqualTo(new[] { (2L, false) }));
        Assert.That(lookup.MatchOf(1), Is.Null);
        Assert.That(lookup.Register(2), Is.EqualTo(RankDuelError.none));
    }
}
