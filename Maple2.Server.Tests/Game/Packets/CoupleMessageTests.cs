using System;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Packets;

public class CoupleMessageTests {
    private static Marriage Make() {
        return new Marriage {
            Id = 9,
            Status = MaritalStatus.Married,
            Partner1 = new MarriagePartner { Info = new PlayerInfo(new CharacterInfo(1, 2, "a", "", "", Gender.Male, Job.Newbie, 1), "", new AchievementInfo(), []) },
            Partner2 = new MarriagePartner { Info = new PlayerInfo(new CharacterInfo(4, 3, "b", "", "", Gender.Female, Job.Newbie, 1), "", new AchievementInfo(), []) },
        };
    }

    [Test]
    public void MessageGoesToTheWritersEntry() {
        Marriage marriage = Make();
        Assert.That(CoupleMessageRule.Apply(marriage, 3, "hi"), Is.True);
        Assert.That(marriage.Partner1.Message, Is.Empty);
        Assert.That(marriage.Partner2.Message, Is.EqualTo("hi"));
    }

    [Test]
    public void StrangerOrSingleIsRefused() {
        Assert.That(CoupleMessageRule.Apply(Make(), 7, "hi"), Is.False);
        Assert.That(CoupleMessageRule.Apply(Marriage.Default, 0, "hi"), Is.False);
        Assert.That(CoupleMessageRule.Apply(Marriage.Default, 2, "hi"), Is.False);
    }

    [Test]
    public void LongMessageIsCut() {
        Marriage marriage = Make();
        CoupleMessageRule.Apply(marriage, 2, new string('x', 300));
        Assert.That(marriage.Partner1.Message.Length, Is.EqualTo(CoupleMessageRule.MaxLength));
    }

    [Test]
    public void UpdateMarriageCarriesBothMessages() {
        Marriage marriage = Make();
        CoupleMessageRule.Apply(marriage, 2, "x");
        CoupleMessageRule.Apply(marriage, 3, "yz");
        ByteWriter writer = WeddingPacket.UpdateMarriage(marriage);
        byte[] body = writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();
        // sub-type byte, id 8, status 1, two times 8+8, two partners of 8+8+(2+2)+1, exp 8 = 76 bytes before the messages
        Assert.That(body[0], Is.EqualTo(0));
        byte[] tail = body.AsSpan(76).ToArray();
        Assert.That(tail, Is.EqualTo(new byte[] {
            1, 0, (byte) 'x', 0, // first partner's message
            2, 0, (byte) 'y', 0, (byte) 'z', 0, // second partner's message
            0, 0, // empty string
            0, 0, // profile
            0, 0, 0, 0, // exp history count
        }));
    }
}
