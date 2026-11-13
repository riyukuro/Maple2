using System.Numerics;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Emote (CSceneStateGame::OnEmote, scenestategame.cpp:2533): byte mode; 0: int count x (int id, int level,
// bool permanent, long expiry, long learn time, bool favourite) (CSkillSystem::AddEmotion, skillslot.cpp:2790);
// 1: int id, int level, long expiry, long learn time; 3: byte result (1 already learned, 0 invalid).
// BuddyEmote (CNetworkService::OnBuddyEmote, networkservice.cpp:1096): byte mode; 2: byte error; 5: int emote,
// long first, long second, 3 floats start, 3 floats end, int effect seed.
public class EmoteLayoutTests {
    private static ByteReader Read(ByteWriter writer, SendOp op) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) op));
        return reader;
    }

    [Test]
    public void LoadLearnError() {
        ByteReader reader = Read(EmotePacket.Load([new Emote(90200001, learnTime: 1700000000, favorite: true, expiryTime: 5)]), SendOp.Emote);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(90200001));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.ReadLong(), Is.EqualTo(5));
        Assert.That(reader.ReadLong(), Is.EqualTo(1700000000));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(EmotePacket.Learn(new Emote(90200001, learnTime: 1700000000)), SendOp.Emote);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(90200001));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(1700000000));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(EmotePacket.Error(EmoteError.s_dynamic_action_already_learn), SendOp.Emote);
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void BuddyStartAndError() {
        ByteReader reader = Read(BuddyEmotePacket.Start(90200011, 10, 20, new Vector3(1, 2, 3), new Vector3(4, 5, 6), 77), SendOp.BuddyEmote);
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(90200011));
        Assert.That(reader.ReadLong(), Is.EqualTo(10));
        Assert.That(reader.ReadLong(), Is.EqualTo(20));
        foreach (float value in new float[] { 1, 2, 3, 4, 5, 6 }) {
            Assert.That(reader.ReadFloat(), Is.EqualTo(value));
        }
        Assert.That(reader.ReadInt(), Is.EqualTo(77));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(BuddyEmotePacket.Error(BuddyEmoteError.s_couple_emotion_failed), SendOp.BuddyEmote);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadByte(), Is.EqualTo(byte.MaxValue));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
