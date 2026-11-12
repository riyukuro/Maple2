using System.Linq;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class EmotePacketTests {
    private const int Greet = 90200011; // a starting emote
    private const int Learned = 90200999;

    // Client OnEmote mode 0: per entry i32 id, i32 level, bool permanent, i64 expiry, i64 learn time, bool favourite.
    [Test]
    public void Load_WritesTheClientsEntry() {
        var unlock = new Unlock();
        unlock.Emotes.Add(Greet);
        unlock.LearnEmote(Learned, 1700000000);
        unlock.SetEmoteFavorite(Learned, true);

        ByteWriter writer = EmotePacket.Load(unlock.Emotes.Select(unlock.GetEmote).ToList());
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Emote));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));

        Assert.That(reader.ReadInt(), Is.EqualTo(Greet));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadBool(), Is.True);            // a starting emote is permanent
        Assert.That(reader.ReadLong(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(0));
        Assert.That(reader.ReadBool(), Is.False);

        Assert.That(reader.ReadInt(), Is.EqualTo(Learned));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.ReadLong(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(1700000000));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4 + 2 * 26));
    }

    // Client OnEmote mode 1: i32 id, i32 level, i64 expiry, i64 learn time.
    [Test]
    public void Learn_CarriesTheLearnTime() {
        var unlock = new Unlock();
        Assert.That(unlock.LearnEmote(Learned, 1700000000), Is.True);

        ByteWriter writer = EmotePacket.Learn(unlock.GetEmote(Learned));
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Emote));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(Learned));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(1700000000));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4 + 4 + 8 + 8));
    }

    [Test]
    public void LearnEmote_Twice_IsRefused() {
        var unlock = new Unlock();
        Assert.That(unlock.LearnEmote(Learned, 100), Is.True);
        Assert.That(unlock.LearnEmote(Learned, 200), Is.False);
        Assert.That(unlock.Emotes, Has.Count.EqualTo(1));
        Assert.That(unlock.GetEmote(Learned).LearnTime, Is.EqualTo(100));
    }

    [Test]
    public void Favorite_OfAnEmoteNotHeld_IsRefused() {
        var unlock = new Unlock();
        Assert.That(unlock.SetEmoteFavorite(Learned, true), Is.False);
        Assert.That(unlock.EmoteStates, Is.Empty);
    }

    [Test]
    public void Favorite_IsKeptAndTakenBack() {
        var unlock = new Unlock();
        unlock.Emotes.Add(Greet);       // a starting emote has no state row
        unlock.Emotes.Add(Learned);     // as has an emote learned before the learn time was kept
        Assert.That(unlock.GetEmote(Learned).LearnTime, Is.EqualTo(0));
        Assert.That(unlock.GetEmote(Learned).Permanent, Is.False);

        Assert.That(unlock.SetEmoteFavorite(Greet, true), Is.True);
        Assert.That(unlock.GetEmote(Greet).Favorite, Is.True);
        Assert.That(unlock.GetEmote(Greet).Permanent, Is.True);
        Assert.That(unlock.SetEmoteFavorite(Greet, false), Is.True);
        Assert.That(unlock.GetEmote(Greet).Favorite, Is.False);
    }
}
