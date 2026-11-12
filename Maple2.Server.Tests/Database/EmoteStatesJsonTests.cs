using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Maple2.Database.Storage;
using Maple2.Model.Game;

namespace Maple2.Server.Tests.Database;

// character-unlock.EmoteStates is a JSON column read with the database's own serializer options (internal,
// reached by reflection). Rows from before the column hold NULL, and a row edited by hand can hold an entry
// without a state.
public class EmoteStatesJsonTests {
    private static readonly JsonSerializerOptions Options = (JsonSerializerOptions) typeof(GameStorage).Assembly
        .GetType("Maple2.Database.Extensions.PropertyBuilderExtensions", true)!
        .GetField("Options", BindingFlags.NonPublic | BindingFlags.Static)!
        .GetValue(null)!;

    private static IDictionary<int, EmoteState>? Read(string json) {
        return JsonSerializer.Deserialize<IDictionary<int, EmoteState>>(json, Options);
    }

    [Test]
    public void RoundTrip() {
        var states = new Dictionary<int, EmoteState> {
            [90200001] = new EmoteState { LearnTime = 1700000000, Favorite = true },
            [90200002] = new EmoteState { LearnTime = 1700000500 },
            [90200003] = new EmoteState { Favorite = true },
        };
        IDictionary<int, EmoteState>? read = Read(JsonSerializer.Serialize<IDictionary<int, EmoteState>>(states, Options));
        Assert.That(read, Is.Not.Null);
        Assert.That(read!.Count, Is.EqualTo(3));
        Assert.That(read[90200001].LearnTime, Is.EqualTo(1700000000));
        Assert.That(read[90200001].Favorite, Is.True);
        Assert.That(read[90200002].LearnTime, Is.EqualTo(1700000500));
        Assert.That(read[90200002].Favorite, Is.False);
        Assert.That(read[90200003].LearnTime, Is.EqualTo(0));
        Assert.That(read[90200003].Favorite, Is.True);
    }

    [Test]
    public void NullColumn_NoStates() {
        Assert.That(Read("null"), Is.Null);
    }

    [Test]
    public void Empty_NoStates() {
        Assert.That(Read("{}"), Is.Empty);
    }

    // A state written with default values only is stored as {} and reads back as learn time 0, no mark.
    [Test]
    public void EmptyState_Defaults() {
        IDictionary<int, EmoteState>? read = Read("{\"90200001\":{}}");
        Assert.That(read![90200001].LearnTime, Is.EqualTo(0));
        Assert.That(read[90200001].Favorite, Is.False);
    }

    // An entry without a state: the favourite mark makes a new state instead of failing, and the list entry
    // goes out without learn time or mark.
    [Test]
    public void NullEntry_CountsAsNoState() {
        IDictionary<int, EmoteState>? read = Read("{\"90200001\":null}");
        Assert.That(read!.ContainsKey(90200001), Is.True);
        Assert.That(read[90200001], Is.Null);

        Emote entry = Emote.Of(90200001, read[90200001]);
        Assert.That(entry.LearnTime, Is.EqualTo(0));
        Assert.That(entry.Favorite, Is.False);
    }
}
