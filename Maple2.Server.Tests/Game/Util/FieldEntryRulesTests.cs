using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// Login into a saved field that cannot be made (no navmesh file): which fields are tried instead.
public class FieldEntryRulesTests {
    [Test]
    public void ReturnField_ThenDefault() {
        Assert.That(FieldEntryRules.Fallbacks(2000064, 2000001, 2000062).ToArray(), Is.EqualTo(new[] { 2000001, 2000062 }));
    }

    [Test]
    public void FailedFieldAndRepeats_AreLeftOut() {
        Assert.That(FieldEntryRules.Fallbacks(2000064, 2000064, 2000062).ToArray(), Is.EqualTo(new[] { 2000062 }));
        Assert.That(FieldEntryRules.Fallbacks(2000064, 2000062, 2000062).ToArray(), Is.EqualTo(new[] { 2000062 }));
        Assert.That(FieldEntryRules.Fallbacks(2000062, 2000062, 2000062).ToArray(), Is.Empty);
        Assert.That(FieldEntryRules.Fallbacks(2000064, 0, 2000062).ToArray(), Is.EqualTo(new[] { 2000062 }));
    }

    [Test]
    public void ReturnMap_GmOnlyFieldForPlainAccount_IsDefault() {
        Assert.That(FieldEntryRules.ReturnMap(2000064, true, 50, false, 2000062), Is.EqualTo(2000062));
        Assert.That(FieldEntryRules.ReturnMap(2000064, true, 50, true, 2000062), Is.EqualTo(2000064));
        Assert.That(FieldEntryRules.ReturnMap(2000001, true, 0, false, 2000062), Is.EqualTo(2000001));
    }

    [Test]
    public void ReturnMap_UnknownField_IsDefault() {
        Assert.That(FieldEntryRules.ReturnMap(123, false, 0, true, 2000062), Is.EqualTo(2000062));
    }

    // "Enter Dungeon" on the party panel after the room was closed: the client gets error 0x1D on GameToGame
    // (a message box, it stays in the field) and is not told to move.
    [Test]
    public void DungeonRoomGone_IsError1D() {
        Assert.That((byte) FieldEntryRules.DungeonRoomError(false), Is.EqualTo(0x1D));
        Assert.That(FieldEntryRules.DungeonRoomError(true), Is.EqualTo(MigrationError.ok));
        Assert.That(MigrationPacket.GameToGameError(FieldEntryRules.DungeonRoomError(false)).ToArray(),
            Is.EqualTo(new byte[] { (byte) SendOp.GameToGame, 0x00, 0x1D }));
    }

    // A dungeon arrival whose room is gone enters the return field; every other arrival is left alone.
    [Test]
    public void DungeonArrival_RoomGone_EntersReturnField() {
        Assert.That(FieldEntryRules.ArrivesInReturnField(MigrationType.Dungeon, false), Is.True);
        Assert.That(FieldEntryRules.ArrivesInReturnField(MigrationType.Dungeon, true), Is.False);
        Assert.That(FieldEntryRules.ArrivesInReturnField(MigrationType.Normal, false), Is.False);
    }

    // A session whose character was never loaded (an arrival with a used token) has an empty log name
    // instead of a null reference.
    [Test]
    public void LogName_WithoutCharacter_IsEmpty() {
        Assert.That(FieldEntryRules.LogName(null), Is.EqualTo(string.Empty));
        Assert.That(FieldEntryRules.LogName("abc"), Is.EqualTo("abc"));
    }

    // A stale room id on one member's session must not clear the dungeon the party has now.
    [Test]
    public void OnlyThePartysOwnRoomClearsThePartyDungeon() {
        Assert.That(FieldEntryRules.ClearsPartyDungeon(7, 7), Is.True);
        Assert.That(FieldEntryRules.ClearsPartyDungeon(7, 9), Is.False);
        Assert.That(FieldEntryRules.ClearsPartyDungeon(7, 0), Is.False);
        Assert.That(FieldEntryRules.ClearsPartyDungeon(0, 0), Is.False);
    }
}
