using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
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
    [TestCase(StringCode.s_ugcmap_fun_roll, 3229)]
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

    // Every enum value is in the map: the client's last string id is 3425 (s_empty_string to the end of
    // commonstringtable.cpp), and a value past the map's end would go out as its raw enum id.
    [Test]
    public void EveryCode_IsMapped() {
        foreach (StringCode code in System.Enum.GetValues<StringCode>()) {
            Assert.That(StringCodeMap.ToClient(code), Is.InRange(0, 3425), code.ToString());
        }
    }

    // Two names never share a client id (0 is the empty string the missing names share).
    [Test]
    public void NoTwoCodes_ShareAClientId() {
        var seen = new System.Collections.Generic.Dictionary<int, StringCode>();
        foreach (StringCode code in System.Enum.GetValues<StringCode>().Distinct()) {
            int id = StringCodeMap.ToClient(code);
            if (id == 0) {
                continue;
            }
            Assert.That(seen.TryAdd(id, code), Is.True, $"{code} and {(seen.TryGetValue(id, out StringCode other) ? other : code)} both map to {id}");
        }
    }

    // The home dice roll (HomeAction 0x0b) carries the client's id of s_ugcmap_fun_roll, then the name and the number.
    [Test]
    public void HomeRoll_WritesTheClientIndex() {
        var character = new Character { Id = 8, AccountId = 7, Name = "roller", Mastery = new Mastery() };
        var reader = new ByteReader(HomeActionPacket.Roll(character, 42).ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.HomeAction));
        reader.ReadByte();
        Assert.That(reader.ReadLong(), Is.EqualTo(8)); // the character id, the client's block-list key
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("roller"));
        reader.ReadByte();
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(3229));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("roller"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("42"));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
