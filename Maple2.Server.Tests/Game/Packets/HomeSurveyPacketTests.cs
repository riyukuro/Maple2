using System.Collections.Generic;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The home survey packets read as UISurveyDialog::HandlePacket takes them (HomeAction 5, then the survey mode):
// 0 message, 1 progress (question, public, options), 2 question (question, public), 3 added option (question, public,
// option, success), 4 start (owner, id, public, question, options), 5 answer (name), 6 end (id, public, question, per
// option name, votes and, when public, the voters; then the votes of no answer and, when public, their names).
public class HomeSurveyPacketTests {
    private static HomeSurvey Survey(bool isPublic) {
        var survey = new HomeSurvey(77, "Best color?", isPublic) { OwnerId = 5 };
        survey.Options["red"] = ["A", "B"];
        survey.Options["blue"] = [];
        survey.AvailableCharacters = ["C"];
        return survey;
    }

    private static ByteReader Open(ByteWriter packet, byte mode) {
        byte[] bytes = packet.ToArray();
        Assert.That(TestGameSession.Op(bytes), Is.EqualTo(SendOp.HomeAction));
        var reader = new ByteReader(bytes, 2);
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        return reader;
    }

    [Test]
    public void Message_IsTheModeOnly() {
        ByteReader reader = Open(HomeActionPacket.SurveyMessage(), 0);
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Progress_IsTheQuestionAndItsOptions(bool isPublic) {
        ByteReader reader = Open(HomeActionPacket.SurveyProgress(Survey(isPublic)), 1);
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Best color?"));
        Assert.That(reader.ReadByte(), Is.EqualTo(isPublic ? 1 : 0));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("red"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("blue"));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Question_IsTheQuestionAndItsKind() {
        ByteReader reader = Open(HomeActionPacket.SurveyQuestion(Survey(true)), 2);
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Best color?"));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void AddOption_NamesTheLastOptionAndTheResult(bool success) {
        ByteReader reader = Open(HomeActionPacket.SurveyAddOption(Survey(false), success), 3);
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Best color?"));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("blue"));
        Assert.That(reader.ReadByte(), Is.EqualTo(success ? 1 : 0));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Start_IsTheOwnerTheIdAndTheOptions() {
        ByteReader reader = Open(HomeActionPacket.SurveyStart(Survey(true)), 4);
        Assert.That(reader.ReadLong(), Is.EqualTo(5));
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Best color?"));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("red"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("blue"));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Answer_IsTheName() {
        ByteReader reader = Open(HomeActionPacket.SurveyAnswer("Kim"), 5);
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Kim"));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void End_OfAPublicSurvey_NamesTheVoters() {
        ByteReader reader = Open(HomeActionPacket.SurveyEnd(Survey(true)), 6);
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Best color?"));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("red"));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("A"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("B"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("blue"));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("C"));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void End_OfAPrivateSurvey_HasCountsOnly() {
        ByteReader reader = Open(HomeActionPacket.SurveyEnd(Survey(false)), 6);
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Best color?"));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("red"));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("blue"));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // HomeAction 4: the host's alarm, a string-id interface (byte, int, text) shown as a system notice.
    [Test]
    public void HostAlarm_IsAStringIdNoticeWithTheText() {
        byte[] bytes = HomeActionPacket.HostAlarm("hello").ToArray();
        var reader = new ByteReader(bytes, 2);
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("hello"));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
