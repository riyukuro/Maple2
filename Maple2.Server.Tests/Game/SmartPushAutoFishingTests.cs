using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// Auto fishing bought by package (KR smartpush 1: AutoFishing, additionalEffect 100000011, durationTick 3 h;
// autoactionpricepackage AutoFishing 0/1/2: 30/60/180 minutes for 40/60/150 merets). The client sends the push id
// and the package index (CSmartPush::vfunc_5) and sets no time: the buff lasts the package's minutes. The result is
// SmartPush bool 1, push id, value (CSceneStateGame::OnSmartPush), which names push 1's effect in the notice.
public class SmartPushAutoFishingTests {
    private const int PushId = 1;
    private const int EffectId = 100000011;
    private const int EffectDurationMs = 10800000;
    private const int MissingPushId = 2;
    private const int MissingEffectId = 100000012; // not in the field's skill data: AddBuff fails

    private TestGameSession test = null!;
    private GameSession session = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.SetTable(new SmartPushTable(new Dictionary<int, SmartPushMetadata> {
            [PushId] = new(PushId, "AutoFishing", SmartPushType.additionalEffect, EffectId, 300, default),
            [MissingPushId] = new(MissingPushId, "SafeRiding", SmartPushType.additionalEffect, MissingEffectId, 120, default),
        }));
        test.SetTable(new AutoActionTable(new Dictionary<string, IReadOnlyDictionary<int, AutoActionMetaData>> {
            ["AutoFishing"] = new Dictionary<int, AutoActionMetaData> {
                [0] = new("AutoFishing", 0, 30, 40, 0),
                [1] = new("AutoFishing", 1, 60, 60, 0),
                [2] = new("AutoFishing", 2, 180, 150, 0),
                [9] = new("AutoFishing", 9, 0, 10, 0),
            },
        }));
        session = test.Start();
        test.EnterMap(TestGameSession.Map(2000062));
        EffectField();
        session.Currency.Meret = 217;
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() => session.Dispose();

    // The field's skill data holds 100000011 as KR has it: group 10, auto fishing, 3 h (as PcBangTests.EffectField).
    private void EffectField() {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var skills = new SkillMetadataStorage(new Maple2.Database.Context.MetadataContext(
            new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).GetField("EffectCache", any)!.GetValue(skills)!;
        AdditionalEffectMetadata effect = BuffAttributeTests.Effect(EffectId,
            DisabledBuffTests.Property with { EventType = BuffEventType.AutoFish, Group = 10, DurationTick = EffectDurationMs });
        cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(EffectId, (short) 1), effect]);
        FieldManager field = session.Field!;
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", any)!.SetValue(field, skills);
        typeof(FieldManager).GetField(nameof(FieldManager.FieldActor), any)!.SetValue(field, new FieldActor(field, 99, field.Metadata, null!));
        typeof(Maple2.Server.Game.Model.Actor<Player>).GetField("<Field>k__BackingField", any)!.SetValue(session.Player, field);
    }

    private void Buy(int package, int pushId = PushId) {
        new SmartPushHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteInt(pushId);
            writer.WriteInt(package);
            writer.WriteInt(0);
            writer.WriteBool(false);
        }));
    }

    [TestCase(0, 30, 40)]
    [TestCase(1, 60, 60)]
    [TestCase(2, 180, 150)]
    public void Package_LastsItsMinutes_ForItsMerets(int package, int minutes, int merets) {
        Buy(package);

        List<Buff> buffs = session.Player.Buffs.EnumerateBuffs(EffectId);
        List<byte[]> sent = test.TakeSent();
        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(217 - merets));
            Assert.That(buffs, Has.Count.EqualTo(1));
            Assert.That(buffs.Single().EndTick - buffs.Single().StartTick, Is.EqualTo(minutes * 60 * 1000L));
            Assert.That(sent.Count(packet => TestGameSession.Op(packet) == SendOp.Buff), Is.EqualTo(1));
            Assert.That(sent.Where(packet => TestGameSession.Op(packet) == SendOp.SmartPush).Select(Convert.ToHexString),
                Is.EqualTo(new[] { Convert.ToHexString(BitConverter.GetBytes((ushort) SendOp.SmartPush)) + "01" + "01000000" + package.ToString("X2") + "000000" }));
        });
    }

    // The client buys a package only when the buff is gone; a buy while held is refused: no price, the end kept.
    [Test]
    public void BuyWhileHeld_IsRefused_AndKeepsTheEnd() {
        Buy(2);
        Buff held = session.Player.Buffs.EnumerateBuffs(EffectId).Single();
        long end = held.EndTick;
        test.TakeSent();

        Buy(0);

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(217 - 150));
            Assert.That(session.Player.Buffs.EnumerateBuffs(EffectId).Single().EndTick, Is.EqualTo(end));
            Assert.That(test.TakeSent().Select(TestGameSession.Op), Has.None.EqualTo(SendOp.SmartPush).And.None.EqualTo(SendOp.Buff));
        });
    }

    // A buff not given costs nothing and has no notice.
    [Test]
    public void BuffNotGiven_TakesNoPrice_AndSendsNoNotice() {
        Buy(0, MissingPushId);

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(217));
            Assert.That(session.Player.Buffs.EnumerateBuffs(MissingEffectId), Is.Empty);
            Assert.That(test.TakeSent().Select(TestGameSession.Op), Has.None.EqualTo(SendOp.SmartPush));
        });
    }

    // A package of no minutes is not sold.
    [Test]
    public void PackageWithoutMinutes_IsNotSold() {
        Buy(9);

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(217));
            Assert.That(session.Player.Buffs.EnumerateBuffs(EffectId), Is.Empty);
        });
    }
}
