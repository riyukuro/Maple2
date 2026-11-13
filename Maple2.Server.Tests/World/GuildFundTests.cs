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

    // Two check-ins of one member at once pay the guild once; the fund stops at the level's ceiling.
    [Test]
    public void CheckIn_TwoAtOncePayOnceAndStopAtTheCeiling() {
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
            Assert.That((guild.Funds, guild.Experience, member.TotalContribution), Is.EqualTo((130, 5, 10)), $"round {round}");
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
}
