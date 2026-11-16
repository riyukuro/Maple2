using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// EventReward 7 and 8 of the gallery window (UIGalleryEventDialog, uigalleryeventdialog.cpp:413/428): byte mode, byte
// card index. 7 flips a card (starts its quest, once per the day's reveal limit), 8 claims a flipped card's quest.
// Also 5 (OnVIPGMClick, uibuddyeventlistener.cpp:667: byte 1) and 16 (the burning levelup's character pick,
// uicharacterlistdialog.cpp:2093: long character id), which the game server reads and answers nothing to.
// Made-up event and quest ids.
public class EventRewardGalleryTests {
    private const int EventId = 7101;
    private const int QuestA = 60110001;
    private const int QuestB = 60110002;

    private TestGameSession test = null!;

    private GameSession Login(int revealDayLimit = 1) {
        test = new TestGameSession();
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        test.AddEvent(EventId, GameEventType.Gallery, new Gallery([QuestA, QuestB], [], revealDayLimit, "image"));
        GameSession session = test.Start();
        test.Register(TestGameSession.QuestData(QuestA, QuestType.EventQuest));
        test.Register(TestGameSession.QuestData(QuestB, QuestType.EventQuest));
        test.TakeSent();
        return session;
    }

    private static void Reward(GameSession session, byte mode, Action<ByteWriter> body) {
        new EventRewardHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            body(writer);
        }));
    }

    private static void Flip(GameSession session, byte index) => Reward(session, 7, writer => writer.WriteByte(index));
    private static void ClaimCard(GameSession session, byte index) => Reward(session, 8, writer => writer.WriteByte(index));

    private static QuestState? State(GameSession session, int questId) {
        return session.Quest.TryGetQuest(questId, out Quest? quest) ? quest.State : null;
    }

    private List<string> SentHex() => test.TakeSent().Select(Convert.ToHexString).ToList();

    // 7: the card's quest starts and the day's flip is counted; the reply names the card last. With a limit of one,
    // a second card the same day is refused (nothing started, nothing sent).
    [Test]
    public void Flip_StartsTheCardsQuest_OncePerTheDaysLimit() {
        GameSession session = Login(revealDayLimit: 1);

        Flip(session, 1);
        List<string> first = SentHex();
        Flip(session, 0);

        Assert.Multiple(() => {
            Assert.That(State(session, QuestB), Is.EqualTo(QuestState.Started));
            Assert.That(first.Last(), Is.EqualTo(Convert.ToHexString(EventRewardPacket.FlipGalleryCard(1).ToArray())));
            Assert.That(session.GameEvent.Peek(GameEventUserValueType.GalleryCardFlipCount, EventId), Is.EqualTo("1"));
            Assert.That(State(session, QuestA), Is.Null);
            Assert.That(SentHex(), Is.Empty);
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // A card index past the event's cards flips nothing.
    [Test]
    public void Flip_PastTheCards_Nothing() {
        GameSession session = Login();

        Flip(session, 2);

        Assert.Multiple(() => {
            Assert.That(SentHex(), Is.Empty);
            Assert.That(session.GameEvent.Peek(GameEventUserValueType.GalleryCardFlipCount, EventId), Is.Null.Or.Empty);
        });
    }

    // 8: a flipped card's quest is completed with the reply; a card not flipped gives nothing.
    [Test]
    public void ClaimCard_CompletesOnlyAFlippedCard() {
        GameSession session = Login(revealDayLimit: 2);
        Flip(session, 0);
        test.TakeSent();

        ClaimCard(session, 1);
        List<string> notFlipped = SentHex();
        ClaimCard(session, 0);

        Assert.Multiple(() => {
            Assert.That(notFlipped, Is.Empty);
            Assert.That(State(session, QuestB), Is.Null);
            Assert.That(State(session, QuestA), Is.EqualTo(QuestState.Completed));
            Assert.That(SentHex().Last(), Is.EqualTo(Convert.ToHexString(EventRewardPacket.ClaimGalleryQuestReward(0).ToArray())));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // 5 and 16 are read and answered with nothing.
    [Test]
    public void VipGmClickAndEventCharacter_AnswerNothing() {
        GameSession session = Login();

        Reward(session, 5, writer => writer.WriteByte(1));
        Reward(session, 16, writer => writer.WriteLong(test.CharacterId));

        Assert.Multiple(() => {
            Assert.That(SentHex(), Is.Empty);
            Assert.That(test.Errors, Is.Empty);
        });
    }
}
