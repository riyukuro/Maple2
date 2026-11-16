using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Microsoft.EntityFrameworkCore;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// HideAndSeekField in a test field with two players: the seekers' misses (a landing near a hider costs nothing, a miss
// costs a life, the last life takes the seek skills and shows the watch dialog), the banner to one team, the players
// arriving before and after the split and after the end.
public class HideAndSeekFieldTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class Game {
        public required TestGameSession A;
        public required TestGameSession B;
        public required FieldManager Field;
        public required HideAndSeekField Rules;
        public required HideAndSeekState State;
        public required TestGameSession SeekerTest;
        public required TestGameSession HiderTest;
        public FieldPlayer Seeker => SeekerTest.Session.Player;
        public FieldPlayer Hider => HiderTest.Session.Player;
    }

    private static Game Start(HideAndSeekStep until) {
        var a = new TestGameSession();
        a.SetServerTable(new HideAndSeekTable(new Dictionary<int, HideAndSeekTable.Field>()));
        a.Start();
        var b = new TestGameSession(a.Options);
        b.SetServerTable(new HideAndSeekTable(new Dictionary<int, HideAndSeekTable.Field>()));
        b.Start();
        FieldManager field = a.Session.Field!;
        typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(b.Session.Player, 2);
        field.Players[2] = b.Session.Player;
        b.Session.Field = field;
        a.Session.Ride = new Maple2.Server.Game.Manager.RideManager(a.Session);
        b.Session.Ride = new Maple2.Server.Game.Manager.RideManager(b.Session);
        FieldInfo userValues = typeof(FieldManager).GetField(nameof(FieldManager.UserValues))!;
        if (userValues.GetValue(field) == null) {
            userValues.SetValue(field, new TriggerUserValues());
        }
        FieldInfo portals = typeof(FieldManager).GetField("fieldPortals", Any)!;
        if (portals.GetValue(field) == null) {
            portals.SetValue(field, new ConcurrentDictionary<int, FieldPortal>());
        }
        // The seek skill in the field's skill store (level 1, const 2500, range 750), as the ingest has it.
        var attack = (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            CompulsionTypes = [],
            Range = (SkillMetadataRange) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataRange)) with { Distance = 750 },
            Damage = new SkillMetadataDamage(Count: 1, Rate: 0, HitSpeed: 0, HitDelay: 0, IsConstDamage: true, Value: 2500,
                DamageByTargetMaxHp: 0, SuperArmorBreak: false, Push: null),
        };
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        var level = (SkillMetadataLevel) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataLevel)) with {
            Motions = [new SkillMetadataMotion(null!, [attack], new Dictionary<string, byte>())],
        };
        var skill = new SkillMetadata(HideAndSeekField.SeekSkillId, 1, null, property, null!, level);
        var skills = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).BaseType!.GetField("Cache", Any)!.GetValue(skills)!;
        cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(HideAndSeekField.SeekSkillId, (short) 1), skill]);
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", Any)!.SetValue(field, skills);
        field.StartHideAndSeek(1);
        HideAndSeekField rules = field.HideAndSeek!;
        rules.Constant("RequireUserCount", "2", "ALL");
        rules.Constant("SeekTeamSkillSetID", "99930041", "ALL");
        rules.Constant("SeekTeamInvincibleTick", "0", "ALL");
        rules.Constant("InvincibleMessage", "x", "ALL");
        for (int i = 0; i < 20 && rules.Step != until; i++) {
            rules.Update(field.FieldTick);
        }
        Assert.That(rules.Step, Is.EqualTo(until));
        var state = (HideAndSeekState) typeof(HideAndSeekField).GetField("state", Any)!.GetValue(rules)!;
        bool aSeeks = state.TeamOf(a.CharacterId) == HideAndSeekState.SeekTeam;
        return new Game {
            A = a, B = b, Field = field, Rules = rules, State = state,
            SeekerTest = aSeeks ? a : b, HiderTest = aSeeks ? b : a,
        };
    }

    private static List<byte[]> HideAndSeek(TestGameSession test) =>
        test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.HideAndSeek).ToList();

    [Test]
    public void Landed_NearAHiderCostsNothingAMissCostsALife() {
        Game game = Start(HideAndSeekStep.LookingForATeams);
        game.State.Hide(game.Hider.Value.Character.Id, 99920001, 0, 5000);
        long id = game.Seeker.Value.Character.Id;
        game.A.TakeSent();
        game.B.TakeSent();

        // Seeker and hider stand on the same spot: the landing is near the hider.
        game.Rules.Landed(game.Seeker, game.Seeker.Position);
        Assert.That(game.State.Lives(id), Is.EqualTo(HideAndSeekState.SeekerLives));
        Assert.That(HideAndSeek(game.SeekerTest), Is.Empty);

        // The hider is far from the landing: a life is lost and the seeker is told.
        game.Hider.Position = new Vector3(100_000, 0, 0);
        game.Rules.Landed(game.Seeker, game.Seeker.Position);
        Assert.That(game.State.Lives(id), Is.EqualTo(HideAndSeekState.SeekerLives - 1));
        Assert.That(HideAndSeek(game.SeekerTest).Any(packet => packet.SequenceEqual(
            HideAndSeekPacket.Life(game.Seeker.ObjectId, HideAndSeekState.SeekerLives - 1, HideAndSeekState.SeekerLives).ToArray())), Is.True);

        // The last life: the seek skills go and the watch dialog opens.
        game.Rules.Landed(game.Seeker, game.Seeker.Position);
        game.A.TakeSent();
        game.B.TakeSent();
        game.Rules.Landed(game.Seeker, game.Seeker.Position);
        List<byte[]> sent = HideAndSeek(game.SeekerTest);
        Assert.That(game.State.Lives(id), Is.Zero);
        Assert.That(sent.Any(packet => packet.SequenceEqual(HideAndSeekPacket.RemoveSkillSet(game.Seeker.ObjectId, 99930041).ToArray())), Is.True);
        Assert.That(sent.Any(packet => packet.SequenceEqual(HideAndSeekPacket.Watch(game.Seeker.ObjectId, 0, true, false).ToArray())), Is.True);
    }

    [Test]
    public void Message_GoesToOneTeamOnly() {
        Game game = Start(HideAndSeekStep.LookingForATeams);
        game.A.TakeSent();
        game.B.TakeSent();
        game.Rules.Message(HideAndSeekState.SeekTeam, "hello", 3000);
        Assert.That(game.SeekerTest.TakeSent().Any(packet => TestGameSession.Op(packet) == SendOp.MassiveEvent), Is.True);
        Assert.That(game.HiderTest.TakeSent().Any(packet => TestGameSession.Op(packet) == SendOp.MassiveEvent), Is.False);
    }

    [Test]
    public void Arrive_BeforeTheSplitNothingAfterItTheGameInfo() {
        Game game = Start(HideAndSeekStep.WaitForEnterUser);
        game.A.TakeSent();
        game.Rules.Arrive(game.A.Session.Player);
        Assert.That(game.A.TakeSent(), Is.Empty);

        Game running = Start(HideAndSeekStep.LookingForATeams);
        running.A.TakeSent();
        running.B.TakeSent();
        running.Rules.Arrive(running.Hider);
        List<byte[]> sent = HideAndSeek(running.HiderTest);
        // The other player's game info, the hidden props, and (a hider, not caught) no watch dialog.
        Assert.That(sent, Is.Not.Empty);
    }

    [Test]
    public void Arrive_AfterTheEndTheLatePlayerIsQueuedBackToItsField() {
        Game game = Start(HideAndSeekStep.LookingForATeams);
        game.Rules.Leave(game.Hider);
        Assert.That(game.Rules.Ended || game.Rules.Step == HideAndSeekStep.TeamMatchResult, Is.True);
        for (int i = 0; i < 20 && !game.Rules.Ended; i++) {
            game.Rules.Update(game.Field.FieldTick + 1_000_000L * (i + 1));
        }
        Assert.That(game.Rules.Ended, Is.True);
        int queued = game.Field.Scheduler.Queued;
        game.Rules.Arrive(game.Seeker);
        Assert.That(game.Field.Scheduler.Queued, Is.EqualTo(queued + 1));
    }

    [Test]
    public void Close_GivesBackWhatTheGameGave() {
        Game game = Start(HideAndSeekStep.LookingForATeams);
        game.A.TakeSent();
        game.B.TakeSent();
        game.Rules.Close();
        List<byte[]> sent = HideAndSeek(game.SeekerTest);
        Assert.That(sent.Any(packet => packet.SequenceEqual(HideAndSeekPacket.RemoveSkillSet(game.Seeker.ObjectId, 99930041).ToArray())), Is.True);
        Assert.That(sent.Any(packet => packet.SequenceEqual(HideAndSeekPacket.End(game.Seeker.ObjectId).ToArray())), Is.True);
    }

    // The result pays a team player its constants' reward item (the loser's one for a game that ended by a leave) and
    // the result dialog shows it; an item the data does not have pays nothing and shows none.
    [TestCase(40100023, 4, true)]
    [TestCase(40199999, 0, false)]
    public void Result_PaysTheRewardItem(int itemId, int count, bool known) {
        Game game = Start(HideAndSeekStep.LookingForATeams);
        if (known) {
            // The field makes the item from the first session's store; the seeker may be either.
            game.A.Register(TestGameSession.Metadata(itemId, slotMax: 9999));
            game.B.Register(TestGameSession.Metadata(itemId, slotMax: 9999));
        }
        game.Rules.Constant("RequireUserCount", "1", "ALL");
        game.Rules.Constant("LoserRewardItemID", itemId.ToString(), "ALL");
        game.Rules.Constant("LoserRewardItemCount", "4", "ALL");
        game.SeekerTest.TakeSent();
        game.Rules.Leave(game.Hider);
        Assert.That(game.Rules.Step, Is.EqualTo(HideAndSeekStep.TeamMatchResult));
        byte[] result = HideAndSeekPacket.Result(game.Seeker.ObjectId, HideAndSeekField.ResultRound, known ? itemId : 0, count).ToArray();
        List<byte[]> got = HideAndSeek(game.SeekerTest);
        Assert.That(got.Any(packet => packet.SequenceEqual(result)), Is.True, string.Join(" ", got.Select(Convert.ToHexString)) + " vs " + Convert.ToHexString(result));
    }
}
