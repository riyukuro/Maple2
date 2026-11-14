using System;
using System.Collections.Generic;
using System.Text.Json;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Game;

// The CustomTriggerString game event: the trigger script texts the client replaces while it runs. The server reads the
// row as the client does and sends the strings in the event list, which the client applies at the event's start.
public class CustomTriggerStringTests {
    // A KR row's shape (581002), the names here made up; a name carries a literal backslash n, as in the data.
    private const string Value1 = "<ms2><key id=\"61000003_ME_003__MAIN__0\" name=\"Q1\\nO or X?\"/>"
        + "<key id=\"61000003_ME_001__MAIN__0\" name=\"\uC624\uB298\uC740\"/></ms2>";

    private static GameEventMetadata Row(int id, GameEventData data) =>
        new(id, GameEventType.CustomTriggerString, DateTime.MinValue, DateTime.MaxValue, TimeSpan.Zero, TimeSpan.Zero, [], data, "", "", "", "");

    [Test]
    public void Reader_KrRow() {
        CustomTriggerString? row = EventXmlReader.CustomTriggerString(Value1);
        Assert.That(row, Is.Not.Null);
        Assert.That(row!.Strings, Is.EqualTo(new Dictionary<string, string> {
            ["61000003_ME_001__MAIN__0"] = "\uC624\uB298\uC740",
            ["61000003_ME_003__MAIN__0"] = "Q1\\nO or X?",
        }));
    }

    // Any element names; an element without an id is skipped; a repeated id keeps its last name.
    [Test]
    public void Reader_AnyNamesSkipsAndLastWins() {
        CustomTriggerString? row = EventXmlReader.CustomTriggerString(
            "<root><a id=\"x\" name=\"1\"/><b name=\"2\"/><c id=\"\" name=\"3\"/><d id=\"x\" name=\"4\"/><e id=\"y\"/></root>");
        Assert.That(row!.Strings, Is.EqualTo(new Dictionary<string, string> { ["x"] = "4", ["y"] = "" }));
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("<ms2")]
    [TestCase("<ms2 />")]
    [TestCase("<ms2><key name=\"a\"/></ms2>")]
    public void Reader_Unusable(string? value1) {
        Assert.That(EventXmlReader.CustomTriggerString(value1), Is.Null);
    }

    // GameEventCustomTriggerString::vfunc_4: the id, then the map: a count and each key and value, keys in order.
    [Test]
    public void Packet() {
        var data = new CustomTriggerString(new Dictionary<string, string> { ["b"] = "\uAC00\\n2", ["B"] = "1", ["a"] = "" });
        var gameEvent = new GameEvent(Row(581002, data));
        Assert.That(gameEvent.SentToClient, Is.True);
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("CustomTriggerString"));
            Assert.That(reader.ReadInt(), Is.EqualTo(581002));
            Assert.That(reader.ReadInt(), Is.EqualTo(3));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("B"));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("1"));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("a"));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo(""));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("b"));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("\uAC00\\n2"));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }

    // The table is stored as json; the strings come back.
    [Test]
    public void JsonRoundTrip() {
        GameEventData data = EventXmlReader.CustomTriggerString(Value1)!;
        GameEventData? back = JsonSerializer.Deserialize<GameEventData>(JsonSerializer.Serialize(data));
        Assert.That(back, Is.TypeOf<CustomTriggerString>());
        Assert.That(((CustomTriggerString) back!).Strings, Is.EqualTo(((CustomTriggerString) data).Strings));
    }
}
