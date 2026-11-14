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
using Microsoft.EntityFrameworkCore;

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

    #region Delete
    private void Delete(LoginSession session, long characterId) {
        var words = new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var handler = new CharacterManagementHandler {
            World = test.World,
            GameStorage = test.Storage,
            BanWordStorage = new BanWordStorage(words),
            ItemMetadata = null!,
            TableMetadata = test.Rows.TableMetadata,
            ServerTableMetadata = test.Rows.ServerTableMetadata,
        };
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteLong(characterId);
        }));
    }

    private static BurningLevelup JumpingData() {
        return Data() with { Jumping = new BurningLevelup.JumpingOption(new RewardItem(20001322, 1, 3), 30, 2000062, "a", "b", "c") };
    }

    // A picked character whose delete wait has passed, with its jumping item given (2003).
    private (LoginSession Session, long CharacterId) PickedForDelete(BurningLevelup? data = null) {
        LoginSession session = Start(data ?? JumpingData());
        long characterId = test.AddCharacter("Gone");
        SetRow(characterId, row => row.DeleteTime = DateTime.UnixEpoch.AddSeconds(1));
        SeedValue(GameEventUserValueType.BurningLevelupCharacter, characterId.ToString());
        SeedValue(GameEventUserValueType.BurningLevelupJumpingNotify, "1");
        test.TakeSent();
        return (session, characterId);
    }

    private string? StoredValue(GameEventUserValueType type) => Stored().FirstOrDefault(value => value.Type == type)?.Value;

    // The set-character popup's text: one burning character per account, changed only when it is deleted. Its delete
    // frees the pick (2000) and the jumping item notice (2003), so the popup offers the event to the next character
    // and that one gets the item; both Updates follow the delete reply.
    [Test]
    public void Delete_OfThePickedCharacter_FreesThePick() {
        (LoginSession session, long characterId) = PickedForDelete();

        Delete(session, characterId);

        Assert.That(StoredValue(GameEventUserValueType.BurningLevelupCharacter), Is.EqualTo("0"));
        Assert.That(StoredValue(GameEventUserValueType.BurningLevelupJumpingNotify), Is.EqualTo("0"));
        List<SendOp> ops = test.TakeSentOps();
        Assert.That(ops, Is.EqualTo(new[] { SendOp.CharacterList, SendOp.GameEventUserValue, SendOp.GameEventUserValue }));
        Assert.That(test.Errors, Is.Empty);

        Pick(session, test.CharacterId);
        Assert.That(StoredValue(GameEventUserValueType.BurningLevelupCharacter), Is.EqualTo(test.CharacterId.ToString()));
    }

    // Completed (2001), or the jumping done (2004, the jumping item's text: no other pick after the jump even after a
    // delete): the pick stays.
    [TestCase(GameEventUserValueType.BurningLevelupReached)]
    [TestCase(GameEventUserValueType.BurningLevelupJumpingDone)]
    public void Delete_AfterTheEndOrTheJump_KeepsThePick(GameEventUserValueType type) {
        (LoginSession session, long characterId) = PickedForDelete();
        SeedValue(type, "1");

        Delete(session, characterId);

        Assert.That(StoredValue(GameEventUserValueType.BurningLevelupCharacter), Is.EqualTo(characterId.ToString()));
        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.CharacterList }));
    }

    [Test]
    public void Delete_OfAnotherCharacter_KeepsThePick() {
        (LoginSession session, long characterId) = PickedForDelete();
        SeedValue(GameEventUserValueType.BurningLevelupCharacter, test.CharacterId.ToString());

        Delete(session, characterId);

        Assert.That(StoredValue(GameEventUserValueType.BurningLevelupCharacter), Is.EqualTo(test.CharacterId.ToString()));
        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.CharacterList }));
    }
    #endregion
}
