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

    // A map the played client does not have is no return field, whoever asks.
    [Test]
    public void ReturnMap_NotInPlayedClient_IsDefault() {
        Assert.That(FieldEntryRules.ReturnMap(900001, true, 0, true, 900009, notInPlayedClient: true), Is.EqualTo(900009));
        Assert.That(FieldEntryRules.ReturnMap(900001, true, 0, true, 900009, notInPlayedClient: false), Is.EqualTo(900001));
    }

    // Arriving (login, channel change) on a map the played client does not have: the return field, then the
    // default one, whichever the client has; an unmarked map is entered as it is.
    [Test]
    public void ArrivalMap_SkipsMapsThePlayedClientLacks() {
        Assert.That(FieldEntryRules.ArrivalMap(900001, false, 900002, 900009, _ => false), Is.EqualTo(900001));
        Assert.That(FieldEntryRules.ArrivalMap(900001, true, 900002, 900009, _ => true), Is.EqualTo(900002));
        Assert.That(FieldEntryRules.ArrivalMap(900001, true, 900002, 900009, id => id != 900002), Is.EqualTo(900009));
        Assert.That(FieldEntryRules.ArrivalMap(900001, true, 900001, 900009, _ => true), Is.EqualTo(900009), "the return field is the same map");
        Assert.That(FieldEntryRules.ArrivalMap(900001, true, 900002, 900009, _ => false), Is.EqualTo(0), "nothing usable");
    }

    [Test]
    public void NotInPlayedClientNotice_NamesTheMap() {
        Assert.That(FieldEntryRules.NotInPlayedClientNotice(900001, "Test Park"), Is.EqualTo("Map Test Park (900001) is not in this client version. Entry refused."));
        Assert.That(FieldEntryRules.NotInPlayedClientNotice(900001, null), Is.EqualTo("Map 900001 is not in this client version. Entry refused."));
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

    // Guild 100: two guilds that open the same event do not meet in one room.
    [Test]
    public void GuildEventMap_HasOneRoomPerGuild() {
        Assert.That(FieldEntryRules.HasOwnerRoom(InstanceType.GuildEvent, 5, 0), Is.True);
        Assert.That(FieldEntryRules.HasOwnerRoom(InstanceType.GuildEvent, 0, 0), Is.False, "no owner");
        Assert.That(FieldEntryRules.HasOwnerRoom(InstanceType.GuildEvent, 5, 9), Is.False, "a named room");
        Assert.That(FieldEntryRules.HasOwnerRoom(InstanceType.solo, 5, 0), Is.False);
        Assert.That(FieldEntryRules.HasOwnerRoom(InstanceType.GuildHouse, 5, 0), Is.True, "a guild's house");
        Assert.That(FieldEntryRules.HasOwnerRoom(InstanceType.GuildHouse, 0, 0), Is.False);
        Assert.That(FieldEntryRules.HasOwnerRoom(InstanceType.ugcMap, 5, 0), Is.False, "homes have their own path");
    }
}
