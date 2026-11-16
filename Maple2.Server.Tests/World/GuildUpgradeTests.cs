using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Containers;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.World;

// The guild fund's spends on the World server (Guild 92 skill upgrade, 101 house upgrade, 102 house theme, 114 NPC
// upgrade), on KR rows of table/guildbuff.xml, guildhouse.xml and guildnpc.xml. The rule each checks is the
// client's (GuildRules, tested on its own); here the fund is taken with the change and both stored, a refused
// spend takes nothing, and a save the database refuses undoes both.
public class GuildUpgradeTests {
    private TestGameSession test = null!;
    private Guild guild = null!;
    private GuildManager manager = null!;
    private GuildMember master = null!;
    private GuildMember member = null!;

    // The guild at the given level (one property row of that level), with the KR rows.
    private void Start(short guildLevel, int funds) {
        test = new TestGameSession();
        var property = (GuildTable.Property) RuntimeHelpers.GetUninitializedObject(typeof(GuildTable.Property));
        property = property with { Level = guildLevel, Capacity = 10, FundMax = 1_000_000_000 };
        test.SetTable(new GuildTable(
            new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>> {
                // <guildBuff id="1" level="1" requireLevel="1" upgradeCost="0" cost="20000000" duration="10080" />
                // <guildBuff id="1" level="2" requireLevel="5" upgradeCost="30000000" cost="30000000" duration="10080" />
                [1] = new Dictionary<short, GuildTable.Buff> {
                    [1] = new(1, 1, 1, 20_000_000, 0, 10080),
                    [2] = new(1, 2, 5, 30_000_000, 30_000_000, 10080),
                },
            },
            new Dictionary<int, IReadOnlyDictionary<int, GuildTable.House>> {
                // <guildHouse level="1" theme="1" fieldID="66100001" upgradeReqGuildLevel="1" upgradeCost="0" rethemeCost="0" />
                [1] = new Dictionary<int, GuildTable.House> { [1] = new(66100001, 1, 0, 0, []) },
                // <guildHouse level="2" theme="1|2" fieldID="66100002|66100011" upgradeReqGuildLevel="6" upgradeCost="50000000" rethemeCost="10000000" />
                [2] = new Dictionary<int, GuildTable.House> {
                    [1] = new(66100002, 6, 50_000_000, 10_000_000, []),
                    [2] = new(66100011, 6, 50_000_000, 10_000_000, []),
                },
            },
            new Dictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>> {
                // <npc type="goods" level="1" upgradeCost="0" requireGuildLevel="2" requireHouseLevel="1" />
                // <npc type="goods" level="2" upgradeCost="30000000" requireGuildLevel="7" requireHouseLevel="2" />
                [GuildNpcType.Goods] = new Dictionary<short, GuildTable.Npc> {
                    [1] = new(GuildNpcType.Goods, 1, 2, 1, 0),
                    [2] = new(GuildNpcType.Goods, 2, 7, 2, 30_000_000),
                },
            },
            new Dictionary<short, GuildTable.Property> { [guildLevel] = property }));

        using (GameStorage.Request db = test.Storage.Context()) {
            guild = db.CreateGuild("Upgrade", test.CharacterId) ?? throw new InvalidOperationException("guild row not created");
        }
        master = Member(test.CharacterId, "Master", 0);
        member = Member(test.CharacterId + 500, "Member", 2);
        guild.Members[master.CharacterId] = master;
        guild.Members[member.CharacterId] = member;
        guild.Funds = funds;
        using (GameStorage.Request db = test.Storage.Context()) {
            db.SaveGuild(guild);
        }
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

    private Guild Stored() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetGuild(guild.Id)!;
    }

    private static int BuffLevel(Guild of, int id) => of.Buffs.Single(buff => buff.Id == id).Level;

    private void FailGuildSaves() {
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.Guild>().Any(entry => entry.State == EntityState.Modified);
    }

    [Test]
    public void UpgradeBuff_TheFundPaysTheNextLevel() {
        Start(guildLevel: 5, funds: 40_000_000);

        Assert.That(manager.UpgradeBuff(master.CharacterId, 1), Is.EqualTo(GuildError.none));

        Assert.Multiple(() => {
            Assert.That((guild.Funds, BuffLevel(guild, 1)), Is.EqualTo((10_000_000, 2)));
            Assert.That((Stored().Funds, BuffLevel(Stored(), 1)), Is.EqualTo((10_000_000, 2)));
        });
    }

    // Not the master, the guild below requireLevel 5, a fund short of 30,000,000, a skill the guild lacks, the top
    // level (no level 3 row): refused, nothing taken.
    [TestCase(false, 5, 40_000_000, 1, GuildError.s_guild_err_no_master)]
    [TestCase(true, 4, 40_000_000, 1, GuildError.s_guild_err_not_enough_guild_level)]
    [TestCase(true, 5, 29_999_999, 1, GuildError.s_guild_err_not_enough_guild_fund)]
    [TestCase(true, 5, 40_000_000, 99, GuildError.s_guild_err_unknown)]
    public void UpgradeBuff_Refused_TakesNothing(bool asMaster, short guildLevel, int funds, int buffId, GuildError error) {
        Start(guildLevel, funds);

        Assert.That(manager.UpgradeBuff(asMaster ? master.CharacterId : member.CharacterId, buffId), Is.EqualTo(error));

        Assert.That((guild.Funds, BuffLevel(guild, 1), Stored().Funds), Is.EqualTo((funds, 1, funds)));
    }

    [Test]
    public void UpgradeBuff_AtTheTopLevelIsRefused() {
        Start(guildLevel: 5, funds: 100_000_000);
        Assert.That(manager.UpgradeBuff(master.CharacterId, 1), Is.EqualTo(GuildError.none));

        Assert.That(manager.UpgradeBuff(master.CharacterId, 1), Is.EqualTo(GuildError.s_guild_err_unknown));
        Assert.That((guild.Funds, BuffLevel(guild, 1)), Is.EqualTo((70_000_000, 2)));
    }

    [Test]
    public void UpgradeBuff_NotStored_UndoesTheFundAndTheLevel() {
        Start(guildLevel: 5, funds: 40_000_000);
        FailGuildSaves();

        Assert.That(manager.UpgradeBuff(master.CharacterId, 1), Is.EqualTo(GuildError.s_guild_err_unknown));
        test.FailSave = null;

        Assert.That((guild.Funds, BuffLevel(guild, 1)), Is.EqualTo((40_000_000, 1)));
        Assert.That((Stored().Funds, BuffLevel(Stored(), 1)), Is.EqualTo((40_000_000, 1)));
    }

    // A house of rank 1 goes to rank 2 in the picked theme for 50,000,000 (upgradeReqGuildLevel 6); then a change to
    // the other theme of rank 2 costs its rethemeCost, 10,000,000, and the same theme again is refused.
    [Test]
    public void UpgradeHouse_ThenChangeTheme_TheFundPaysEach() {
        Start(guildLevel: 6, funds: 70_000_000);

        Assert.That(manager.UpgradeHouse(master.CharacterId, 2), Is.EqualTo(GuildError.none));
        Assert.That((guild.Funds, guild.HouseRank, guild.HouseTheme), Is.EqualTo((20_000_000, 2, 2)));
        Assert.That((Stored().Funds, Stored().HouseRank, Stored().HouseTheme), Is.EqualTo((20_000_000, 2, 2)));

        Assert.That(manager.ChangeHouseTheme(master.CharacterId, 2), Is.EqualTo(GuildError.s_guild_err_unknown));
        Assert.That(manager.ChangeHouseTheme(master.CharacterId, 1), Is.EqualTo(GuildError.none));
        Assert.That((guild.Funds, guild.HouseTheme), Is.EqualTo((10_000_000, 1)));
        Assert.That((Stored().Funds, Stored().HouseTheme), Is.EqualTo((10_000_000, 1)));
    }

    [TestCase(false, 6, 70_000_000, 2, GuildError.s_guild_err_no_master)]
    [TestCase(true, 5, 70_000_000, 2, GuildError.s_guild_err_not_enough_guild_level)]
    [TestCase(true, 6, 49_999_999, 2, GuildError.s_guild_err_not_enough_guild_fund)]
    [TestCase(true, 6, 70_000_000, 3, GuildError.s_guild_err_unknown)]
    public void UpgradeHouse_Refused_TakesNothing(bool asMaster, short guildLevel, int funds, int theme, GuildError error) {
        Start(guildLevel, funds);

        Assert.That(manager.UpgradeHouse(asMaster ? master.CharacterId : member.CharacterId, theme), Is.EqualTo(error));

        Assert.That((guild.Funds, guild.HouseRank, guild.HouseTheme, Stored().Funds), Is.EqualTo((funds, 1, 1, funds)));
    }

    [Test]
    public void UpgradeHouse_NotStored_UndoesTheFundAndTheHouse() {
        Start(guildLevel: 6, funds: 70_000_000);
        FailGuildSaves();

        Assert.That(manager.UpgradeHouse(master.CharacterId, 2), Is.EqualTo(GuildError.s_guild_err_unknown));
        test.FailSave = null;

        Assert.That((guild.Funds, guild.HouseRank, guild.HouseTheme), Is.EqualTo((70_000_000, 1, 1)));
        Assert.That((Stored().Funds, Stored().HouseRank, Stored().HouseTheme), Is.EqualTo((70_000_000, 1, 1)));
    }

    // The goods NPC the guild does not have yet starts at level 1 (free, guild level 2); level 2 needs guild level 7
    // and house rank 2, and costs 30,000,000.
    [Test]
    public void UpgradeNpc_FirstLevelThenTheNext() {
        Start(guildLevel: 7, funds: 40_000_000);

        Assert.That(manager.UpgradeNpc(master.CharacterId, GuildNpcType.Goods), Is.EqualTo(GuildError.none));
        Assert.That(guild.Npcs, Is.EqualTo(new[] { new GuildNpc(GuildNpcType.Goods, 1) }));
        Assert.That(guild.Funds, Is.EqualTo(40_000_000));

        // House rank 1: level 2 is refused with the guild level text.
        Assert.That(manager.UpgradeNpc(master.CharacterId, GuildNpcType.Goods), Is.EqualTo(GuildError.s_guild_err_not_enough_guild_level));
        guild.HouseRank = 2;

        Assert.That(manager.UpgradeNpc(master.CharacterId, GuildNpcType.Goods), Is.EqualTo(GuildError.none));
        Assert.Multiple(() => {
            Assert.That((guild.Funds, guild.Npcs.Single()), Is.EqualTo((10_000_000, new GuildNpc(GuildNpcType.Goods, 2))));
            Assert.That((Stored().Funds, Stored().Npcs.Single()), Is.EqualTo((10_000_000, new GuildNpc(GuildNpcType.Goods, 2))));
        });
    }

    [Test]
    public void UpgradeNpc_NotStored_UndoesTheFundAndTheNpc() {
        Start(guildLevel: 7, funds: 40_000_000);
        guild.HouseRank = 2;
        Assert.That(manager.UpgradeNpc(master.CharacterId, GuildNpcType.Goods), Is.EqualTo(GuildError.none));
        FailGuildSaves();

        Assert.That(manager.UpgradeNpc(master.CharacterId, GuildNpcType.Goods), Is.EqualTo(GuildError.s_guild_err_unknown));
        test.FailSave = null;

        Assert.That((guild.Funds, guild.Npcs.Single()), Is.EqualTo((40_000_000, new GuildNpc(GuildNpcType.Goods, 1))));
        Assert.That(Stored().Funds, Is.EqualTo(40_000_000));
    }
}
