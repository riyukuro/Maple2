using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Pvp;

// A pk field's open PvP (PC::vfunc_122, pc.cpp:3112): on a field whose pkMode is set another player is hostile unless
// it is the player itself, both share a party, or either holds a pvpOnOff buff (70000032 the shadow world's move
// protection, 70000029). The rule's vetoes also hold over a field game on a pk field (the survival maps under KR).
public class PkFieldTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Map = 3000059;
    private const int Protection = 70000032;
    private const int KillTrophy = 21300001;
    private const int DieTrophy = 21300002;

    private sealed class Scene {
        public required FieldManager Field;
        public required TestGameSession TestA;
        public required TestGameSession TestB;
        public FieldPlayer A => TestA.Session.Player;
        public FieldPlayer B => TestB.Session.Player;
    }

    // Two players in one field of the map; pk sets its pkMode. Both have the shadow world kill and die trophies.
    private static Scene Start(bool pk) {
        AchievementMetadata[] trophies = [
            TestGameSession.Trophy(KillTrophy, ConditionType.shadow_world_kill, [1000]),
            TestGameSession.Trophy(DieTrophy, ConditionType.shadow_world_die, [1000]),
        ];
        var a = new TestGameSession();
        a.AddAchievement(trophies);
        a.Start();
        var tb = new TestGameSession(a.Options);
        tb.AddAchievement(trophies);
        tb.Start();
        MapMetadata map = TestGameSession.Map(Map);
        a.EnterMap(map with { Property = map.Property with { PkMode = pk } });
        FieldManager field = a.Session.Field!;
        typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(tb.Session.Player, 2);
        field.Players[a.Session.Player.ObjectId] = a.Session.Player;
        field.Players[2] = tb.Session.Player;
        tb.Session.Field = field;
        tb.Session.Player.Position = new Vector3(100, 0, 0);
        foreach (TestGameSession test in new[] { a, tb }) {
            typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, field);
            // The login sets the party manager (no party here) before the player enters a field.
            test.Session.Party = new PartyManager(test.World, test.Session);
            test.TakeSent();
        }
        return new Scene { Field = field, TestA = a, TestB = tb };
    }

    // The buff on the holder, as the field keeps it, and the holder's sums taken again.
    private static void Give(FieldPlayer holder, int id, bool pvpOnOff, bool enabled = true) {
        Buff buff = DisabledBuffTests.Held(DisabledBuffTests.Effect(id, property: DisabledBuffTests.Property with { PvpOnOff = pvpOnOff }), enabled);
        typeof(Buff).GetField("<Caster>k__BackingField", Any)!.SetValue(buff, holder);
        typeof(Buff).GetField("<Owner>k__BackingField", Any)!.SetValue(buff, holder);
        var held = (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", Any)!.GetValue(holder.Buffs)!;
        held[id] = [buff];
        holder.Buffs.EnabledChanged(buff);
    }

    private static void Join(FieldPlayer player, int partyId) {
        var party = new Party(partyId, 1, 1, "leader");
        typeof(PartyManager).GetField("<Party>k__BackingField", Any)!.SetValue(player.Session.Party, party);
    }

    [Test]
    public void Rule_SelfPartyAndPvpOnOff() {
        Assert.Multiple(() => {
            Assert.That(PkFieldRules.Hostile(false, 0, 0, false, false), Is.True);
            Assert.That(PkFieldRules.Hostile(true, 0, 0, false, false), Is.False);
            Assert.That(PkFieldRules.Hostile(false, 5, 5, false, false), Is.False);
            Assert.That(PkFieldRules.Hostile(false, 5, 6, false, false), Is.True);
            Assert.That(PkFieldRules.Hostile(false, 0, 0, true, false), Is.False);
            Assert.That(PkFieldRules.Hostile(false, 0, 0, false, true), Is.False);
        });
    }

    // The sums count an enabled pvpOnOff buff; a disabled one, or a buff without the flag, gives none.
    [Test]
    public void Sums_PvpOnOffOfEnabledBuffs() {
        AdditionalEffectMetadata on = DisabledBuffTests.Effect(Protection, property: DisabledBuffTests.Property with { PvpOnOff = true });
        AdditionalEffectMetadata off = DisabledBuffTests.Effect(1);
        Assert.Multiple(() => {
            Assert.That(BuffManager.SumsOf([DisabledBuffTests.Held(on, true), DisabledBuffTests.Held(off, true)]).PvpOnOff, Is.True);
            Assert.That(BuffManager.SumsOf([DisabledBuffTests.Held(on, false)]).PvpOnOff, Is.False);
            Assert.That(BuffManager.SumsOf([DisabledBuffTests.Held(off, true)]).PvpOnOff, Is.False);
        });
    }

    [Test]
    public void PkField_OthersHostileBothWays() {
        Scene scene = Start(pk: true);
        Assert.Multiple(() => {
            Assert.That(scene.Field.PlayerHostile(scene.A, scene.B), Is.True);
            Assert.That(scene.Field.PlayerHostile(scene.B, scene.A), Is.True);
            Assert.That(scene.Field.PlayerHostile(scene.A, scene.A), Is.False);
        });
    }

    [Test]
    public void PlainField_NotHostile() {
        Scene scene = Start(pk: false);
        Assert.That(scene.Field.PlayerHostile(scene.A, scene.B), Is.False);
    }

    [Test]
    public void PkField_PartyMembersNotHostile() {
        Scene scene = Start(pk: true);
        Join(scene.A, 7);
        Join(scene.B, 7);
        Assert.That(scene.Field.PlayerHostile(scene.A, scene.B), Is.False);
        Join(scene.B, 8);
        Assert.That(scene.Field.PlayerHostile(scene.A, scene.B), Is.True);
    }

    // The holder of a pvpOnOff buff is no one's enemy and has none; the flag ends with the buff.
    [Test]
    public void PkField_PvpOnOffHolderNotHostile() {
        Scene scene = Start(pk: true);
        Give(scene.B, Protection, pvpOnOff: true);
        Assert.Multiple(() => {
            Assert.That(scene.Field.PlayerHostile(scene.A, scene.B), Is.False);
            Assert.That(scene.Field.PlayerHostile(scene.B, scene.A), Is.False);
        });
        Give(scene.B, Protection, pvpOnOff: true, enabled: false);
        Assert.That(scene.Field.PlayerHostile(scene.A, scene.B), Is.True);
    }

    // Under a running field game only the game makes players hostile (chosen here), but on a pk field the client's
    // vetoes still hold: a pvpOnOff holder in the survival game is not hostile.
    [Test]
    public void Survival_GameRuleUnderPkVetoes() {
        Scene scene = Start(pk: true);
        scene.Field.StartSurvival();
        Assume.That(scene.Field.Survival!.Hostile(scene.A, scene.B), Is.True);
        Assert.That(scene.Field.PlayerHostile(scene.A, scene.B), Is.True);
        Give(scene.A, Protection, pvpOnOff: true);
        Assert.Multiple(() => {
            Assert.That(scene.Field.Survival!.Hostile(scene.A, scene.B), Is.True);
            Assert.That(scene.Field.PlayerHostile(scene.A, scene.B), Is.False);
            Assert.That(scene.Field.PlayerHostile(scene.B, scene.A), Is.False);
        });
    }

    [Test]
    public void Survival_PlainFieldKeepsGameRule() {
        Scene scene = Start(pk: false);
        scene.Field.StartSurvival();
        Give(scene.A, Protection, pvpOnOff: true);
        Assert.That(scene.Field.PlayerHostile(scene.A, scene.B), Is.EqualTo(scene.Field.Survival!.Hostile(scene.A, scene.B)));
    }

    // A kill on a pk field: the trophies, the field's kill log and each side's notice.
    [Test]
    public void Kill_TrophiesAndNotices() {
        Scene scene = Start(pk: true);
        string killer = scene.A.Value.Character.Name;
        string victim = scene.B.Value.Character.Name;
        Assert.That(scene.Field.PkKill(scene.A, scene.B), Is.True);
        string log = Convert.ToHexString(PlayerKillNoticePacket.Kill(killer, victim).ToArray());
        string killNotice = Convert.ToHexString(PlayerKillNoticePacket.Notice(new InterfaceText(StringCode.s_shadowworld_kill_log_kill, victim)).ToArray());
        string dieNotice = Convert.ToHexString(PlayerKillNoticePacket.Notice(new InterfaceText(StringCode.s_shadowworld_kill_log_die, killer)).ToArray());
        List<string> sentA = scene.TestA.TakeSent().Select(Convert.ToHexString).ToList();
        List<string> sentB = scene.TestB.TakeSent().Select(Convert.ToHexString).ToList();
        Assert.Multiple(() => {
            Assert.That(log, Does.StartWith(Convert.ToHexString(BitConverter.GetBytes((short) SendOp.PlayerKillNotice)) + "00"));
            Assert.That(killNotice, Does.StartWith(Convert.ToHexString(BitConverter.GetBytes((short) SendOp.PlayerKillNotice)) + "01"));
            Assert.That(sentA, Does.Contain(log).And.Contain(killNotice).And.No.Contain(dieNotice));
            Assert.That(sentB, Does.Contain(log).And.Contain(dieNotice).And.No.Contain(killNotice));
            Assert.That(scene.TestA.TrophyCounter(KillTrophy), Is.EqualTo(1));
            Assert.That(scene.TestA.TrophyCounter(DieTrophy), Is.Null);
            Assert.That(scene.TestB.TrophyCounter(DieTrophy), Is.EqualTo(1));
            Assert.That(scene.TestB.TrophyCounter(KillTrophy), Is.Null);
        });
    }

    [Test]
    public void Kill_NoneOffPkOrUnderGame() {
        Scene plain = Start(pk: false);
        Assert.That(plain.Field.PkKill(plain.A, plain.B), Is.False);
        Scene game = Start(pk: true);
        game.Field.StartSurvival();
        game.TestA.TakeSent();
        Assert.Multiple(() => {
            Assert.That(game.Field.PkKill(game.A, game.B), Is.False);
            Assert.That(game.Field.PkKill(game.A, game.A), Is.False);
            Assert.That(game.TestA.TrophyCounter(KillTrophy), Is.Null);
        });
    }
}
