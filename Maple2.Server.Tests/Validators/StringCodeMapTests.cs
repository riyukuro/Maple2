using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Validators;

// The server's StringCode enum is not the client's string index: from s_err_invalid_item (74) on, most names sit one
// or more ids below where the KMS2 client's common string table (CommonString m_strings) has them. StringCodeMap
// turns the enum into the client's index by name; these ids are read from the client's table.
public class StringCodeMapTests {
    [TestCase(StringCode.s_empty_string, 0)]
    [TestCase(StringCode.s_err_input_whisper_target, 78)]
    [TestCase(StringCode.s_item_err_twohand, 214)]
    [TestCase(StringCode.s_item_err_drop, 215)]
    [TestCase(StringCode.s_item_err_cannot_drop, 216)]
    [TestCase(StringCode.s_item_err_invalid_count, 235)]
    [TestCase(StringCode.s_ban_check_err_any, 2234)]
    // A name the client's table does not have shows the empty string.
    [TestCase(StringCode.s_item_opt_sa_improve_acquire_potion, 0)]
    public void ToClient(StringCode code, int client) {
        Assert.That(StringCodeMap.ToClient(code), Is.EqualTo(client));
    }

    // A localized text goes out with the client's index, not the enum value.
    [Test]
    public void InterfaceText_WritesTheClientIndex() {
        var writer = new ByteWriter();
        writer.WriteClass<InterfaceText>(new InterfaceText(StringCode.s_item_err_drop));
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadBool(), Is.True);
        reader.ReadInt();
        Assert.That(reader.ReadInt(), Is.EqualTo(215));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
    }
}
