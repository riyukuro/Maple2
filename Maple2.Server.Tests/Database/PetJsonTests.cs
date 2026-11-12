using System;
using System.Reflection;
using System.Text.Json;
using Maple2.Database.Storage;
using Maple2.Model.Game;

namespace Maple2.Server.Tests.Database;

// The pet data is stored in item.SubType ({"!": "pet", ...}). The client shows the level the item stream carries
// (CItem::ReadFromStream: name, exp, evolve points, level), and that is the stored Level: a row without an Exp
// key (as a hand-edited one) loads its Level too.
public class PetJsonTests {
    private static readonly Assembly Database = typeof(GameStorage).Assembly;
    private static readonly JsonSerializerOptions Options = (JsonSerializerOptions) Database
        .GetType("Maple2.Database.Extensions.PropertyBuilderExtensions", true)!
        .GetField("Options", BindingFlags.NonPublic | BindingFlags.Static)!
        .GetValue(null)!;
    private static readonly Type SubType = Database.GetType("Maple2.Database.Model.ItemSubType", true)!;
    private static readonly Type Record = Database.GetType("Maple2.Database.Model.ItemPet", true)!;

    private static ItemPet Load(string json) {
        object loaded = JsonSerializer.Deserialize(json, SubType, Options)!;
        Assert.That(loaded.GetType(), Is.EqualTo(Record));
        foreach (MethodInfo method in Record.GetMethods(BindingFlags.Public | BindingFlags.Static)) {
            if (method.Name == "op_Implicit" && method.ReturnType == typeof(ItemPet)) {
                return (ItemPet) method.Invoke(null, [loaded])!;
            }
        }
        throw new InvalidOperationException("no conversion");
    }

    [TestCase("{\"!\": \"pet\", \"Exp\": 212, \"Name\": \"\", \"Level\": 5, \"RenameRemaining\": 1}", 212)]
    [TestCase("{\"!\": \"pet\", \"Name\": \"\", \"Level\": 5, \"RenameRemaining\": 1}", 0)]
    public void StoredLevel_IsLoaded(string json, long exp) {
        ItemPet pet = Load(json);
        Assert.That((pet.Level, pet.Exp), Is.EqualTo(((short) 5, exp)));
    }
}
