using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.File.Ingest;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// PlayInstrument 0x0074 mode 0 (start improvise, i64 instrument uid): the client refuses an expired instrument
// (CItem::FUN_140528980, s_play_instrument_invalid_instrument), and so does the server.
public class InstrumentHandlerTests {
    private const int InstrumentItemId = 34000003;
    private const int InstrumentId = 1;

    private TestGameSession test = null!;

    private GameSession Start(long expiryTime, out Item instrument, int scoreItemId = 0) {
        test = new TestGameSession();
        ItemMetadata metadata = TestGameSession.Metadata(InstrumentItemId, type: 6,
            function: new ItemMetadataFunction(ItemFunction.OpenInstrument, "OpenInstrument", $"{InstrumentId}", false));
        Item stored = test.Store(metadata);
        if (scoreItemId != 0) {
            test.Store(TestGameSession.Metadata(scoreItemId, type: 6));
        }
        test.SetTable(new InstrumentTable(new Dictionary<int, InstrumentMetadata> {
            [InstrumentId] = new(InstrumentId, 0, 1, 1, 1, 0),
        }));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        instrument = session.Item.Inventory.Get(stored.Uid, InventoryType.FishingMusic)!;
        instrument.ExpiryTime = expiryTime;
        test.TakeSent();
        return session;
    }

    private void StartImprovise(GameSession session, long itemUid) {
        new InstrumentHandler { TableMetadata = test.TableMetadata, BanWordStorage = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(itemUid);
        }));
    }

    [Test]
    public void StartImprovise_PlaysAnInstrumentInTime() {
        GameSession session = Start(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600, out Item instrument);

        StartImprovise(session, instrument.Uid);

        Assert.That(session.Instrument, Is.Not.Null);
        Assert.That(session.Instrument!.Improvising, Is.True);
    }

    [Test]
    public void StartImprovise_RefusesAnExpiredInstrument() {
        GameSession session = Start(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1, out Item instrument);

        StartImprovise(session, instrument.Uid);

        Assert.That(session.Instrument, Is.Null);
    }

    // music_play_score (KR 89000423 and 89010346: code 35000033, value 10): the start of a score counts it, by the
    // score's item id; another score does not.
    [TestCase(35000033, 1)]
    [TestCase(35000034, 0)]
    public void StartScore_MusicPlayScore_89000423_TheScoresItem(int scoreItemId, int counted) {
        GameSession session = Start(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600, out Item instrument, scoreItemId);
        Item score = session.Item.Inventory.Filter(item => item.Id == scoreItemId, InventoryType.FishingMusic).Single();
        score.RemainUses = 5;
        Quest quest = test.StartQuest(TestGameSession.QuestData(91000001) with {
            Conditions = [new ConditionMetadata(ConditionType.music_play_score, 10, new[] { "35000033" }.ConvertCodes(), new string[0].ConvertCodes())],
        });

        new InstrumentHandler { TableMetadata = test.TableMetadata, BanWordStorage = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteLong(instrument.Uid);
            writer.WriteLong(score.Uid);
        }));

        Assert.That(session.Instrument?.Score, Is.SameAs(score));
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(counted));
    }
}
