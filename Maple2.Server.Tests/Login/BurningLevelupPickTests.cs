using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;

namespace Maple2.Server.Tests.Login;

// The burning levelup pick at the character list (EventReward 16, UICharacterListDialog::SendEventReward): the
// account's values sent with the list, the pick of a character and its refusals.
public class BurningLevelupPickTests {
    private const int EventId = 380010;

    private TestLoginSession test = null!;

    private static BurningLevelup Data(params int[] targets) {
        return new BurningLevelup(9, 70, 2, 90000838, "s_popup", "s_bottom", "s_desc", "s_title", 47, targets,
            [new RewardItem(20303380, 6, 1)], null);
    }

    private LoginSession Start(BurningLevelup? data = null, DateTime? end = null) {
        test = new TestLoginSession();
        test.AddEvent(EventId, GameEventType.BurningLevelup, data ?? Data(), end: end);
        LoginSession session = test.Start();
        session.ListCharacters();
        test.TakeSent();
        return session;
    }

    private void Pick(LoginSession session, long characterId) {
        new EventRewardHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(16);
            writer.WriteLong(characterId);
        }));
    }

    private void SetRow(long characterId, Action<Maple2.Database.Model.Character> change) {
        using var context = new Ms2Context(test.Rows.Options);
        change(context.Character.Find(characterId)!);
        context.SaveChanges();
    }

    private List<GameEventUserValue> Stored() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetAccountEventValues(test.AccountId).Where(value => value.EventId == EventId).ToList();
    }

    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private void SeedValue(GameEventUserValueType type, string value) {
        var stored = new GameEventUserValue(type, DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds(), EventId);
        stored.SetValue(value);
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.SaveAccountEventValues(test.AccountId, [stored]), Is.True);
    }

    [Test]
    public void Pick_StoresTheCharacterUntilTheEventEndsAndAnswersWithItsId() {
        LoginSession session = Start();

        Pick(session, test.CharacterId);

        GameEventUserValue picked = Stored().Single();
        Assert.That((picked.Type, picked.Value), Is.EqualTo((GameEventUserValueType.BurningLevelupCharacter, test.CharacterId.ToString())));
        Assert.That(picked.ExpirationTime, Is.EqualTo(test.Events[EventId].EndTime));
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(GameEventUserValuePacket.Update(picked)),
            Hex(BurningLevelupPacket.Picked(test.CharacterId)),
        }));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void Picked_IsAnEventRewardOf16AndTheId() {
        Assert.That(Hex(BurningLevelupPacket.Picked(0x0102030405060708)), Is.EqualTo("F700" + "10" + "0807060504030201"));
        Assert.That(Hex(BurningLevelupPacket.RewardTaken()), Is.EqualTo("F700" + "0F"));
    }

    [Test]
    public void ListCharacters_SendsTheAccountValuesFirstWhileTheEventRuns() {
        LoginSession session = Start();
        Pick(session, test.CharacterId);
        test.TakeSent();

        session.ListCharacters();

        List<SendOp> ops = test.TakeSentOps();
        Assert.That(ops.First(), Is.EqualTo(SendOp.GameEventUserValue));
        Assert.That(ops.Count(op => op == SendOp.GameEventUserValue), Is.EqualTo(1));
    }

    [Test]
    public void ListCharacters_SendsNoValuesWithoutARunningEvent() {
        test = new TestLoginSession();
        test.AddEvent(EventId, GameEventType.BurningLevelup, Data(), DateTime.Now.AddDays(-10), DateTime.Now.AddDays(-1));
        LoginSession session = test.Start();

        session.ListCharacters();

        Assert.That(test.TakeSentOps(), Does.Not.Contain(SendOp.GameEventUserValue));
    }

    [Test]
    public void Pick_WithoutARunningEvent_IsRefused() {
        test = new TestLoginSession();
        test.AddEvent(EventId, GameEventType.BurningLevelup, Data(), DateTime.Now.AddDays(-10), DateTime.Now.AddDays(-1));
        LoginSession session = test.Start();
        session.ListCharacters();
        test.TakeSent();

        Pick(session, test.CharacterId);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(Stored(), Is.Empty);
    }

    [Test]
    public void SecondPick_IsRefused() {
        LoginSession session = Start();
        long other = test.AddCharacter("Second");
        Pick(session, test.CharacterId);
        test.TakeSent();

        Pick(session, other);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(Stored().Single().Value, Is.EqualTo(test.CharacterId.ToString()));
    }

    [Test]
    public void Pick_AfterTheEventWasCompleted_IsRefused() {
        LoginSession session = Start();
        SeedValue(GameEventUserValueType.BurningLevelupReached, "1");

        Pick(session, test.CharacterId);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(Stored().Any(value => value.Type == GameEventUserValueType.BurningLevelupCharacter), Is.False);
    }

    [Test]
    public void Pick_OfAnotherAccountsCharacter_IsRefused() {
        LoginSession session = Start();
        long foreign = test.AddCharacter("Foreign", test.AccountId + 1000);

        Pick(session, foreign);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(Stored(), Is.Empty);
    }

    [Test]
    public void Pick_OfACharacterPendingDelete_IsRefused() {
        LoginSession session = Start();
        SetRow(test.CharacterId, row => row.DeleteTime = DateTime.Now.AddDays(1));

        Pick(session, test.CharacterId);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(Stored(), Is.Empty);
    }

    [TestCase((short) 70)]
    [TestCase((short) 80)]
    public void Pick_AtOrPastTheEndLevel_IsRefused(short level) {
        LoginSession session = Start();
        SetRow(test.CharacterId, row => row.Level = level);

        Pick(session, test.CharacterId);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(Stored(), Is.Empty);
    }

    [Test]
    public void Pick_OfAJobTheEventDoesNotTake_IsRefused() {
        LoginSession session = Start(Data((int) JobCode.Knight));

        Pick(session, test.CharacterId);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(Stored(), Is.Empty);

        SetRow(test.CharacterId, row => row.Job = Job.Knight);
        Pick(session, test.CharacterId);
        Assert.That(Stored().Single().Value, Is.EqualTo(test.CharacterId.ToString()));
    }
}
