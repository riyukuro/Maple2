using System;
using System.Collections.Generic;
using System.Reflection;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestAdditionalEffect as the GM effect dialog writes it (GmCreateRequestsTests has the layout): mode 0 (add) is
// i32 effect id, i32 level, f32 value; mode 1 (reset) has no body. A GM's add puts the effect on the GM and records
// the buff it made; the reset takes off the recorded buffs only. The effects are made up.
public class GmAdditionalEffectTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int GmEffect = 90000001;
    private const int OtherEffect = 90000002;

    private TestGameSession test = null!;
    private GameSession Session => test.Session;

    private void Start(bool gm = true) {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        FieldManager field = Session.Field!;
        var skills = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).GetField("EffectCache", Any)!.GetValue(skills)!;
        foreach (int id in new[] { GmEffect, OtherEffect }) {
            AdditionalEffectMetadata effect = BuffAttributeTests.Effect(id, DisabledBuffTests.Property with { DurationTick = 10000 });
            cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(effect.Id, effect.Level), effect]);
        }
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", Any)!.SetValue(field, skills);
        typeof(GameSession).GetField("<SkillMetadata>k__BackingField", Any)!.SetValue(Session, skills);
        typeof(FieldManager).GetField(nameof(FieldManager.FieldActor), Any)!.SetValue(field, new FieldActor(field, 99, field.Metadata, null!));
        typeof(Actor<Maple2.Model.Game.Player>).GetField("<Field>k__BackingField", Any)!.SetValue(Session.Player, field);
        // The GM's recorded buffs (the test session is built without its field initializers).
        typeof(GameSession).GetField(nameof(GameSession.GmEffects), Any)!.SetValue(Session, new List<Buff>());
        if (gm) {
            Session.Player.AdminPermissions = AdminPermissions.GameMaster;
        }
        test.TakeSent();
    }

    private void Add(int effectId) {
        new RequestAdditionalEffectHandler().Handle(Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteInt(effectId);
            writer.WriteInt(1);
            writer.WriteFloat(0);
        }));
    }

    private void Reset() {
        new RequestAdditionalEffectHandler().Handle(Session, TestGameSession.Packet(writer => writer.WriteByte(1)));
    }

    [Test]
    public void Add_FromAGm_PutsTheEffectOnAndRecordsIt() {
        Start();

        Add(GmEffect);

        Assert.That(Session.Player.Buffs.EnumerateBuffs(GmEffect), Has.Count.EqualTo(1));
        Assert.That(Session.GmEffects, Has.Count.EqualTo(1));
    }

    // An effect the skill data does not have is not added.
    [Test]
    public void Add_UnknownEffect_AddsNothing() {
        Start();

        Add(GmEffect + 100);

        Assert.That(Session.Player.Buffs.EnumerateBuffs(GmEffect + 100), Is.Empty);
        Assert.That(Session.GmEffects, Is.Empty);
    }

    // The reset takes off what the GM's adds made; a buff from elsewhere stays.
    [Test]
    public void Reset_TakesOffOnlyTheRecordedEffects() {
        Start();
        Add(GmEffect);
        Session.Player.Buffs.AddBuff(Session.Player, Session.Player, OtherEffect, 1, Session.Field!.FieldTick);

        Reset();

        Assert.That(Session.Player.Buffs.EnumerateBuffs(GmEffect), Is.Empty);
        Assert.That(Session.Player.Buffs.EnumerateBuffs(OtherEffect), Has.Count.EqualTo(1));
        Assert.That(Session.GmEffects, Is.Empty);
    }

    // From a player both modes are dropped.
    [Test]
    public void FromAPlayer_IsDropped() {
        Start(gm: false);
        Session.Player.Buffs.AddBuff(Session.Player, Session.Player, OtherEffect, 1, Session.Field!.FieldTick);

        Add(GmEffect);
        Reset();

        Assert.That(Session.Player.Buffs.EnumerateBuffs(GmEffect), Is.Empty);
        Assert.That(Session.Player.Buffs.EnumerateBuffs(OtherEffect), Has.Count.EqualTo(1));
    }
}
