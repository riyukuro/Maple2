using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Containers;

namespace Maple2.Server.Tests.World;

// The guild storage on the World server: guild-fund products are stored as counts with the fund, and the master
// gives them out (Guild 109) by the system mail guildStorageItemMail. Every gift is refused whole, or the storage,
// the log row and the mail with its item are stored together.
public class GuildStorageTests {
    private const int Rounds = 20;
    private const int ItemId = 20301138;
    private const int OnlyToMeId = 20301139;
    private const int MailId = 52000002;
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
        test.SetServerTable(new ConstantsTable(guildStorageGiftNeedPeriod: 7, guildStorageItemMail: MailId, MailExpiryDays: 30));
        ItemMetadata onlyToMe = TestGameSession.Metadata(OnlyToMeId, slotMax: 10);
        test.Register(TestGameSession.Metadata(ItemId, slotMax: 10),
            onlyToMe with { Property = onlyToMe.Property with { Tag = ItemTag.GuildStorageItemOnlyToMe } });

        using (GameStorage.Request db = test.Storage.Context()) {
            guild = db.CreateGuild("Storage", test.CharacterId) ?? throw new InvalidOperationException("guild row not created");
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

    private void Store(int itemId, int amount) {
        Assert.That(manager.SpendFund(master.CharacterId, 0, new RewardItem(itemId, 1, amount)), Is.EqualTo(GuildError.none));
    }

    private GuildError Gift(long giver, int amount, string receiver = "Member", int itemId = ItemId) {
        return manager.StorageGift(giver, itemId, 1, amount, receiver, [], out _);
    }

    private Guild StoredGuild() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetGuild(guild.Id)!;
    }

    private ICollection<Mail> Mails(long characterId) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetAllMail(characterId);
    }

    private int ItemRows() {
        using var context = new Maple2.Database.Context.Ms2Context(test.Options);
        return context.Item.Count();
    }

    // A guild-fund buy stores the product with the fund in the one save; a second buy adds to its count.
    [Test]
    public void SpendFund_StoresTheProductWithTheFund() {
        Assert.That(manager.SpendFund(master.CharacterId, 30, new RewardItem(ItemId, 1, 5)), Is.EqualTo(GuildError.none));
        Assert.That(manager.SpendFund(master.CharacterId, 30, new RewardItem(ItemId, 1, 5)), Is.EqualTo(GuildError.none));

        Assert.That((guild.Funds, guild.StoredAmount(ItemId, 1)), Is.EqualTo((40, 10)));
        Guild stored = StoredGuild();
        Assert.That((stored.Funds, stored.StoredAmount(ItemId, 1)), Is.EqualTo((40, 10)));
        Assert.That(ItemRows(), Is.EqualTo(0), "a stored product is a count, not an item row");
    }

    // A refused buy stores nothing; a failed save puts back the fund and the count.
    [Test]
    public void SpendFund_RefusedOrFailedStoresNothing() {
        Store(ItemId, 3);
        Assert.That(manager.SpendFund(master.CharacterId, 101, new RewardItem(ItemId, 1, 5)), Is.Not.EqualTo(GuildError.none));
        Assert.That(manager.SpendFund(member.CharacterId, 10, new RewardItem(ItemId, 1, 5)), Is.Not.EqualTo(GuildError.none));
        Assert.That(manager.SpendFund(master.CharacterId, 10, new RewardItem(ItemId, 1, int.MaxValue)), Is.Not.EqualTo(GuildError.none));

        test.FailSave = _ => true;
        Assert.That(manager.SpendFund(master.CharacterId, 10, new RewardItem(ItemId, 1, 5)), Is.Not.EqualTo(GuildError.none));
        test.FailSave = null;

        Assert.That((guild.Funds, guild.StoredAmount(ItemId, 1)), Is.EqualTo((100, 3)));
        Assert.That(StoredGuild().StoredAmount(ItemId, 1), Is.EqualTo(3));
    }

    // A gift takes the count, logs the row and sends the system mail with a new item of the count, all stored.
    [Test]
    public void Gift_MailsTheItemAndLogsIt() {
        Store(ItemId, 5);

        Assert.That(manager.StorageGift(master.CharacterId, ItemId, 1, 2, "Member", [], out GuildMember? receiver), Is.EqualTo(GuildError.none));

        Assert.That(receiver, Is.SameAs(member));
        Assert.That(guild.StoredAmount(ItemId, 1), Is.EqualTo(3));
        Mail mail = Mails(member.CharacterId).Single();
        Assert.That((mail.Type, mail.Content), Is.EqualTo((MailType.System, MailId.ToString())));
        Assert.That(mail.Items.Select(item => (item.Id, item.Rarity, item.Amount)), Is.EqualTo(new[] { (ItemId, 1, 2) }));
        Guild stored = StoredGuild();
        Assert.That(stored.StoredAmount(ItemId, 1), Is.EqualTo(3));
        GuildStorageLog entry = stored.StorageLogSnapshot().Single();
        Assert.That((entry.GiverCharacterId, entry.GiverName, entry.ReceiverAccountId, entry.ReceiverCharacterId, entry.ReceiverName),
            Is.EqualTo((master.CharacterId, "Master", member.AccountId, member.CharacterId, "Member")));
        Assert.That((entry.ItemId, entry.Rarity, entry.Amount), Is.EqualTo((ItemId, (short) 1, 2)));
    }

    // The channel's rolls (ItemRolls) come with the gift: the mailed item has the stats, sockets and color the
    // channel rolled, as a bag item of the product would. The World used to mail a bare item.
    [Test]
    public void Gift_MailsTheChannelsRolls() {
        Store(ItemId, 5);
        Item rolled = new Item(TestGameSession.Metadata(ItemId, slotMax: 10), 1, 2);
        rolled.Stats = new ItemStats();
        rolled.Stats[ItemStats.Type.Constant].Basic[BasicAttribute.Strength] = new BasicOption(12);
        rolled.Stats[ItemStats.Type.Random].Special[SpecialAttribute.TotalDamage] = new SpecialOption(0.05f);
        rolled.Socket = new ItemSocket(3, 1);
        var color = new EquipColor(new Color(1, 2, 3, 255), new Color(4, 5, 6, 255), new Color(7, 8, 9, 255), 11, 2);
        rolled.Appearance!.Color = color;
        rolled.Transfer!.SocketTransfer = false;

        Assert.That(manager.StorageGift(master.CharacterId, ItemId, 1, 2, "Member", ItemRolls.Write(rolled), out _),
            Is.EqualTo(GuildError.none));

        Item mailed = Mails(member.CharacterId).Single().Items.Single();
        Assert.That(mailed.Stats, Is.Not.Null);
        Assert.That(mailed.Stats![ItemStats.Type.Constant].Basic[BasicAttribute.Strength], Is.EqualTo(new BasicOption(12)));
        Assert.That(mailed.Stats[ItemStats.Type.Random].Special[SpecialAttribute.TotalDamage], Is.EqualTo(new SpecialOption(0.05f)));
        Assert.That((mailed.Socket?.MaxSlots, mailed.Socket?.UnlockSlots), Is.EqualTo(((byte?) 3, (byte?) 1)));
        Assert.That((mailed.Appearance?.Color.PaletteId, mailed.Appearance?.Color.Index, mailed.Appearance?.Color.Tertiary),
            Is.EqualTo(((int?) 11, (int?) 2, (Color?) new Color(7, 8, 9, 255))));
        Assert.That(mailed.Transfer?.SocketTransfer, Is.False);
    }

    // Rolls that do not read (cut short) leave the item bare; the gift still goes.
    [Test]
    public void Gift_UnreadableRollsMailTheItemBare() {
        Store(ItemId, 5);
        Item rolled = new Item(TestGameSession.Metadata(ItemId, slotMax: 10), 1, 2) { Stats = new ItemStats() };
        byte[] rolls = ItemRolls.Write(rolled);

        Assert.That(manager.StorageGift(master.CharacterId, ItemId, 1, 2, "Member", rolls[..^1], out _), Is.EqualTo(GuildError.none));
        Assert.That(Mails(member.CharacterId).Single().Items.Single().Stats, Is.Null);
    }

    // Expel and Leave take the guild lock, so a member cannot leave (or be expelled) between a gift's receiver check
    // and its mail. They used to run without it.
    [TestCase("expel")]
    [TestCase("leave")]
    public void ExpelAndLeave_WaitForTheGuildLock(string which) {
        object guildLock = typeof(GuildManager).GetField("guildLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        Task<GuildError> task;
        Monitor.Enter(guildLock);
        try {
            task = Task.Run(() => which == "expel" ? manager.Expel(master.CharacterId, member.CharacterId) : manager.Leave(member.CharacterId));
            Thread.Sleep(300);
            Assert.That(task.IsCompleted, Is.False, "ran while the guild lock was held");
        } finally {
            Monitor.Exit(guildLock);
        }
        // The in-memory store cannot run the member row delete, so the call may end in an exception; only that it ran
        // after the lock was let go is checked here.
        Assert.That(((IAsyncResult) task).AsyncWaitHandle.WaitOne(5000), Is.True, "ran once the lock was free");
    }

    // The whole stored count leaves the storage row.
    [Test]
    public void Gift_AllOfItRemovesTheRow() {
        Store(ItemId, 2);
        Assert.That(Gift(master.CharacterId, 2), Is.EqualTo(GuildError.none));
        Assert.That(guild.StorageSnapshot(), Is.Empty);
        Assert.That(StoredGuild().StorageSnapshot(), Is.Empty);
    }

    // A failed store gives nothing: the storage and the log are as before in memory and stored, no mail, no item row.
    [Test]
    public void Gift_FailedSaveRestoresAndGivesNothing() {
        Store(ItemId, 5);
        Assert.That(Gift(master.CharacterId, 1), Is.EqualTo(GuildError.none));

        test.FailSave = _ => true;
        Assert.That(Gift(master.CharacterId, 2), Is.EqualTo(GuildError.s_guild_err_unknown));
        test.FailSave = null;

        Assert.That(guild.StoredAmount(ItemId, 1), Is.EqualTo(4));
        Assert.That(guild.StorageLogSnapshot(), Has.Length.EqualTo(1));
        Assert.That(Mails(member.CharacterId), Has.Count.EqualTo(1));
        Assert.That(ItemRows(), Is.EqualTo(1));
        Guild stored = StoredGuild();
        Assert.That((stored.StoredAmount(ItemId, 1), stored.StorageLogSnapshot().Length), Is.EqualTo((4, 1)));
    }

    // The guild row is gone (disbanded): nothing is given and the memory is put back.
    [Test]
    public void Gift_GuildRowGoneGivesNothing() {
        Store(ItemId, 5);
        using (var context = new Maple2.Database.Context.Ms2Context(test.Options)) {
            context.Guild.Remove(context.Guild.Find(guild.Id)!);
            context.SaveChanges();
        }

        Assert.That(Gift(master.CharacterId, 2), Is.EqualTo(GuildError.s_guild_err_unknown));

        Assert.That(guild.StoredAmount(ItemId, 1), Is.EqualTo(5));
        Assert.That(guild.StorageLogSnapshot(), Is.Empty);
        Assert.That(Mails(member.CharacterId), Is.Empty);
        Assert.That(ItemRows(), Is.EqualTo(0));
    }

    // Two gifts at once of the one stored item: one is given, one mail, the count never goes below 0.
    [Test]
    public void Gift_TwoAtOnceGiveOnce() {
        for (int round = 0; round < Rounds; round++) {
            Store(ItemId, 1);
            int mailsBefore = Mails(member.CharacterId).Count;
            var errors = new GuildError[2];
            Parallel.Invoke(
                () => errors[0] = Gift(master.CharacterId, 1),
                () => errors[1] = Gift(master.CharacterId, 1));

            Assert.That(errors, Has.Exactly(1).EqualTo(GuildError.none), $"round {round}");
            Assert.That(guild.StoredAmount(ItemId, 1), Is.EqualTo(0), $"round {round}");
            Assert.That(Mails(member.CharacterId), Has.Count.EqualTo(mailsBefore + 1), $"round {round}");
        }
        Assert.That(ItemRows(), Is.EqualTo(Rounds));
    }

    // Refused gifts change nothing: more than stored, more than a stack, none, not the master, a receiver outside the
    // guild, an OnlyToMe item for another member, a receiver who joined within the need period.
    [TestCase("short", GuildError.s_guild_err_unknown)]
    [TestCase("stack", GuildError.s_guild_err_unknown)]
    [TestCase("none", GuildError.s_guild_err_unknown)]
    [TestCase("notMaster", GuildError.s_guild_err_no_master)]
    [TestCase("stranger", GuildError.s_guild_err_null_member)]
    [TestCase("onlyToMe", GuildError.s_guild_err_unknown)]
    [TestCase("needPeriod", GuildError.s_guild_storage_not_enough_gift_need_period)]
    public void Gift_Refused(string why, GuildError expected) {
        Store(ItemId, 24);
        Store(OnlyToMeId, 4);
        GuildError error = why switch {
            "short" => Gift(master.CharacterId, 25),
            "stack" => Gift(master.CharacterId, 11),
            "none" => Gift(master.CharacterId, 0),
            "notMaster" => Gift(member.CharacterId, 1),
            "stranger" => Gift(master.CharacterId, 1, "Nobody"),
            "onlyToMe" => Gift(master.CharacterId, 1, itemId: OnlyToMeId),
            _ => NeedPeriod(),
        };

        Assert.That(error, Is.EqualTo(expected));
        Assert.That((guild.StoredAmount(ItemId, 1), guild.StoredAmount(OnlyToMeId, 1)), Is.EqualTo((24, 4)));
        Assert.That(guild.StorageLogSnapshot(), Is.Empty);
        Assert.That(Mails(member.CharacterId), Is.Empty);
        Assert.That(ItemRows(), Is.EqualTo(0));
    }

    private GuildError NeedPeriod() {
        member.JoinTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 6 * 86400L;
        return Gift(master.CharacterId, 1);
    }

    // The need period passed: the gift goes. An OnlyToMe item goes to the master.
    [Test]
    public void Gift_AfterTheNeedPeriodAndOnlyToMeToTheMaster() {
        Store(ItemId, 1);
        Store(OnlyToMeId, 1);
        member.JoinTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 7 * 86400L - 60;

        Assert.That(Gift(master.CharacterId, 1), Is.EqualTo(GuildError.none));
        Assert.That(Gift(master.CharacterId, 1, "Master", OnlyToMeId), Is.EqualTo(GuildError.none));
        Assert.That(Mails(master.CharacterId).Single().Items.Single().Id, Is.EqualTo(OnlyToMeId));
    }

    // The log keeps the newest 50 rows.
    [Test]
    public void Gift_LogKeepsFiftyRows() {
        for (int i = 0; i < 51; i++) {
            if (guild.StoredAmount(ItemId, 1) == 0) {
                Store(ItemId, 10);
            }
            Assert.That(Gift(master.CharacterId, 1, "Master"), Is.EqualTo(GuildError.none), $"gift {i}");
        }

        GuildStorageLog[] log = StoredGuild().StorageLogSnapshot();
        Assert.That(log, Has.Length.EqualTo(50));
        Assert.That(log.Select(entry => entry.Amount).Sum(), Is.EqualTo(50));
    }

    // A guild row from before the storage columns (both null) loads with an empty storage and log.
    [Test]
    public void Load_RowWithoutTheColumnsHasAnEmptyStorage() {
        using (var context = new Maple2.Database.Context.Ms2Context(test.Options)) {
            Maple2.Database.Model.Guild row = context.Guild.Find(guild.Id)!;
            row.Storage = null;
            row.StorageLog = null;
            context.SaveChanges();
        }

        Guild stored = StoredGuild();
        Assert.That(stored.StorageSnapshot(), Is.Empty);
        Assert.That(stored.StorageLogSnapshot(), Is.Empty);
    }
}
