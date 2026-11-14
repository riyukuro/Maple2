using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Database.Storage;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// The PC bang dialog (feature PCBang, KR on): the KR PcBangBonus.xml and PcBangGiftItem.xml rows, the day record's
// rules, the packets as UIPcBangDialog::OnPacket reads them, and the handler's claims on a session.
public class PcBangTests {
    private const string KrBonus = """
        <ms2>
            <buffs level="1"><buff additionalEffectID="100000006" /><buff additionalEffectID="100000010" /></buffs>
            <playBonus>
                <item requireMins="1" itemID="20001752" count="3" />
                <item requireMins="5" itemID="20001037" count="10" />
                <item requireMins="30" itemID="20302777" count="10" />
                <item requireMins="60" itemID="20303212" count="10" />
                <item requireMins="90" itemID="40100054" count="999" />
                <item requireMins="120" itemID="20303212" count="10" />
            </playBonus>
        </ms2>
        """;
    private const string KrGifts = """
        <ms2>
            <item giftID="1" itemID="70100009" count="1" grade="1" />
            <item giftID="2" itemID="50610022" count="1" grade="4" />
            <item giftID="3" itemID="0" count="1" grade="1" />
        </ms2>
        """;

    private static XmlDocument Xml(string text) {
        var document = new XmlDocument();
        document.LoadXml(text);
        return document;
    }

    internal static PcBangTable Kr => ServerTableMapper.MapPcBang(Xml(KrBonus), Xml(KrGifts));

    [Test]
    public void MapsKrRows() {
        PcBangTable table = Kr;
        Assert.That(table.Buffs[1], Is.EqualTo(new[] { 100000006, 100000010 }));
        Assert.That(table.PlayBonuses.Select(row => row.RequireMinutes), Is.EqualTo(new[] { 1, 5, 30, 60, 90, 120 }));
        Assert.That(table.PlayBonuses[4], Is.EqualTo(new PcBangTable.PlayBonus(90, 40100054, 1, 999, 0)));
        // A gift without an item is left out; the grade is the item's rarity.
        Assert.That(table.Gifts.Keys, Is.EquivalentTo(new[] { 1, 2 }));
        Assert.That(table.Gifts[2], Is.EqualTo(new PcBangTable.Gift(2, 50610022, 4, 1)));
        Assert.That(ServerTableMapper.MapPcBang(null, null) is { Buffs.Count: 0, PlayBonuses.Count: 0, Gifts.Count: 0 });
    }

    [Test]
    public void FoldCountsTheDay() {
        var now = new DateTime(2026, 11, 14, 10, 0, 0);
        PcBangRecord record = PcBangRules.Fold(null, now.AddMinutes(-30), now);
        Assert.That(record, Is.EqualTo(record with { Day = 20261114, PlayMs = 30 * 60_000L, Claimed = 0 }));
        record = PcBangRules.Claim(record, 2);
        Assert.That(PcBangRules.Fold(record, now, now.AddMinutes(5)).PlayMs, Is.EqualTo(35 * 60_000L));
        // A segment begun yesterday counts from midnight; yesterday's record (and its claims) is not today's.
        PcBangRecord next = PcBangRules.Fold(record, now.AddHours(-1), new DateTime(2026, 11, 15, 0, 20, 0));
        Assert.That((next.Day, next.PlayMs, next.Claimed, next.Gifts.Count), Is.EqualTo((20261115, 20 * 60_000L, 0, 0)));
    }

    [Test]
    public void ClaimRules() {
        PcBangTable table = Kr;
        var today = new PcBangRecord(20261114, 30 * 60_000L, 0, []);
        Assert.That(PcBangRules.CanClaim(table, today, 0, 0), Is.EqualTo(PcBangRules.ClaimError.NotPcBang));
        Assert.That(PcBangRules.CanClaim(table, today, 1, 6), Is.EqualTo(PcBangRules.ClaimError.NoRow));
        Assert.That(PcBangRules.CanClaim(table, today, 1, -1), Is.EqualTo(PcBangRules.ClaimError.NoRow));
        Assert.That(PcBangRules.CanClaim(table, today, 1, 2), Is.EqualTo(PcBangRules.ClaimError.None));
        Assert.That(PcBangRules.CanClaim(table, today, 1, 3), Is.EqualTo(PcBangRules.ClaimError.NotReached));
        PcBangRecord claimed = PcBangRules.Claim(today, 2);
        Assert.That(claimed.Claimed, Is.EqualTo(4));
        Assert.That(PcBangRules.CanClaim(table, claimed, 1, 2), Is.EqualTo(PcBangRules.ClaimError.Claimed));

        Assert.That(PcBangRules.CanReceiveGift(table, today, 0, 1), Is.EqualTo(PcBangRules.GiftError.NotPcBang));
        Assert.That(PcBangRules.CanReceiveGift(table, today, 1, 3), Is.EqualTo(PcBangRules.GiftError.NoGift));
        PcBangRecord received = PcBangRules.ReceiveGift(today, 1);
        Assert.That(PcBangRules.CanReceiveGift(table, received, 1, 1), Is.EqualTo(PcBangRules.GiftError.Received));
        Assert.That(PcBangRules.CanReceiveGift(table, received, 1, 2), Is.EqualTo(PcBangRules.GiftError.None));
        Assert.That(PcBangRules.Buffs(table, 0), Is.Empty);
        Assert.That(PcBangRules.Buffs(table, 2), Is.Empty);
    }

    [Test]
    public void Networks() {
        IReadOnlyList<IPNetwork> networks = Target.ParseNetworks("10.1.0.0/16, 192.168.5.7");
        Assert.That(PcBangManager.GradeOf(IPAddress.Parse("10.1.20.3"), networks), Is.EqualTo(1));
        Assert.That(PcBangManager.GradeOf(IPAddress.Parse("10.1.20.3").MapToIPv6(), networks), Is.EqualTo(1));
        Assert.That(PcBangManager.GradeOf(IPAddress.Parse("192.168.5.7"), networks), Is.EqualTo(1));
        Assert.That(PcBangManager.GradeOf(IPAddress.Parse("192.168.5.8"), networks), Is.EqualTo(0));
        Assert.That(PcBangManager.GradeOf(null, networks), Is.EqualTo(0));
        Assert.That(Target.ParseNetworks(null), Is.Empty);
        Assert.That(Target.ParseNetworks(""), Is.Empty);
    }

    [Test]
    public void InfoLayout() {
        PcBangTable table = Kr;
        ByteWriter writer = PcBangPacket.Info(1, 777, 90_000, 5, 100000006, PcBangRules.Rows(table), table.Gifts.Values.ToList(), true);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.PcBangBonus));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That((reader.ReadByte(), reader.ReadInt(), reader.ReadInt(), reader.ReadByte()), Is.EqualTo(((byte) 1, 777, 90_000, (byte) 5)));
        Assert.That(reader.ReadInt(), Is.EqualTo(100000006));
        Assert.That(reader.ReadInt(), Is.EqualTo(6));
        for (int i = 0; i < 6; i++) {
            Assert.That((reader.ReadInt(), reader.ReadShort(), reader.ReadInt()), Is.EqualTo((table.PlayBonuses[i].ItemId, (short) 1, table.PlayBonuses[i].Count)));
            Assert.That(reader.ReadInt(), Is.Zero); // four flags
            Assert.That((reader.ReadInt(), reader.ReadInt(), reader.ReadInt()), Is.EqualTo((0, table.PlayBonuses[i].RequireMinutes * 60_000, 0)));
        }
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        for (int i = 0; i < 2; i++) {
            reader.ReadInt();
            reader.ReadShort();
            reader.ReadInt();
            reader.ReadInt();
            Assert.That(reader.ReadInt(), Is.EqualTo(i + 1));
        }
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.Available, Is.Zero);
    }

    private TestGameSession test = null!;

    private void StartSession(params string[] off) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(20302777), TestGameSession.Metadata(70100009));
        test.SetServerTable(Kr);
        if (off.Length > 0) {
            test.SetFeaturesOff(off);
        }
        test.Start();
        test.EnterMap(TestGameSession.Map(2000062));
        EffectField(100000006, 100000010);
        test.TakeSent();
    }

    // The field's skill data holds the KR level 1 buffs (as ItemFunctionUseTests.EffectField).
    private void EffectField(params int[] ids) {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var skills = new SkillMetadataStorage(new Maple2.Database.Context.MetadataContext(
            new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).GetField("EffectCache", any)!.GetValue(skills)!;
        foreach (int id in ids) {
            AdditionalEffectMetadata effect = BuffAttributeTests.Effect(id, DisabledBuffTests.Property with { DurationTick = 600000 });
            cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(id, (short) 1), effect]);
        }
        FieldManager field = test.Session.Field!;
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", any)!.SetValue(field, skills);
        typeof(FieldManager).GetField(nameof(FieldManager.FieldActor), any)!.SetValue(field, new FieldActor(field, 99, field.Metadata, null!));
        typeof(Actor<Player>).GetField("<Field>k__BackingField", any)!.SetValue(test.Session.Player, field);
    }

    private void Send(Action<ByteWriter> write) => new PcBangHandler().Handle(test.Session, TestGameSession.Packet(write));

    private List<byte[]> PcBangSent() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.PcBangBonus).ToList();

    private int Count(int itemId) => test.Session.Item.Inventory.Find(itemId).Sum(item => item.Amount);

    [Test]
    public void ClaimAtPcBang() {
        StartSession();
        Assume.That(DateTime.Now.TimeOfDay, Is.LessThan(new TimeSpan(23, 58, 0)));
        Account account = test.Session.Player.Value.Account;
        account.PcBang = new PcBangRecord(PcBangRules.Day(DateTime.Now), 31 * 60_000L, 0, []);

        // Not at a PC bang: the notice, nothing given.
        Send(writer => { writer.WriteByte(1); writer.WriteByte(2); });
        Assert.That(PcBangSent().Select(packet => packet[2..]), Is.EqualTo(new[] { new byte[] { 5, 1 } }));
        Assert.That(Count(20302777), Is.Zero);

        test.Session.PcBang.SetGrade(1);
        test.TakeSent();
        Send(writer => { writer.WriteByte(1); writer.WriteByte(2); });
        Assert.That(PcBangSent().Select(packet => packet[2..]), Is.EqualTo(new[] { new byte[] { 1, 2, 4 } }));
        Assert.That(Count(20302777), Is.EqualTo(10));
        Assert.That(account.PcBang!.Claimed, Is.EqualTo(4));

        // Claimed today, or not reached: the status only.
        Send(writer => { writer.WriteByte(1); writer.WriteByte(2); });
        Send(writer => { writer.WriteByte(1); writer.WriteByte(3); });
        Assert.That(PcBangSent().Select(packet => packet[2]), Is.EqualTo(new byte[] { 3, 3 }));
        Assert.That(Count(20302777), Is.EqualTo(10));
    }

    [Test]
    public void GiftOncePerDay() {
        StartSession();
        Send(writer => { writer.WriteByte(2); writer.WriteInt(1); });
        Assert.That(PcBangSent().Select(packet => packet[2..]), Is.EqualTo(new[] { new byte[] { 5, 1 } }));

        test.Session.PcBang.SetGrade(1);
        test.TakeSent();
        Send(writer => { writer.WriteByte(2); writer.WriteInt(1); });
        Assert.That(PcBangSent().Select(packet => packet[2..]), Is.EqualTo(new[] { new byte[] { 2, 1, 0, 0, 0 } }));
        Assert.That(Count(70100009), Is.EqualTo(1));
        Send(writer => { writer.WriteByte(2); writer.WriteInt(1); });
        Send(writer => { writer.WriteByte(2); writer.WriteInt(3); });
        Assert.That(PcBangSent(), Is.Empty);
        Assert.That(Count(70100009), Is.EqualTo(1));
    }

    [Test]
    public void LoadSendsInfoAndGradeSetsField() {
        StartSession();
        Send(writer => { writer.WriteByte(3); });
        List<byte[]> sent = PcBangSent();
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That((sent[0][2], sent[0][3]), Is.EqualTo(((byte) 0, (byte) 0)));
        Assert.That(sent[0][^1], Is.EqualTo(0)); // closed

        test.Session.PcBang.SetGrade(1);
        List<byte[]> grade = test.TakeSent();
        Assert.That(grade.Select(TestGameSession.Op), Does.Contain(SendOp.SetPcBang));
        Assert.That(grade.Single(packet => TestGameSession.Op(packet) == SendOp.PcBangBonus)[2..4], Is.EqualTo(new byte[] { 3, 1 }));
        Assert.That(test.Session.PcBang.Grade, Is.EqualTo(1));
        // The grade's buffs, taken away at grade 0.
        Assert.That(test.Session.Player.Buffs.EnumerateBuffs(100000006), Has.Count.EqualTo(1));
        Assert.That(test.Session.Player.Buffs.EnumerateBuffs(100000010), Has.Count.EqualTo(1));
        test.Session.PcBang.SetGrade(0);
        Assert.That(test.Session.Player.Buffs.EnumerateBuffs(100000006), Is.Empty);
        Assert.That(test.Session.Player.Buffs.EnumerateBuffs(100000010), Is.Empty);
        Assert.That(test.TakeSent().Single(packet => TestGameSession.Op(packet) == SendOp.SetPcBang)[^1], Is.EqualTo(0));
    }

    [Test]
    public void FeatureOff() {
        StartSession(PcBangRules.Feature);
        test.Session.Player.Value.Account.PcBang = null;
        Send(writer => { writer.WriteByte(3); });
        Send(writer => { writer.WriteByte(2); writer.WriteInt(1); });
        test.Session.PcBang.SetGrade(1);
        Assert.That(PcBangSent(), Is.Empty);
        Assert.That(Count(70100009), Is.Zero);
        Assert.That(test.Session.Player.Value.Account.PcBang, Is.Null);
    }
}
