using System;
using System.Collections.Generic;
using System.Data.Common;
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
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.World;

// The guild storage's saves against MySQL, where a commit can throw after it went through (its answer lost) and a
// save can fail half way inside the transaction. The in-memory store has no transactions, so these need
// MS2_TEST_GAME_DB (a migrated game db).
// - A lost answer: the row is read back. What it holds stays taken (the gift is mailed, the fund spent); it used to be
//   put back in memory, and the next save of the row wrote it back, so the item was given twice.
// - A refused commit, or a save failing at any step of a gift: nothing is stored, and the memory is put back.
public class GuildStorageCommitTests {
    private const int ItemId = 20301138;
    private const int MailId = 52000002;
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");

    private readonly CommitFaults commits = new();
    private readonly CountSaves saves = new();
    private TestGameSession test = null!;
    private TestGameSession other = null!;
    private Guild guild = null!;
    private GuildManager manager = null!;
    private GuildMember master = null!;
    private GuildMember member = null!;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("a commit's lost answer needs MySQL: set MS2_TEST_GAME_DB");
        }
        commits.Lose = 0;
        commits.Refuse = 0;
        saves.Count = 0;
        saves.FailAt = 0;
        DbContextOptions options = new DbContextOptionsBuilder().UseMySql(Connection, ServerVersion.AutoDetect(Connection))
            .AddInterceptors(commits, saves).Options;
        test = new TestGameSession(options);
        // The member's character row (a guild member row needs one).
        other = new TestGameSession(options);
        var property = (GuildTable.Property) RuntimeHelpers.GetUninitializedObject(typeof(GuildTable.Property));
        property = property with { Level = 1, Capacity = 10, FundMax = 1000, DonateMax = 3, CheckInFund = 30, CheckInExp = 5 };
        test.SetTable(new GuildTable(
            new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>>(),
            new Dictionary<int, IReadOnlyDictionary<int, GuildTable.House>>(),
            new Dictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>>(),
            new Dictionary<short, GuildTable.Property> { [1] = property }));
        test.SetServerTable(new ConstantsTable(guildStorageGiftNeedPeriod: 7, guildStorageItemMail: MailId, MailExpiryDays: 30));
        test.Register(TestGameSession.Metadata(ItemId, slotMax: 10));

        using (GameStorage.Request db = test.Storage.Context()) {
            guild = db.CreateGuild("S" + Guid.NewGuid().ToString("N")[..10], test.CharacterId)
                ?? throw new InvalidOperationException("guild row not created");
        }
        master = Member(test.AccountId, test.CharacterId, "Master", 0);
        member = Member(other.AccountId, other.CharacterId, "Member", 2);
        guild.Members.Clear();
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
        Store(5);
    }

    [TearDown]
    public void TearDown() {
        manager?.Dispose();
        master?.Dispose();
        member?.Dispose();
    }

    private GuildMember Member(long accountId, long characterId, string name, byte rank) {
        var info = new PlayerInfo(new CharacterInfo(accountId, characterId, name, "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []);
        return new GuildMember { GuildId = guild.Id, Info = info, Rank = rank, JoinTime = 1000 };
    }

    private void Store(int amount) {
        Assert.That(manager.SpendFund(master.CharacterId, 0, new RewardItem(ItemId, 1, amount)), Is.EqualTo(GuildError.none));
    }

    private GuildError Gift(int amount) => manager.StorageGift(master.CharacterId, ItemId, 1, amount, "Member", [], out _);

    private Guild StoredGuild() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetGuild(guild.Id)!;
    }

    private int Mails() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetAllMail(member.CharacterId).Count;
    }

    // The gift's commit went through with its answer lost: the gift stands (one mail, the count taken), and the next
    // save of the row keeps the count taken. The old code put the 2 back and the next buy stored 5 + 1.
    [Test]
    public void LostCommit_Gift_KeepsTheCountTaken() {
        commits.Lose = 1;
        Assert.That(Gift(2), Is.EqualTo(GuildError.none));
        Assert.Multiple(() => {
            Assert.That(guild.StoredAmount(ItemId, 1), Is.EqualTo(3));
            Assert.That(guild.StorageLogSnapshot(), Has.Length.EqualTo(1));
            Assert.That(Mails(), Is.EqualTo(1));
            Assert.That(StoredGuild().StoredAmount(ItemId, 1), Is.EqualTo(3));
        });

        Store(1);
        Assert.That(StoredGuild().StoredAmount(ItemId, 1), Is.EqualTo(4), "the next save wrote the taken count back");
    }

    // A fund buy's commit went through with its answer lost: the fund stays spent and the product stored. The old code
    // put the fund back, and the next save of the row wrote it back too.
    [Test]
    public void LostCommit_SpendFund_KeepsTheFundSpent() {
        commits.Lose = 1;
        Assert.That(manager.SpendFund(master.CharacterId, 30, new RewardItem(ItemId, 1, 2)), Is.EqualTo(GuildError.none));
        Assert.That((guild.Funds, guild.StoredAmount(ItemId, 1)), Is.EqualTo((70, 7)));

        Store(1);
        Guild stored = StoredGuild();
        Assert.That((stored.Funds, stored.StoredAmount(ItemId, 1)), Is.EqualTo((70, 8)));
    }

    // A refused commit stored nothing: the gift and the buy are put back, no mail.
    [Test]
    public void RefusedCommit_PutsBack() {
        commits.Refuse = 2;
        Assert.That(Gift(2), Is.EqualTo(GuildError.s_guild_err_unknown));
        Assert.That(manager.SpendFund(master.CharacterId, 30, new RewardItem(ItemId, 1, 2)), Is.EqualTo(GuildError.s_guild_err_unknown));
        Guild stored = StoredGuild();
        Assert.Multiple(() => {
            Assert.That((guild.Funds, guild.StoredAmount(ItemId, 1), guild.StorageLogSnapshot().Length), Is.EqualTo((100, 5, 0)));
            Assert.That((stored.Funds, stored.StoredAmount(ItemId, 1), stored.StorageLogSnapshot().Length), Is.EqualTo((100, 5, 0)));
            Assert.That(Mails(), Is.EqualTo(0));
        });
    }

    // A gift's save failing at any of its steps (the row and the item, the mail, the mail's item) stores none of it.
    [Test]
    public void FailedSave_AtEachStepOfAGift_StoresNothing() {
        saves.Count = 0;
        Assert.That(Gift(1), Is.EqualTo(GuildError.none));
        int steps = saves.Count;
        Assert.That(steps, Is.GreaterThanOrEqualTo(3), "the gift's saves: the row and the item, the mail, the mail's item");

        for (int step = 1; step <= steps; step++) {
            saves.Count = 0;
            saves.FailAt = step;
            Assert.That(Gift(1), Is.EqualTo(GuildError.s_guild_err_unknown), $"failed at save {step}");
            saves.FailAt = 0;
            Guild stored = StoredGuild();
            Assert.Multiple(() => {
                Assert.That((guild.StoredAmount(ItemId, 1), guild.StorageLogSnapshot().Length), Is.EqualTo((4, 1)), $"memory, save {step}");
                Assert.That((stored.StoredAmount(ItemId, 1), stored.StorageLogSnapshot().Length), Is.EqualTo((4, 1)), $"row, save {step}");
                Assert.That(Mails(), Is.EqualTo(1), $"mails, save {step}");
            });
        }
    }

    private sealed class CommitFaults : DbTransactionInterceptor {
        public int Lose;
        public int Refuse;

        public override InterceptionResult TransactionCommitting(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result) {
            if (Refuse > 0) {
                Refuse--;
                throw new InvalidOperationException("the test refused the commit");
            }
            return result;
        }

        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) {
            if (Lose > 0) {
                Lose--;
                throw new InvalidOperationException("the test lost the commit's answer");
            }
        }
    }

    private sealed class CountSaves : SaveChangesInterceptor {
        public int Count;
        public int FailAt;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) {
            Count++;
            if (Count == FailAt) {
                throw new InvalidOperationException($"the test failed save {Count}");
            }
            return result;
        }
    }
}
