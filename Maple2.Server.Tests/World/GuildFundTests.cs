using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Containers;

namespace Maple2.Server.Tests.World;

// The guild fund on the World server: every change is refused or stored whole, and changes that arrive together
// leave a fund that adds up.
public class GuildFundTests {
    private const int Rounds = 40;
    private const long Stranger = 999;

    private TestGameSession test = null!;
    private Guild guild = null!;
    private GuildManager manager = null!;
    private GuildMember master = null!;
    private GuildMember member = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        var property = (GuildTable.Property) RuntimeHelpers.GetUninitializedObject(typeof(GuildTable.Property));
        property = property with { Level = 1, Capacity = 10, FundMax = 1000, DonateMax = 3, CheckInFund = 30, CheckInExp = 5 };
        test.SetTable(new GuildTable(
            new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>>(),
            new Dictionary<int, IReadOnlyDictionary<int, GuildTable.House>>(),
            new Dictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>>(),
            new Dictionary<short, GuildTable.Property> { [1] = property }));

        using (GameStorage.Request db = test.Storage.Context()) {
            guild = db.CreateGuild("Fund", test.CharacterId) ?? throw new InvalidOperationException("guild row not created");
        }
        master = Member(test.CharacterId, "Master", 0);
        member = Member(test.CharacterId + 500, "Member", 2);
        guild.Members[master.CharacterId] = master;
        guild.Members[member.CharacterId] = member;
        guild.Funds = 100;
        manager = new GuildManager(guild) {
            GameStorage = test.Storage,
            ChannelClients = new ChannelClientLookup(),
            TableMetadata = test.TableMetadata,
            ItemMetadata = test.ItemMetadata,
            ServerTableMetadata = test.ServerTableMetadata,
        };
    }

    [TearDown]
    public void TearDown() {
        manager.Dispose();
        master.Dispose();
        member.Dispose();
    }

    private GuildMember Member(long characterId, string name, byte rank) {
        var info = new PlayerInfo(new CharacterInfo(characterId + 100, characterId, name, "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []);
        return new GuildMember { GuildId = guild.Id, Info = info, Rank = rank, JoinTime = 1000 };
    }

    // The guild table's contribution rows (table/guildcontribution.xml) the test gives.
    private void SetContributions(Dictionary<string, int> contributions) {
        test.SetTable(test.TableMetadata.GuildTable with { Contributions = contributions });
    }

    // The master's stored contribution (the master's row is made with the guild).
    private (int Weekly, int Total) StoredContribution() {
        using var context = new Maple2.Database.Context.Ms2Context(test.Options);
        var row = context.GuildMember.Single(row => row.CharacterId == master.CharacterId);
        return (row.WeeklyContribution, row.TotalContribution);
    }

    private int StoredFunds() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetGuild(guild.Id)!.Funds;
    }

    // A donation refused: not a member, no count, more than the day allows, a fund over the level's ceiling, a
    // negative fund, a member who joined today. The fund and the member's count stay as they were.
    [TestCase("stranger", 1, 10)]
    [TestCase("member", 0, 0)]
    [TestCase("member", -1, 10)]
    [TestCase("member", 4, 40)]
    [TestCase("member", 1, 901)]
    [TestCase("member", 1, -10)]
    [TestCase("today", 1, 10)]
    public void Donate_Refused(string who, int count, long fund) {
        if (who == "today") {
            member.JoinTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        GuildError error = manager.Donate(who == "stranger" ? Stranger : member.CharacterId, count, fund, out _);

        Assert.That(error, Is.Not.EqualTo(GuildError.none));
        Assert.That(guild.Funds, Is.EqualTo(100));
        Assert.That(member.DailyDonationCount, Is.EqualTo(0));
        Assert.That(member.DonationTime, Is.EqualTo(0));
    }

    // A donation is stored before it is answered; up to the day's count and no further.
    [Test]
    public void Donate_StoredUpToTheDaysCount() {
        Assert.That(manager.Donate(member.CharacterId, 2, 20, out _), Is.EqualTo(GuildError.none));
        Assert.That((guild.Funds, member.DailyDonationCount, StoredFunds()), Is.EqualTo((120, 2, 120)));

        Assert.That(manager.Donate(member.CharacterId, 2, 20, out _), Is.Not.EqualTo(GuildError.none));
        Assert.That(manager.Donate(member.CharacterId, 1, 10, out _), Is.EqualTo(GuildError.none));
        Assert.That((guild.Funds, member.DailyDonationCount, StoredFunds()), Is.EqualTo((130, 3, 130)));
    }

    // The save fails (the guild's row is gone): the donation is refused and the fund and the count are put back,
    // so the channel returns the meso for a fund that was not added.
    [Test]
    public void Donate_FailedSaveChangesNothing() {
        using (var context = new Maple2.Database.Context.Ms2Context(test.Options)) {
            context.Guild.Remove(context.Guild.Find(guild.Id)!);
            context.SaveChanges();
        }

        Assert.That(manager.Donate(member.CharacterId, 1, 10, out _), Is.Not.EqualTo(GuildError.none));
        Assert.That(manager.SpendFund(master.CharacterId, 10), Is.Not.EqualTo(GuildError.none));

        Assert.That((guild.Funds, member.DailyDonationCount, member.DonationTime), Is.EqualTo((100, 0, 0L)));
    }

    // Each donation is worth the table's donation row to the member (2 here, so a donation of 3 gives 6), stored at
    // once; a refused donation gives nothing.
    [Test]
    public void Donate_GivesTheTablesContributionPerDonation() {
        SetContributions(new Dictionary<string, int> { [GuildContributionKind.Donation] = 2 });

        Assert.That(manager.Donate(master.CharacterId, 3, 30, out _), Is.EqualTo(GuildError.none));
        Assert.That((master.WeeklyContribution, master.TotalContribution), Is.EqualTo((6, 6)));
        Assert.That(StoredContribution(), Is.EqualTo((6, 6)));

        Assert.That(manager.Donate(master.CharacterId, 1, 10, out _), Is.Not.EqualTo(GuildError.none));
        Assert.That((master.WeeklyContribution, master.TotalContribution), Is.EqualTo((6, 6)));
    }

    // Two donations of one member at once, each of two of the day's three: one is taken.
    [Test]
    public void Donate_TwoAtOnceStayInsideTheDaysCount() {
        for (int round = 0; round < Rounds; round++) {
            guild.Funds = 100;
            member.DailyDonationCount = 0;
            member.DonationTime = 0;
            var errors = new GuildError[2];
            Parallel.Invoke(
                () => errors[0] = manager.Donate(member.CharacterId, 2, 20, out _),
                () => errors[1] = manager.Donate(member.CharacterId, 2, 20, out _));

            Assert.That(errors, Has.Exactly(1).EqualTo(GuildError.none), $"round {round}");
            Assert.That((guild.Funds, member.DailyDonationCount), Is.EqualTo((120, 2)), $"round {round}");
            Assert.That(StoredFunds(), Is.EqualTo(120), $"round {round}");
        }
    }

    // Two members donate at once to a fund with room for one of the two: the fund never passes its ceiling.
    [Test]
    public void Donate_TwoMembersAtOnceStayUnderTheCeiling() {
        for (int round = 0; round < Rounds; round++) {
            guild.Funds = 900;
            foreach (GuildMember each in new[] { master, member }) {
                each.DailyDonationCount = 0;
                each.DonationTime = 0;
            }
            var errors = new GuildError[2];
            Parallel.Invoke(
                () => errors[0] = manager.Donate(master.CharacterId, 1, 60, out _),
                () => errors[1] = manager.Donate(member.CharacterId, 1, 60, out _));

            Assert.That(errors, Has.Exactly(1).EqualTo(GuildError.none), $"round {round}");
            Assert.That(guild.Funds, Is.EqualTo(960), $"round {round}");
            Assert.That(master.DailyDonationCount + member.DailyDonationCount, Is.EqualTo(1), $"round {round}");
        }
    }

    // A buy from the fund refused: not a member, not the master, more than the fund holds. Nothing is taken.
    [TestCase("stranger", 10)]
    [TestCase("member", 10)]
    [TestCase("master", 101)]
    public void SpendFund_Refused(string who, int amount) {
        long id = who switch { "stranger" => Stranger, "member" => member.CharacterId, _ => master.CharacterId };

        Assert.That(manager.SpendFund(id, amount), Is.Not.EqualTo(GuildError.none));
        Assert.That(guild.Funds, Is.EqualTo(100));
    }

    // Two buys from the fund at once, together more than it holds: one is paid, the fund is never negative.
    [Test]
    public void SpendFund_TwoAtOnceNeverOverdraw() {
        for (int round = 0; round < Rounds; round++) {
            guild.Funds = 100;
            var errors = new GuildError[2];
            Parallel.Invoke(
                () => errors[0] = manager.SpendFund(master.CharacterId, 60),
                () => errors[1] = manager.SpendFund(master.CharacterId, 60));

            Assert.That(errors, Has.Exactly(1).EqualTo(GuildError.none), $"round {round}");
            Assert.That((guild.Funds, StoredFunds()), Is.EqualTo((40, 40)), $"round {round}");
        }
    }

    // A donation and a buy at once: the buy is paid only when the donation came first, and the fund is the sum
    // of what happened.
    [Test]
    public void Donate_RacingASpend() {
        for (int round = 0; round < Rounds; round++) {
            guild.Funds = 100;
            member.DailyDonationCount = 0;
            member.DonationTime = 0;
            GuildError donated = GuildError.s_guild_err_unknown;
            GuildError spent = GuildError.s_guild_err_unknown;
            Parallel.Invoke(
                () => donated = manager.Donate(member.CharacterId, 1, 50, out _),
                () => spent = manager.SpendFund(master.CharacterId, 120));

            Assert.That(donated, Is.EqualTo(GuildError.none), $"round {round}");
            Assert.That(guild.Funds, Is.EqualTo(spent == GuildError.none ? 30 : 150), $"round {round}");
            Assert.That(StoredFunds(), Is.EqualTo(guild.Funds), $"round {round}");
        }
    }

    // Two check-ins of one member at once pay the guild once and the member the table's attend row (7 here) once; the
    // fund stops at the level's ceiling.
    [Test]
    public void CheckIn_TwoAtOncePayOnceAndStopAtTheCeiling() {
        SetContributions(new Dictionary<string, int> { [GuildContributionKind.Attend] = 7 });
        for (int round = 0; round < Rounds; round++) {
            guild.Funds = 100;
            guild.Experience = 0;
            member.CheckinTime = 0;
            member.TotalContribution = 0;
            var errors = new GuildError[2];
            Parallel.Invoke(
                () => errors[0] = manager.CheckIn(member.CharacterId),
                () => errors[1] = manager.CheckIn(member.CharacterId));

            Assert.That(errors, Has.Exactly(1).EqualTo(GuildError.none), $"round {round}");
            Assert.That((guild.Funds, guild.Experience, member.TotalContribution), Is.EqualTo((130, 5, 7)), $"round {round}");
        }

        guild.Funds = 990;
        Assert.That(manager.CheckIn(master.CharacterId), Is.EqualTo(GuildError.none));
        Assert.That(guild.Funds, Is.EqualTo(1000));
        Assert.That(manager.CheckIn(Stranger), Is.Not.EqualTo(GuildError.none));
        Assert.That(guild.Funds, Is.EqualTo(1000));
    }

    // A check-in that reaches the next level is in the guild's row at once: the guild search reads the row, and
    // showed the old level and member cap until the World server stopped.
    [Test]
    public void CheckIn_IsStored_AndTheSearchSeesIt() {
        guild.Experience = 69990;
        guild.Listed = true;
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveGuild(guild), Is.True);
        }
        guild.Experience = 69995;

        Assert.That(manager.CheckIn(member.CharacterId), Is.EqualTo(GuildError.none));

        using GameStorage.Request read = test.Storage.Context();
        Guild stored = read.GetGuild(guild.Id)!;
        Assert.That((stored.Experience, stored.Funds), Is.EqualTo((70000, 130)));
        Assert.That(read.SearchGuilds(0, "Fund", 10).Single().Experience, Is.EqualTo(70000));
    }

    [Test]
    public void AddExpFund_IsStored() {
        Assert.That(manager.AddExpFund(member.CharacterId, 40, 20), Is.EqualTo(GuildError.none));

        using GameStorage.Request db = test.Storage.Context();
        Guild stored = db.GetGuild(guild.Id)!;
        Assert.That((stored.Experience, stored.Funds), Is.EqualTo((40, 120)));
    }

    // The amounts the guild got, as the Game notices them (the guild vs game's Guild 123): the fund stops at the
    // level's fund max (1000 from 100: 900 of 950), the exp is whole; a stranger gets nothing added.
    [Test]
    public void AddExpFund_GivesWhatWasAdded() {
        Assert.That(manager.AddExpFund(member.CharacterId, 7, 950, out int addedExp, out int addedFund), Is.EqualTo(GuildError.none));
        Assert.That((addedExp, addedFund), Is.EqualTo((7, 900)));
        Assert.That(StoredFunds(), Is.EqualTo(1000));

        Assert.That(manager.AddExpFund(member.CharacterId, 0, 10, out addedExp, out addedFund), Is.EqualTo(GuildError.none));
        Assert.That((addedExp, addedFund), Is.EqualTo((0, 0)));

        Assert.That(manager.AddExpFund(Stranger, 7, 10, out addedExp, out addedFund), Is.EqualTo(GuildError.s_guild_err_null_member));
        Assert.That((addedExp, addedFund), Is.EqualTo((0, 0)));
    }

    // The guild vs game's contribution: added to the member's week and total; refused for a stranger or no amount.
    [Test]
    public void AddContribution_AddsToTheWeekAndTotal() {
        Assert.That(manager.AddContribution(member.CharacterId, 12), Is.EqualTo(GuildError.none));
        Assert.That((member.WeeklyContribution, member.TotalContribution), Is.EqualTo((12, 12)));

        Assert.That(manager.AddContribution(Stranger, 12), Is.EqualTo(GuildError.s_guild_err_null_member));
        Assert.That(manager.AddContribution(member.CharacterId, 0), Is.Not.EqualTo(GuildError.none));
        Assert.That((member.WeeklyContribution, member.TotalContribution), Is.EqualTo((12, 12)));
    }

    // A championship match that pays is worth the table's winPvp or losePvp row (made-up 11 and 4) to each of this
    // guild's payable fighters, once each; a fighter of the other guild gets nothing here.
    [TestCase(true, 11)]
    [TestCase(false, 4)]
    public void EndPvp_PayableFightersGetTheirContribution(bool won, int expected) {
        SetContributions(new Dictionary<string, int> { [GuildContributionKind.WinPvp] = 11, [GuildContributionKind.LosePvp] = 4 });
        long[] fighters = [master.CharacterId, member.CharacterId, Stranger];

        (_, _, bool reward) = manager.EndPvp(new GuildPvpMatch(), won, fought: true, point: 10, fighters,
            [master.CharacterId, master.CharacterId, Stranger], DateTime.Now);

        Assert.That(reward, Is.True);
        Assert.That((master.WeeklyContribution, master.TotalContribution), Is.EqualTo((expected, expected)));
        Assert.That(StoredContribution(), Is.EqualTo((expected, expected)));
        Assert.That(member.TotalContribution, Is.EqualTo(0)); // fought, but not payable
    }

    // A match that does not pay (no fought round) gives no contribution.
    [Test]
    public void EndPvp_WithoutTheRewardGivesNoContribution() {
        SetContributions(new Dictionary<string, int> { [GuildContributionKind.WinPvp] = 11 });

        (_, _, bool reward) = manager.EndPvp(new GuildPvpMatch(), won: true, fought: false, point: 10, [master.CharacterId],
            [master.CharacterId], DateTime.Now);

        Assert.That(reward, Is.False);
        Assert.That(master.TotalContribution, Is.EqualTo(0));
    }

    // Guild 90 under a running GuildSkillSale event: the fund pays the cost less the highest rate
    // (GetSkillCostWithEventDiscount), checked and taken alike; an ended event is left out.
    [TestCase(0, false, 100, GuildError.none, 20)]
    [TestCase(2500, false, 100, GuildError.none, 40)]
    [TestCase(2500, false, 70, GuildError.none, 10)]
    [TestCase(0, false, 70, GuildError.s_guild_err_not_enough_guild_fund, 70)]
    [TestCase(2500, true, 70, GuildError.s_guild_err_not_enough_guild_fund, 70)]
    public void UseSkill_SaleLowersTheCost(int rate, bool over, int funds, GuildError error, int fundsLeft) {
        test.SetTable(test.TableMetadata.GuildTable with {
            Buffs = new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>> {
                [1] = new Dictionary<short, GuildTable.Buff> { [1] = new(Id: 90000001, Level: 1, RequireLevel: 1, Cost: 80, UpgradeCost: 0, Duration: 60) },
            },
        });
        var events = new Dictionary<int, GameEventMetadata>();
        if (rate > 0) {
            DateTime start = over ? DateTime.Now.AddDays(-9) : DateTime.Now.AddDays(-1);
            DateTime end = over ? DateTime.Now.AddDays(-2) : DateTime.Now.AddDays(1);
            events[7400] = new GameEventMetadata(7400, GameEventType.GuildSkillSale, start, end, TimeSpan.Zero, TimeSpan.Zero, [],
                new EventRate(rate), "", "", "", "");
            events[7401] = new GameEventMetadata(7401, GameEventType.GuildSkillSale, DateTime.Now.AddDays(-1), DateTime.Now.AddDays(1),
                TimeSpan.Zero, TimeSpan.Zero, [], new EventRate(rate / 5), "", "", "", "");
        }
        test.SetServerTable(new GameEventTable(events));
        guild.Buffs.Add(new GuildBuff(1, 1, 0));
        guild.Funds = funds;
        using (GameStorage.Request db = test.Storage.Context()) {
            db.SaveGuild(guild);
        }

        Assert.Multiple(() => {
            Assert.That(manager.UseBuff(master.CharacterId, 1), Is.EqualTo(error));
            Assert.That(guild.Funds, Is.EqualTo(fundsLeft));
            Assert.That(StoredFunds(), Is.EqualTo(fundsLeft));
        });
    }

    // A running GuildExp event (KR 20180: 10000, twice the exp) adds its share to the check-in's exp and to an exp
    // gain (quest, trigger, guild vs game), the highest rate when two run; an ended one adds nothing.
    private void SetGuildExpEvents(params (int Rate, bool Over)[] rates) {
        var events = new Dictionary<int, GameEventMetadata>();
        foreach ((int rate, bool over) in rates) {
            int id = 20180 + events.Count;
            // Each later row starts a minute later.
            events[id] = new GameEventMetadata(id, GameEventType.GuildExp, DateTime.Now.AddDays(over ? -9 : -1).AddMinutes(events.Count),
                DateTime.Now.AddDays(over ? -2 : 1), TimeSpan.Zero, TimeSpan.Zero, [], new EventRate(rate), "", "", "", "");
        }
        test.SetServerTable(new GameEventTable(events));
    }

    [TestCase(0, false, 5)]
    [TestCase(10000, false, 10)]
    [TestCase(10000, true, 5)]
    [TestCase(5000, false, 7)]
    public void CheckIn_GuildExpEventAddsItsShare(int rate, bool over, int exp) {
        if (rate > 0) {
            SetGuildExpEvents((rate, over));
        }

        Assert.That(manager.CheckIn(member.CharacterId), Is.EqualTo(GuildError.none));

        using GameStorage.Request db = test.Storage.Context();
        Assert.That((guild.Experience, db.GetGuild(guild.Id)!.Experience), Is.EqualTo((exp, exp)));
    }

    // The rate is the one of the event that started last, as the client's single rate (the guild dialog shows it).
    [Test]
    public void AddExpFund_GuildExpEvent_TheLastStartedRateCounts() {
        SetGuildExpEvents((10000, false), (2500, false));

        Assert.That(manager.AddExpFund(member.CharacterId, 40, 0, out int addedExp, out _), Is.EqualTo(GuildError.none));

        Assert.That((addedExp, guild.Experience), Is.EqualTo((50, 50)));
    }

    [Test]
    public void AddExpFund_GuildExpEvent_LastStartedRunningRateAndTheAddedExp() {
        SetGuildExpEvents((2500, false), (10000, false), (30000, true));

        Assert.That(manager.AddExpFund(member.CharacterId, 40, 0, out int addedExp, out int addedFund), Is.EqualTo(GuildError.none));

        using GameStorage.Request db = test.Storage.Context();
        Assert.That((addedExp, addedFund, guild.Experience, db.GetGuild(guild.Id)!.Experience), Is.EqualTo((80, 0, 80, 80)));
    }

    // The guild exp stops at int.MaxValue; the added exp is what was really added.
    [Test]
    public void AddExpFund_GuildExpEvent_StopsAtTheIntCeiling() {
        SetGuildExpEvents((10000, false));
        guild.Experience = int.MaxValue - 50;

        Assert.That(manager.AddExpFund(member.CharacterId, 40, 0, out int addedExp, out _), Is.EqualTo(GuildError.none));

        Assert.That((addedExp, guild.Experience), Is.EqualTo((50, int.MaxValue)));
    }
}
