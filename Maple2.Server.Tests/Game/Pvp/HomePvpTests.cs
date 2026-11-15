using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Pvp;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.Pvp;

// The home's PvP match: the dialog's settings, the match's sides, kills and end (HomePvpState), and in a home with its
// owner and a visitor the start from the dialog (HomeAction 14, 1), the fight's points, buffs and rows, and the end.
public class HomePvpTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public void Settings_AreTheDialogsLists() {
        Assert.That(HomePvpSettings.Default.Valid, Is.True);
        Assert.That(new HomePvpSettings(1, 4, 1, 1, 1, 3).Valid, Is.True);
        Assert.That(new HomePvpSettings(2, 0, 0, 0, 0, 0).Valid, Is.False);
        Assert.That(new HomePvpSettings(0, 5, 0, 0, 0, 0).Valid, Is.False);
        Assert.That(new HomePvpSettings(0, 0, 0, 0, 0, 4).Valid, Is.False);
        Assert.That(new HomePvpSettings(0, 0, 0, 1, 0, 0).NoRevive, Is.True);
        // Survival: the dialog sends revive 0, a 1 there is not taken.
        Assert.That(new HomePvpSettings(0, 0, 0, 1, 1, 0).NoRevive, Is.False);
        Assert.That(new HomePvpSettings(0, 0, 0, 0, 0, 2).Seconds, Is.EqualTo(300));
        Assert.That(HomePvpSettings.Default.Seconds, Is.EqualTo(0));
        Assert.That(HomePvpSettings.Default.Effect, Is.EqualTo((70000171, (short) 1)));
        Assert.That(new HomePvpSettings(0, 3, 0, 0, 0, 0).Effect, Is.EqualTo((70000173, (short) 3)));
        Assert.That(HomePvpField.Rows(new HomePvpSettings(0, 0, 1, 1, 0, 0)),
            Is.EqualTo(new[] { HomePermission.Pvp, HomePermission.Potion, HomePermission.Revival }));
        Assert.That(HomePvpField.Rows(HomePvpSettings.Default), Is.EqualTo(new[] { HomePermission.Pvp }));
    }

    [Test]
    public void FreeForAll_EachPlayerIsASide() {
        var state = new HomePvpState(HomePvpSettings.Default, 0);
        Assert.That(state.DueToActivate(4999), Is.False);
        Assert.That(state.DueToActivate(5000), Is.True);
        IReadOnlyList<HomePvpEntry> entries = state.Activate([(1, 7), (2, 7), (3, 0)], 5000);
        Assert.That(entries.Select(entry => entry.Point).Distinct().Count(), Is.EqualTo(3));
        Assert.That(state.Hostile(1, 2), Is.True);
        Assert.That(state.Hostile(1, 1), Is.False);
        Assert.That(state.Hostile(1, 4), Is.False);
    }

    [Test]
    public void ByParty_APartyIsOneSide_ASoloPlayerItsOwn() {
        var state = new HomePvpState(new HomePvpSettings(1, 0, 0, 0, 0, 0), 0);
        IReadOnlyList<HomePvpEntry> entries = state.Activate([(1, 7), (2, 7), (3, 0), (4, 0)], 5000);
        Dictionary<long, int> points = entries.ToDictionary(entry => entry.CharacterId, entry => entry.Point);
        Assert.That(points[1], Is.EqualTo(points[2]));
        Assert.That(new[] { points[1], points[3], points[4] }.Distinct().Count(), Is.EqualTo(3));
        Assert.That(state.Hostile(1, 2), Is.False);
        Assert.That(state.Hostile(1, 3), Is.True);
    }

    [Test]
    public void FewerThanTwoSides_CallsItOff() {
        var state = new HomePvpState(new HomePvpSettings(1, 0, 0, 0, 0, 0), 0);
        Assert.That(state.Activate([(1, 7), (2, 7)], 5000), Is.Empty);
        Assert.That(state.Phase, Is.EqualTo(HomePvpPhase.Ended));
        Assert.That(state.InPlay(1), Is.False);
    }

    [Test]
    public void Survival_TheLastSideWins() {
        var state = new HomePvpState(new HomePvpSettings(0, 0, 0, 0, 1, 0), 0);
        state.Activate([(1, 0), (2, 0), (3, 0)], 5000);
        state.Damage(1, 2, 6000);
        Assert.That(state.Death(2, 6000 + HomePvpState.KillCreditMs), Is.EqualTo(1));
        Assert.That(state.InPlay(2), Is.False);
        Assert.That(state.DueToEnd(7000), Is.False);
        // A hit too long ago is no kill.
        state.Damage(1, 3, 7000);
        Assert.That(state.Death(3, 7001 + HomePvpState.KillCreditMs), Is.EqualTo(0));
        Assert.That(state.DueToEnd(20000), Is.True);
        Assert.That(state.End(), Is.EqualTo(state.Entry(1)!.Point));
    }

    [Test]
    public void TimeLimit_MostKillsWins_ATieIsADraw() {
        var state = new HomePvpState(new HomePvpSettings(0, 0, 0, 0, 0, 1), 0);
        state.Activate([(1, 0), (2, 0)], 5000);
        Assert.That(state.DueToEnd(5000 + 179_999), Is.False);
        Assert.That(state.DueToEnd(5000 + 180_000), Is.True);
        Assert.That(state.End(), Is.EqualTo(0));

        state = new HomePvpState(HomePvpSettings.Default, 0);
        state.Activate([(1, 0), (2, 0)], 5000);
        state.Damage(2, 1, 6000);
        Assert.That(state.Death(1, 6000), Is.EqualTo(2));
        // Any number of revives: the victim stays in play.
        Assert.That(state.InPlay(1), Is.True);
        Assert.That(state.DueToEnd(long.MaxValue - 1), Is.False);
        Assert.That(state.End(), Is.EqualTo(state.Entry(2)!.Point));
    }

    [Test]
    public void Leaving_IsOut() {
        var state = new HomePvpState(HomePvpSettings.Default, 0);
        state.Activate([(1, 0), (2, 0)], 5000);
        Assert.That(state.Leave(2), Is.True);
        Assert.That(state.Leave(2), Is.False);
        Assert.That(state.DueToEnd(6000), Is.True);
        Assert.That(state.End(), Is.EqualTo(state.Entry(1)!.Point));
    }

    [Test]
    public void PvpControl_IsTheClientsDialogOpen() {
        byte[] bytes = HomeActionPacket.PvpControl(new HomePvpSettings(1, 2, 1, 0, 1, 3)).ToArray();
        Assert.That(bytes.Skip(2).ToArray(), Is.EqualTo(new byte[] { 14, 0, 1, 2, 1, 0, 1, 3 }));
    }

    private sealed class Home {
        public required TestGameSession Owner;
        public required TestGameSession Visitor;
        public required HomeFieldManager Field;
    }

    // The owner in its home with a visitor; the field can give buffs (the dialog's effects at levels 1 to 4).
    private static Home InAHome() {
        var owner = new TestGameSession();
        owner.Start();
        owner.Session.Housing.InitNewHome("home", null);
        owner.EnterHome();
        var visitor = new TestGameSession(owner.Options);
        visitor.Start();
        var field = (HomeFieldManager) owner.Session.Field!;
        // The test field is not constructed: its match lock is set here.
        typeof(HomeFieldManager).GetField("pvpMutex", Any)!.SetValue(field, new object());
        typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(visitor.Session.Player, owner.Session.Player.ObjectId + 1);
        field.Players[visitor.Session.Player.ObjectId] = visitor.Session.Player;
        visitor.Session.Field = field;

        var skills = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).GetField("EffectCache", Any)!.GetValue(skills)!;
        var always = new BeginCondition(0, 0, Gender.All, [], 1, 0, 0, 0, false, false, false, true, false, true, [],
            new Dictionary<BasicAttribute, long>(), [], [], [], [], null, null, null, null);
        foreach ((int id, short level) in new[] { (HomePvpSettings.DefaultEffectId, (short) 1), (HomePvpSettings.HealthEffectId, (short) 1),
                     (HomePvpSettings.HealthEffectId, (short) 2), (HomePvpSettings.HealthEffectId, (short) 3), (HomePvpSettings.HealthEffectId, (short) 4) }) {
            cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(id, level), DisabledBuffTests.Effect(id) with {
                Level = level,
                Condition = always,
                Reflect = new AdditionalEffectMetadataReflect(0, 0, 0, 0, 0, 0, new Dictionary<BasicAttribute, long>(), new Dictionary<BasicAttribute, float>()),
                Update = new AdditionalEffectMetadataUpdate(null, [], [], [], []),
                Consume = new AdditionalEffectMetadataConsume(0, 0),
            }]);
        }
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", Any)!.SetValue(field, skills);
        typeof(FieldManager).GetField(nameof(FieldManager.FieldActor), Any)!.SetValue(field, new FieldActor(field, 99, field.Metadata, null!));
        foreach (TestGameSession test in new[] { owner, visitor }) {
            typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, field);
            test.TakeSent();
        }
        return new Home { Owner = owner, Visitor = visitor, Field = field };
    }

    private static void Dialog(TestGameSession test, byte sub, HomePvpSettings settings) {
        new HomeActionHandler().Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(14);
            writer.WriteByte(sub);
            writer.WriteByte(settings.Type);
            writer.WriteByte(settings.Buff);
            writer.WriteByte(settings.Potion);
            writer.WriteByte(settings.Revive);
            writer.WriteByte(settings.Rule);
            writer.WriteByte(settings.Time);
        }));
    }

    [Test]
    public void OnlyTheOwnersValidStart_StartsAMatch() {
        Home home = InAHome();
        Dialog(home.Visitor, 1, HomePvpSettings.Default);
        Assert.That(home.Field.PvpZone, Is.Null);
        Dialog(home.Owner, 1, new HomePvpSettings(0, 9, 0, 0, 0, 0));
        Assert.That(home.Field.PvpZone, Is.Null);
        Dialog(home.Owner, 0, HomePvpSettings.Default);
        Assert.That(home.Field.PvpZone, Is.Null);

        var settings = new HomePvpSettings(0, 2, 1, 0, 0, 1);
        Dialog(home.Owner, 1, settings);
        Assert.That(home.Field.PvpZone, Is.TypeOf<HomePvpField>());
        Assert.That(home.Field.PvpSettings, Is.EqualTo(settings));
        Assert.That(home.Visitor.TakeSentOps(), Does.Contain(SendOp.Notice));
        // A second start while it runs is dropped.
        IPvpZoneField? first = home.Field.PvpZone;
        Dialog(home.Owner, 1, HomePvpSettings.Default);
        Assert.That(home.Field.PvpZone, Is.SameAs(first));
        Assert.That(home.Field.PvpSettings, Is.EqualTo(HomePvpSettings.Default));
    }

    [Test]
    public void TheFight_GivesPointsBuffsAndRows_TheStopTakesThemBack() {
        Home home = InAHome();
        home.Field.Home.Permissions[HomePermission.Potion] = HomePermissionSetting.Self;
        Dialog(home.Owner, 1, new HomePvpSettings(0, 2, 1, 0, 0, 0));
        var match = (HomePvpField) home.Field.PvpZone!;
        home.Owner.TakeSent();
        home.Visitor.TakeSent();
        FieldPlayer owner = home.Owner.Session.Player;
        FieldPlayer visitor = home.Visitor.Session.Player;

        match.Update(home.Field.FieldTick + HomePvpState.CountdownMs - 1);
        Assert.That(match.IsParticipant(owner), Is.False);
        match.Update(home.Field.FieldTick + HomePvpState.CountdownMs);
        Assert.That(match.IsParticipant(owner), Is.True);
        Assert.That(match.Hostile(owner, visitor), Is.True);
        List<byte[]> sent = home.Visitor.TakeSent();
        Assert.That(sent.Count(packet => TestGameSession.Op(packet) == SendOp.Pvp), Is.EqualTo(2));
        Assert.That(owner.Buffs.HasBuff(HomePvpSettings.HealthEffectId, 2), Is.True);
        Assert.That(visitor.Buffs.HasBuff(HomePvpSettings.HealthEffectId, 2), Is.True);
        // The match's rows stop the owner too, and a row change is saved but not shown while it runs.
        Assert.That(HomeRowCheck.Blocks(home.Owner.Session, HomePermission.Pvp), Is.True);
        Assert.That(HomeRowCheck.Blocks(home.Owner.Session, HomePermission.Potion), Is.True);
        Assert.That(HomeRowCheck.Blocks(home.Owner.Session, HomePermission.Revival), Is.False);

        // The owner's console ends it.
        Assert.That(home.Field.StopPvp(), Is.True);
        Assert.That(home.Field.PvpZone, Is.Null);
        Assert.That(match.Ended, Is.True);
        Assert.That(owner.Buffs.HasBuff(HomePvpSettings.HealthEffectId, 2), Is.False);
        Assert.That(HomeRowCheck.Blocks(home.Owner.Session, HomePermission.Pvp), Is.False);
        Assert.That(HomeRowCheck.Blocks(home.Owner.Session, HomePermission.Potion), Is.False);
        Assert.That(HomeRowCheck.Blocks(home.Visitor.Session, HomePermission.Potion), Is.True);
        // The points back to 0 and the home's own potion row shown again.
        sent = home.Visitor.TakeSent();
        Assert.That(sent.Count(packet => TestGameSession.Op(packet) == SendOp.Pvp), Is.EqualTo(2));
        Assert.That(sent.Count(packet => TestGameSession.Op(packet) == SendOp.ResponseCube), Is.EqualTo(3));
        Assert.That(home.Field.StopPvp(), Is.False);
    }

    [Test]
    public void AloneInTheHome_TheMatchIsCalledOff() {
        Home home = InAHome();
        home.Field.Players.TryRemove(home.Visitor.Session.Player.ObjectId, out _);
        Dialog(home.Owner, 1, HomePvpSettings.Default);
        var match = (HomePvpField) home.Field.PvpZone!;
        match.Update(home.Field.FieldTick + HomePvpState.CountdownMs);
        Assert.That(home.Field.PvpZone, Is.Null);
        Assert.That(HomeRowCheck.Blocks(home.Owner.Session, HomePermission.Pvp), Is.False);
    }

    [Test]
    public void SurvivalDeath_EndsTheMatch() {
        Home home = InAHome();
        Dialog(home.Owner, 1, new HomePvpSettings(0, 0, 0, 0, 1, 0));
        var match = (HomePvpField) home.Field.PvpZone!;
        match.Update(home.Field.FieldTick + HomePvpState.CountdownMs);
        FieldPlayer owner = home.Owner.Session.Player;
        FieldPlayer visitor = home.Visitor.Session.Player;
        home.Visitor.TakeSent();

        match.Damage(owner, visitor, 10);
        match.Death(visitor);
        Assert.That(home.Visitor.TakeSentOps(), Does.Contain(SendOp.PlayerKillNotice));
        Assert.That(visitor.Buffs.HasBuff(HomePvpSettings.DefaultEffectId, 1), Is.False);
        match.Update(home.Field.FieldTick + HomePvpState.CountdownMs + 1);
        Assert.That(home.Field.PvpZone, Is.Null);
    }
}
