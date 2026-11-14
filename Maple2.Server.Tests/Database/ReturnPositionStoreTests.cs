using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Maple2.Database.Storage;
using Maple2.Model.Game;

namespace Maple2.Server.Tests.Database;

// Character.ReturnPosition (the spot on the return field a dungeon hall or a match goes back to) is stored with the
// character in the profile JSON column: a move to another server reloads the character from the database.
public class ReturnPositionStoreTests {
    private static readonly JsonSerializerOptions Options = (JsonSerializerOptions) typeof(GameStorage).Assembly
        .GetType("Maple2.Database.Extensions.PropertyBuilderExtensions", true)!
        .GetField("Options", BindingFlags.NonPublic | BindingFlags.Static)!
        .GetValue(null)!;

    [Test]
    public void RoundTrip_KeepsTheReturnPosition() {
        var character = new Character { AccountId = 1, Id = 2, Name = "Return", Mastery = new Mastery(), ReturnPosition = new Vector3(1.5f, -2f, 300f) };
        character.ReturnMaps.Push(2000062);

        Maple2.Database.Model.Character stored = character;
        string json = JsonSerializer.Serialize(stored.Profile, Options);
        stored.Profile = JsonSerializer.Deserialize<Maple2.Database.Model.Profile>(json, Options)!;
        Character loaded = stored;

        Assert.That(loaded.ReturnPosition, Is.EqualTo(new Vector3(1.5f, -2f, 300f)));
        Assert.That(loaded.ReturnMaps.Peek(), Is.EqualTo(2000062));
    }

    // A profile written before the fields has none of them: the spot reads as 0 0 0 (none, the field's spawn).
    [Test]
    public void OldProfile_HasNoReturnPosition() {
        var profile = JsonSerializer.Deserialize<Maple2.Database.Model.Profile>("{\"Motto\":\"\",\"Picture\":\"\",\"Title\":0}", Options)!;
        Assert.That(new Vector3(profile.ReturnX, profile.ReturnY, profile.ReturnZ), Is.EqualTo(Vector3.Zero));
    }
}
