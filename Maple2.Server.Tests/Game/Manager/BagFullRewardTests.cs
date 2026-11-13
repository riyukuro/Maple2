using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;
using Serilog;
using StoredItem = Maple2.Model.Game.Item;

namespace Maple2.Server.Tests.Game.Manager;

// What a full bag does to an item the player is owed or already paid for: it comes by mail, or nothing is taken.
public class BagFullRewardTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const long GuildId = 7;
    private TestGameSession test = null!;

    private GameSession Start(params ItemMetadata[] metadata) {
        test = new TestGameSession();
        test.Register(metadata);
        GameSession session = test.Start();
        // Items are owned by a character, account or mail id; the in-memory database starts each at 1, so one mail
        // id is used up first (see BagSplitTests.Mail).
        using GameStorage.Request db = test.Storage.Context();
        db.CreateMail(new Mail { ReceiverId = test.CharacterId + 1, Type = MailType.System, Title = "t", Content = "c" });
        return session;
    }

    // The bag tab of the item with no open slot: its last slot holds a locked stack, which takes no add.
    private static void FullBag(GameSession session, StoredItem sample) =>
        BagSplitTests.Bag(session, sample, (short) (session.Item.Inventory.Filter(_ => true, sample.Inventory).Count + 1), locked: 1);

    private int Mailed(int itemId) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetAllMail(test.CharacterId).SelectMany(mail => mail.Items).Where(item => item.Id == itemId).Sum(item => item.Amount);
    }

    private static GuildTable.Property Property(int checkInCoin = 0, int winCoin = 0) {
        var property = (GuildTable.Property) RuntimeHelpers.GetUninitializedObject(typeof(GuildTable.Property));
        return property with { Level = 1, Capacity = 10, CheckInCoin = checkInCoin, WinMiniGameCoin = winCoin };
    }

    private void Join(GameSession session, GuildTable.Property property) {
        test.SetTable(new GuildTable(
            new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>>(),
            new Dictionary<int, IReadOnlyDictionary<int, GuildTable.House>>(),
            new Dictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>>(),
            new Dictionary<short, GuildTable.Property> { [1] = property }));
        var manager = (GuildManager) RuntimeHelpers.GetUninitializedObject(typeof(GuildManager));
        GC.SuppressFinalize(manager);
        typeof(GuildManager).GetField("session", Any)!.SetValue(manager, session);
        typeof(GuildManager).GetField("tokenSource", Any)!.SetValue(manager, new CancellationTokenSource());
        typeof(GuildManager).GetField("logger", Any)!.SetValue(manager, Log.Logger);
        typeof(GuildManager).GetProperty("Properties", Any)!.SetValue(manager, property);
        var info = new PlayerInfo(new CharacterInfo(session.AccountId, session.CharacterId, "Self", "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []);
        var self = new GuildMember { GuildId = GuildId, Info = info, Rank = 0, JoinTime = 1000 };
        var guild = new Guild(GuildId, "Guild", self);
        guild.Members[self.CharacterId] = self;
        typeof(GuildManager).GetProperty("Guild", Any)!.SetValue(manager, guild);
        session.Guild = manager;
    }

    // The check-in's guild coin with a full bag: the World has taken the check-in, so the coin comes by mail
    // (before, the bag's refusal dropped it).
    [Test]
    public void GuildCheckIn_FullBag_MailsTheCoin() {
        GameSession session = Start(TestGameSession.Metadata(Constant.GuildCoinId));
        Join(session, Property(checkInCoin: 5));
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry>()));
        FullBag(session, session.Field!.ItemDrop.CreateItem(Constant.GuildCoinId)!);
        var handler = new GuildHandler {
            World = test.World,
            TableMetadata = test.TableMetadata,
            BanWordStorage = null!,
            ServerTableMetadata = test.ServerTableMetadata,
        };
        test.World.GuildWorld = _ => new GuildResponse { GuildId = GuildId };

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(15)));

        Assert.Multiple(() => {
            Assert.That(test.World.GuildRequests.Single().CheckIn.GuildId, Is.EqualTo(GuildId));
            Assert.That(BagSplitTests.Held(session, Constant.GuildCoinId), Is.Zero);
            Assert.That(Mailed(Constant.GuildCoinId), Is.EqualTo(5));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // The guild vs game's coin reward with a full bag comes by mail.
    [Test]
    public void GuildVsGameCoin_FullBag_MailsTheCoin() {
        GameSession session = Start(TestGameSession.Metadata(Constant.GuildCoinId));
        Join(session, Property(winCoin: 7));
        FullBag(session, session.Field!.ItemDrop.CreateItem(Constant.GuildCoinId)!);
        var game = new GuildVsGameField(session.Field);
        game.SetMatch(1, GuildId, GuildId + 1, _ => { });
        game.Arrive(session.Player);

        game.GiveReward("guildCoin", GuildVsGameState.BlueTeam, isWin: true);

        Assert.Multiple(() => {
            Assert.That(BagSplitTests.Held(session, Constant.GuildCoinId), Is.Zero);
            Assert.That(Mailed(Constant.GuildCoinId), Is.EqualTo(7));
            Assert.That(test.Errors, Is.Empty);
        });
    }
}
