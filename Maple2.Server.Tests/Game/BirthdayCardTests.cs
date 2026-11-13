using System;
using System.CommandLine;
using System.CommandLine.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Commands;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// The account birthday. The birthday card (npc kind 88, UIBirthdayCardDialog) sends BirthdayCard 0xA6 mode 0 on load
// and reads 0x111 mode 1 (object id, birthday) into PC+0x11e8; it shows the month when it is the current one
// (PC::FUN_141660030). Only month and day are kept (12:00 UTC of that day in 2000).
public class BirthdayCardTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.TakeSent();
    }

    [Test]
    public void Of_KeepsMonthAndDayAtNoonUtc() {
        long? value = BirthdayRules.Of(3, 14);
        Assert.That(value, Is.EqualTo(new DateTimeOffset(2000, 3, 14, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds()));
        Assert.That(BirthdayRules.Of(2, 29), Is.Not.Null);
    }

    [TestCase(0, 1)]
    [TestCase(13, 1)]
    [TestCase(2, 30)]
    [TestCase(4, 31)]
    [TestCase(1, 0)]
    public void Of_RefusesWhatIsNotADate(int month, int day) {
        Assert.That(BirthdayRules.Of(month, day), Is.Null);
    }

    [Test]
    public void IsBirthdayMonth_TestsTheMonthOnly() {
        long march = BirthdayRules.Of(3, 14)!.Value;
        TimeZoneInfo utc = TimeZoneInfo.Utc;
        Assert.Multiple(() => {
            Assert.That(BirthdayRules.IsBirthdayMonth(march, new DateTimeOffset(2031, 3, 1, 0, 0, 0, TimeSpan.Zero), utc), Is.True);
            Assert.That(BirthdayRules.IsBirthdayMonth(march, new DateTimeOffset(2031, 3, 31, 23, 0, 0, TimeSpan.Zero), utc), Is.True);
            Assert.That(BirthdayRules.IsBirthdayMonth(march, new DateTimeOffset(2031, 4, 14, 0, 0, 0, TimeSpan.Zero), utc), Is.False);
            Assert.That(BirthdayRules.IsBirthdayMonth(0, new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero), utc), Is.False);
        });
    }

    // Both months are taken in the server's zone, as the client's CMsTime does (TimeSyncPacket sends that zone).
    [Test]
    public void IsBirthdayMonth_InTheServerZone() {
        TimeZoneInfo kst = TimeZoneInfo.CreateCustomTimeZone("kst", TimeSpan.FromHours(9), "kst", "kst");
        TimeZoneInfo plus14 = TimeZoneInfo.CreateCustomTimeZone("p14", TimeSpan.FromHours(14), "p14", "p14");
        long march = BirthdayRules.Of(3, 14)!.Value;
        long march31 = BirthdayRules.Of(3, 31)!.Value; // 03-31 12:00 UTC is 04-01 02:00 at +14
        var lateMarchUtc = new DateTimeOffset(2031, 3, 31, 20, 0, 0, TimeSpan.Zero); // 04-01 at +9 and +14
        Assert.Multiple(() => {
            Assert.That(BirthdayRules.IsBirthdayMonth(march, lateMarchUtc, TimeZoneInfo.Utc), Is.True);
            Assert.That(BirthdayRules.IsBirthdayMonth(march, lateMarchUtc, kst), Is.False);
            Assert.That(BirthdayRules.IsBirthdayMonth(march31, lateMarchUtc, plus14), Is.True);
            Assert.That(BirthdayRules.IsBirthdayMonth(march31, new DateTimeOffset(2031, 3, 15, 0, 0, 0, TimeSpan.Zero), plus14), Is.False);
        });
    }

    [Test]
    public void CardLoad_RepliesWithTheBirthday() {
        long birthday = BirthdayRules.Of(12, 25)!.Value;
        session.Player.Value.Account.Birthday = birthday;

        new BirthdayCardHandler().Handle(session, TestGameSession.Packet(writer => writer.WriteByte(0)));

        byte[] sent = test.TakeSent().Single();
        Assert.That(TestGameSession.Op(sent), Is.EqualTo(SendOp.BirthdayCard));
        var reader = new ByteReader(sent, 2);
        Assert.Multiple(() => {
            Assert.That(reader.ReadByte(), Is.EqualTo(1));
            Assert.That(reader.ReadInt(), Is.EqualTo(session.Player.ObjectId));
            Assert.That(reader.ReadLong(), Is.EqualTo(birthday));
            Assert.That(reader.Available, Is.EqualTo(0));
        });
    }

    [Test]
    public void CardOtherMode_SendsNothing() {
        new BirthdayCardHandler().Handle(session, TestGameSession.Packet(writer => writer.WriteByte(1)));

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Command_SetsClearsAndRefuses() {
        var command = new PlayerCommand(session, null!);

        Assert.That(command.Invoke(["birthday", "7", "4"], new TestConsole()), Is.EqualTo(0));
        Assert.That(session.Player.Value.Account.Birthday, Is.EqualTo(BirthdayRules.Of(7, 4)));
        Assert.That(test.TakeSentOps(), Does.Contain(SendOp.BirthdayCard));

        Assert.That(command.Invoke(["birthday", "2", "30"], new TestConsole()), Is.EqualTo(1));
        Assert.That(session.Player.Value.Account.Birthday, Is.EqualTo(BirthdayRules.Of(7, 4)));

        Assert.That(command.Invoke(["birthday", "0", "0"], new TestConsole()), Is.EqualTo(0));
        Assert.That(session.Player.Value.Account.Birthday, Is.EqualTo(0));
    }

    // The command stores the birthday at once, with no player save: the character list reads the stored value.
    [Test]
    public void Command_StoresAtOnce() {
        Assert.That(new PlayerCommand(session, null!).Invoke(["birthday", "9", "30"], new TestConsole()), Is.EqualTo(0));

        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetAccount(session.AccountId)?.Birthday, Is.EqualTo(BirthdayRules.Of(9, 30)));
    }

    // The same on MySQL, where the birthday is one column update. Needs MS2_TEST_GAME_DB, a scratch MySQL game database.
    [Test]
    public void SetAccountBirthday_OnMySql() {
        string? connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");
        if (connection == null) {
            Assert.Ignore("needs MySQL: set MS2_TEST_GAME_DB");
        }
        var mysql = new TestGameSession(new DbContextOptionsBuilder().UseMySql(connection, ServerVersion.AutoDetect(connection)).Options);
        using GameStorage.Request db = mysql.Storage.Context();
        Assert.That(db.SetAccountBirthday(mysql.AccountId, BirthdayRules.Of(5, 5)!.Value), Is.True);
        Assert.That(db.GetAccount(mysql.AccountId)?.Birthday, Is.EqualTo(BirthdayRules.Of(5, 5)));
        Assert.That(db.SetAccountBirthday(-1, 1), Is.False);
    }

    [Test]
    public void Birthday_IsSaved() {
        session.Player.Value.Account.Birthday = BirthdayRules.Of(10, 1)!.Value;
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SavePlayer(session.Player.Value), Is.True);
        }

        using (GameStorage.Request db = test.Storage.Context()) {
            Account? account = db.GetAccount(session.Player.Value.Account.Id);
            Assert.That(account?.Birthday, Is.EqualTo(BirthdayRules.Of(10, 1)));
        }
    }

    // KR 11003587 (kind 88): select 0 and pick script 10, no Job state; its job condition row needs the birthday. In the
    // birthday month the talk carries no script, so the client opens the card; otherwise the npc talks script 10.
    [TestCase(true)]
    [TestCase(false)]
    public void CardNpc_OffersTheCardOnlyInTheBirthdayMonth(bool birthdayMonth) {
        const int npcId = 11003587;
        test.Register(new ScriptMetadata(npcId, ScriptType.Npc, new Dictionary<int, ScriptState> {
            [0] = new(0, ScriptStateType.Select, false, null, [new CinematicContent("", NpcTalkButton.None, 0, [], [])]),
            [10] = new(10, ScriptStateType.Script, true, null, [new CinematicContent("", NpcTalkButton.None, 0, [], [])]),
        }));
        test.SetServerTable(new ScriptConditionTable(new Dictionary<int, Dictionary<int, ScriptConditionMetadata>>()));
        test.SetServerTable(new ScriptFunctionTable(new Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>>()));
        test.SetServerTable(new JobConditionTable(new Dictionary<int, JobConditionMetadata> {
            [npcId] = new(npcId, 0, 0, 0, JobCode.None, false, 0, 0, 0, 0, 0, 0, false, false, false, 0, true, JobCode.None, 0, 0, false, 0),
        }));
        int month = birthdayMonth ? DateTime.Now.Month : DateTime.Now.Month % 12 + 1;
        session.Player.Value.Account.Birthday = BirthdayRules.Of(month, 1)!.Value;

        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var basic = new NpcMetadataBasic(2, 2, 1, 88, 0, 1, 1, 1, 0, false, 0, 0, 0, 0, [], [], 0);
        var metadata = new NpcMetadata(npcId, "npc", "", null!, null!, basic, null!, null!, null!, null!, null!, null!, null, null!);
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", any)!.SetValue(npc, new Npc(metadata, null));
        typeof(FieldNpc).GetField(nameof(FieldNpc.SpawnAnimation), any)!.SetValue(npc, "spawn");

        NpcTalkHandler.BeginTalk(session, npc);

        Assert.Multiple(() => {
            Assert.That(test.TakeSentOps(), Does.Contain(SendOp.NpcTalk));
            Assert.That(session.NpcScript?.State?.Id, birthdayMonth ? Is.Null : Is.EqualTo(10));
        });
    }
}
