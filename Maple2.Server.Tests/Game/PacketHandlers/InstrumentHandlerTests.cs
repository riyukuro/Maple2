using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.File.Ingest;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// PlayInstrument 0x0074 mode 0 (start improvise, i64 instrument uid): the client refuses an expired instrument
// (CItem::FUN_140528980, s_play_instrument_invalid_instrument), and so does the server.
public class InstrumentHandlerTests {
    private const int InstrumentItemId = 34000003;
    private const int InstrumentId = 1;

    private TestGameSession test = null!;

    private GameSession Start(long expiryTime, out Item instrument, int scoreItemId = 0, ItemMetadata? scoreData = null) {
        test = new TestGameSession();
        ItemMetadata metadata = TestGameSession.Metadata(InstrumentItemId, type: 6,
            function: new ItemMetadataFunction(ItemFunction.OpenInstrument, "OpenInstrument", $"{InstrumentId}", false));
        Item stored = test.Store(metadata);
        if (scoreItemId != 0) {
            test.Store(scoreData ?? TestGameSession.Metadata(scoreItemId, type: 6));
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

    // MusicInstrument off: the client uses no instrument item (invenslot.cpp:803), so the server plays none either.
    [Test]
    public void StartImprovise_WithMusicInstrumentOff_PlaysNothing() {
        GameSession session = Start(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600, out Item instrument);
        test.SetFeaturesOff(InstrumentHandler.Feature);

        StartImprovise(session, instrument.Uid);

        Assert.That(session.Instrument, Is.Null);
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
    // The play dialog's score slot takes no blank written score (uiplayinstrumentdialog.cpp, CItem::FUN_140525730:
    // a custom-note score with no author), so a start with one plays nothing and spends no use; a written one plays.
    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("writer", true)]
    public void StartScore_RefusesABlankWrittenScore(string? author, bool plays) {
        ItemMetadata scoreData = TestGameSession.Metadata(35100001, type: 6) with {
            Music = new ItemMetadataMusic(10, 1, 100, true, 3000, string.Empty, 0),
        };
        GameSession session = Start(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600, out Item instrument, scoreData.Id, scoreData);
        Item score = session.Item.Inventory.Filter(item => item.Id == scoreData.Id, InventoryType.FishingMusic).Single();
        score.RemainUses = 5;
        score.Music = author == null ? null : new ItemCustomMusicScore { Author = author, Title = "song", Length = 2500, Mml = "cdefg" };

        new InstrumentHandler { TableMetadata = test.TableMetadata, BanWordStorage = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteLong(instrument.Uid);
            writer.WriteLong(score.Uid);
        }));

        Assert.That(session.Instrument != null, Is.EqualTo(plays));
        Assert.That(score.RemainUses, Is.EqualTo(plays ? 4 : 5));
    }
    // PlayInstrument 8 (compose): the length must be the one the client computes from the notes (cdefg: five quarter
    // notes at tempo 120, 2.5 s), give or take 1 ms; another length is refused (17, 8) and the score stays blank.
    [TestCase(2500, true)]
    [TestCase(2501, true)]
    [TestCase(2502, false)]
    [TestCase(3_600_000, false)]
    public void ComposeScore_TakesOnlyTheLengthTheNotesPlayFor(int length, bool composed) {
        ItemMetadata scoreData = TestGameSession.Metadata(35100001, type: 6) with {
            Music = new ItemMetadataMusic(10, 1, 100, true, 3000, string.Empty, 0),
        };
        GameSession session = Start(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600, out _, scoreData.Id, scoreData);
        Item score = session.Item.Inventory.Filter(item => item.Id == scoreData.Id, InventoryType.FishingMusic).Single();
        score.RemainUses = 5;
        score.Music = new ItemCustomMusicScore();
        var words = new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        new InstrumentHandler { TableMetadata = test.TableMetadata, BanWordStorage = new BanWordStorage(words) }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(8);
            writer.WriteLong(score.Uid);
            writer.WriteInt(length);
            writer.WriteInt(1); // the instrument category
            writer.WriteUnicodeString("song");
            writer.WriteString("cdefg");
        }));

        Assert.That(score.Music!.Author, composed ? Is.Not.Empty : Is.Empty);
        Assert.That(score.Music.Length, Is.EqualTo(composed ? length : 0));
    }
    // A home's music row (HomePermission 3): the client sends no play where it stops the player
    // (CubeMapInformationService::FUN_1410c3cd0, checked before modes 0, 3 and 5); the server refuses one too.
    // Setting 1 (Self) stops a visitor, not the owner; setting 0 stops the owner as well; no row stops no one.
    [TestCase(false, null, true)]
    [TestCase(false, HomePermissionSetting.Self, false)]
    [TestCase(true, HomePermissionSetting.Self, true)]
    [TestCase(true, HomePermissionSetting.None, false)]
    [TestCase(false, HomePermissionSetting.Party, false)]
    public void StartImprovise_InAHome_FollowsTheMusicRow(bool owner, HomePermissionSetting? setting, bool plays) {
        GameSession session = Start(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600, out Item instrument);
        session.Housing.InitNewHome("home", null);
        test.EnterHome();
        var home = new Home { AccountId = owner ? session.AccountId : session.AccountId + 1 };
        if (setting != null) {
            home.Permissions[HomePermission.Music] = setting.Value;
        }
        typeof(HomeFieldManager).GetField("<Home>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Field, home);

        StartImprovise(session, instrument.Uid);

        Assert.That(session.Instrument != null, Is.EqualTo(plays));
    }
    // RequestCube 42 (row, on) in the owner's home: the field keeps the home it loaded from the database, so the
    // row must reach that copy too, or the check above reads the old rows. An enabled row starts at setting 0,
    // which stops the owner as well.
    [Test]
    public void EnablePermission_ReachesTheFieldsHome() {
        GameSession session = Start(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600, out Item instrument);
        session.Housing.InitNewHome("home", null);
        test.EnterHome();
        var loaded = new Home { AccountId = session.AccountId };
        typeof(HomeFieldManager).GetField("<Home>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Field, loaded);

        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(42);
            writer.WriteByte((byte) HomePermission.Music);
            writer.WriteBool(true);
        }));
        StartImprovise(session, instrument.Uid);

        Assert.That(loaded.Permissions.ContainsKey(HomePermission.Music), Is.True);
        Assert.That(session.Instrument, Is.Null);
    }
}
