using System;
using System.Linq;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game;

// GroupChat 0xc (client OnPacketMemberPicture: int chat id, member name, picture): a member's new profile picture
// reaches the chat's other members; an update that keeps the picture sends nothing.
public class GroupChatMemberPictureTests {
    private const int ChatId = 5;

    [Test]
    public void MemberPicture_IsIdNamePicture() {
        var reader = new ByteReader(GroupChatPacket.MemberPicture(ChatId, "other", "new.png").ToArray());
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.GroupChat));
            Assert.That(reader.ReadByte(), Is.EqualTo(0xc));
            Assert.That(reader.ReadInt(), Is.EqualTo(ChatId));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("other"));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("new.png"));
            Assert.That(reader.Available, Is.EqualTo(0));
        });
    }

    [TestCase("new.png", true)]
    [TestCase("", false)]
    public void PictureUpdate_SendsTheMemberPicture(string picture, bool sent) {
        var test = new TestGameSession();
        GameSession session = test.Start();
        long otherId = session.CharacterId + 500;
        test.World.AddPlayer(session.AccountId + 500, otherId, "other");
        _ = new GroupChatManager(new GroupChatInfo {
            Id = ChatId,
            Members = { new GroupChatInfo.Types.Member { CharacterId = otherId } },
        }, session);
        test.TakeSent();

        session.PlayerInfo.ReceiveUpdate(new PlayerUpdateRequest {
            AccountId = session.AccountId + 500,
            CharacterId = otherId,
            Picture = picture,
            Level = 30,
        });

        string[] lines = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.GroupChat).Select(Convert.ToHexString).ToArray();
        string[] expected = sent ? [Convert.ToHexString(GroupChatPacket.MemberPicture(ChatId, "other", picture).ToArray())] : [];
        Assert.That(lines, Is.EqualTo(expected));
    }
}
