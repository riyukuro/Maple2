using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Error;
using Maple2.Server.Channel.Service;
using Maple2.Server.World.Containers;

namespace Maple2.Server.Tests.World;

// The World's guild vs game matchmaker with the channel calls replaced: the match field made (or not) and the
// notices sent to the guilds.
public class GuildVsGameLookupTests {
    private readonly List<(long GuildId, GuildRequest Notice)> notices = [];
    private readonly List<(int MatchId, long Blue, long Red)> made = [];
    private MigrationError makeError;
    private bool open;
    private long tick;
    private GuildVsGameLookup lookup = null!;

    [SetUp]
    public void SetUp() {
        notices.Clear();
        made.Clear();
        makeError = MigrationError.ok;
        open = true;
        tick = 1000;
        lookup = new GuildVsGameLookup((matchId, blue, red) => {
            made.Add((matchId, blue, red));
            return (makeError, 2, 66200001, 30 + matchId);
        }, (guildId, notice) => notices.Add((guildId, notice)), () => open, () => tick);
    }

    [TearDown]
    public void TearDown() => lookup.Dispose();

    private List<string> Notices(long guildId) => notices.Where(n => n.GuildId == guildId).Select(n => n.Notice.GuildCase switch {
        GuildRequest.GuildOneofCase.VsGameTicket => $"ticket {n.Notice.VsGameTicket.Registered} {n.Notice.VsGameTicket.Notify}",
        GuildRequest.GuildOneofCase.VsGameOpen => $"open {n.Notice.VsGameOpen.GameId}",
        GuildRequest.GuildOneofCase.VsGameCanceled => "canceled",
        _ => n.Notice.GuildCase.ToString(),
    }).ToList();

    [Test]
    public void ClosedTimeTakesNoTicket() {
        open = false;
        Assert.That(lookup.Register(1), Is.EqualTo(GuildError.s_guild_err_isNotVsGameTime));
        Assert.That(lookup.IsWaiting(1), Is.False);
        Assert.That(notices, Is.Empty);
    }

    [Test]
    public void TwoTicketsMakeAMatch() {
        Assert.That(lookup.Register(1), Is.EqualTo(GuildError.none));
        Assert.That(lookup.IsWaiting(1), Is.True);
        // A second ask of the same guild: the ticket again, without a line.
        Assert.That(lookup.Register(1), Is.EqualTo(GuildError.none));
        Assert.That(lookup.Register(2), Is.EqualTo(GuildError.none));
        lookup.LastStep.Wait();

        Assert.That(made, Is.EqualTo(new[] { (1, 1L, 2L) }));
        Assert.That(Notices(1), Is.EqualTo(new[] { "ticket True True", "ticket True False", "open 1" }));
        Assert.That(Notices(2), Is.EqualTo(new[] { "ticket True True", "open 1" }));
        Assert.That(lookup.IsWaiting(1), Is.False);

        Assert.That(lookup.Join(2, out int channel, out int mapId, out int roomId), Is.EqualTo(GuildError.none));
        Assert.That((channel, mapId, roomId), Is.EqualTo((2, 66200001, 31)));
        Assert.That(lookup.Join(3, out _, out _, out _), Is.EqualTo(GuildError.s_guild_err_isNotVsGameTime));
        // A matched guild takes no new ticket.
        Assert.That(lookup.Register(1), Is.EqualTo(GuildError.s_guild_err_isNotVsGameTime));
    }

    // A World start's ids follow the id it is given (the clock's seconds in the server), so an old field's end
    // names no match of this start.
    [Test]
    public void IdsFollowTheFirstId() {
        using var seeded = new GuildVsGameLookup((matchId, blue, red) => {
            made.Add((matchId, blue, red));
            return (MigrationError.ok, 2, 66200001, 30);
        }, (guildId, notice) => notices.Add((guildId, notice)), () => open, () => tick, lastMatchId: 500000);
        seeded.Register(1);
        seeded.Register(2);
        seeded.LastStep.Wait();
        seeded.End(1);

        Assert.That(made, Is.EqualTo(new[] { (500001, 1L, 2L) }));
        Assert.That(seeded.MatchOf(1)?.Id, Is.EqualTo(500001));
    }

    [Test]
    public void FailedFieldCancelsTheMatch() {
        makeError = MigrationError.s_move_err_FailCreateDungeon;
        lookup.Register(1);
        lookup.Register(2);
        lookup.LastStep.Wait();
        Assert.That(Notices(1).Last(), Is.EqualTo("canceled"));
        Assert.That(Notices(2).Last(), Is.EqualTo("canceled"));
        Assert.That(lookup.MatchOf(1), Is.Null);
        Assert.That(lookup.Join(1, out _, out _, out _), Is.EqualTo(GuildError.s_guild_err_isNotVsGameTime));
    }

    [Test]
    public void UnregisterGivesTheTicketBack() {
        lookup.Register(1);
        Assert.That(lookup.Unregister(1), Is.EqualTo(GuildError.none));
        Assert.That(lookup.IsWaiting(1), Is.False);
        lookup.Unregister(1);
        Assert.That(Notices(1), Is.EqualTo(new[] { "ticket True True", "ticket False True", "ticket False False" }));
        // The next guild waits instead of being matched.
        lookup.Register(2);
        Assert.That(made, Is.Empty);
        Assert.That(lookup.IsWaiting(2), Is.True);
    }

    [Test]
    public void EndClosesOnce() {
        lookup.Register(1);
        lookup.Register(2);
        lookup.LastStep.Wait();
        lookup.End(1);
        lookup.End(1);
        Assert.That(Notices(1).Count(n => n == "open 0"), Is.EqualTo(1));
        Assert.That(Notices(2).Count(n => n == "open 0"), Is.EqualTo(1));
        Assert.That(lookup.MatchOf(1), Is.Null);
        // Both can take tickets again.
        Assert.That(lookup.Register(1), Is.EqualTo(GuildError.none));
    }

    [Test]
    public void TickCancelsTicketsAfterTheEvent() {
        lookup.Register(1);
        lookup.Tick();
        Assert.That(lookup.IsWaiting(1), Is.True);
        open = false;
        lookup.Tick();
        Assert.That(lookup.IsWaiting(1), Is.False);
        Assert.That(Notices(1).Last(), Is.EqualTo("canceled"));
        lookup.Tick();
        Assert.That(Notices(1).Count(n => n == "canceled"), Is.EqualTo(1));
    }

    [Test]
    public void MatchExpires() {
        lookup.Register(1);
        lookup.Register(2);
        lookup.LastStep.Wait();
        tick += (long) GuildVsGameLookup.MatchLifetime.TotalMilliseconds - 1;
        lookup.Tick();
        Assert.That(lookup.MatchOf(1), Is.Not.Null);
        tick += 1;
        lookup.Tick();
        Assert.That(lookup.MatchOf(1), Is.Null);
        Assert.That(Notices(2).Last(), Is.EqualTo("open 0"));
    }

    [Test]
    public void ChannelDownEndsItsMatches() {
        lookup.Register(1);
        lookup.Register(2);
        lookup.LastStep.Wait();
        lookup.EndChannel(1);
        Assert.That(lookup.MatchOf(1), Is.Not.Null);
        lookup.EndChannel(2);
        Assert.That(lookup.MatchOf(1), Is.Null);
        Assert.That(Notices(1).Last(), Is.EqualTo("open 0"));
    }
}
