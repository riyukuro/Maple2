using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// The promotionContents rows (PromotionTable): the ingest keeps the locale's non-snowman rows, and a counted
// condition gives a running row's box with its chance, level, code and limit (the KMS2 rows' 22_06 coin event and
// the TreasureMap01 key drops).
public class PromotionTests {
    private const int Box = 20001013;
    private const int Coin = 30001529;
    private const int FishId = 99000001;

    private static readonly DateTime Start = new(2022, 6, 9, 8, 0, 0);
    private static readonly DateTime End = new(2022, 9, 8, 8, 0, 0);

    private const string Xml = """
        <ms2>
          <promotion id="8804" promotionType="normal" startTime="2022-06-09-08-00" endTime="2022-09-08-08-00" point="" type="fish_goldmedal" rewardLimitType="day" rewardLimitCount="23" rewardLimitShareID="8804" prop="10000" code="" target="" individualDropBoxID="20001013" isAccount="1" buffID="" buffLevel="" feature="22_06" locale="KR" />
          <promotion id="8849" promotionType="normal" startTime="2022-06-09-08-00" endTime="2022-09-08-08-00" point="" type="map" rewardLimitType="day" rewardLimitCount="23" rewardLimitShareID="8804" prop="10000" code="80000001-80000010" target="" individualDropBoxID="20001013" isAccount="1" buffID="" buffLevel="" feature="22_06" locale="KR" />
          <promotion id="8866" promotionType="normal" startTime="2022-06-09-08-00" endTime="2022-09-08-08-00" point="" type="enchant_result" rewardLimitType="day" rewardLimitCount="23" rewardLimitShareID="8804" prop="12000" code="2,3,4,5" target="" individualDropBoxID="20001013" isAccount="1" buffID="" buffLevel="" feature="22_06" locale="KR" />
          <promotion id="8822" promotionType="normal" startTime="2022-06-09-08-00" endTime="2022-09-08-08-00" point="" type="trigger" rewardLimitType="day" rewardLimitCount="23" rewardLimitShareID="8804" prop="10000" code="springfarm_start" target="" individualDropBoxID="20001013" isAccount="1" buffID="" buffLevel="" feature="22_06" locale="KR" />
          <promotion id="8021" promotionType="normal" startTime="2018-12-21-00-00" endTime="2100-01-01-12-00" point="" type="fish" rewardLimitType="" rewardLimitCount="" prop="150" code="" target="" individualDropBoxID="330001825" buffID="" buffLevel="" requireLevel="50" feature="TreasureMap01" locale="KR" />
          <promotion id="8020" promotionType="normal" startTime="2018-12-21-00-00" endTime="2100-01-01-12-00" point="" type="playinstrument_time" rewardLimitType="" rewardLimitCount="" prop="500" value="15" code="" target="" individualDropBoxID="330001825" buffID="" buffLevel="" requireLevel="50" feature="TreasureMap01" locale="KR" />
          <promotion id="4" promotionType="goldus" startTime="2018-07-01-07-00" endTime="2018-07-26-07-00" point="" type="pvp_win" rewardLimitType="week" rewardLimitCount="1" prop="10000" code="65000001" target="" individualDropBoxID="330001219" buffID="" buffLevel="" feature="" locale="KR" />
          <promotion id="112" promotionType="normal" startTime="2020-05-21-07-00-00" endTime="2020-07-08-23-59-59" point="" type="Npc" rewardLimitType="day" rewardLimitCount="100" prop="500" code="" target="" individualDropBoxID="340000620" isAccount="1" buffID="" buffLevel="" feature="GemMeta" locale="KR" />
          <promotion id="73" promotionType="Snowman" startTime="2019-01-17-07-00" endTime="2019-02-14-07-00" point="2" type="npc" rewardLimitType="" rewardLimitCount="" prop="5000" code="" target="" individualDropBoxID="" buffID="" buffLevel="" feature="" locale="KR" />
          <promotion id="7201" promotionType="pesca" startTime="2019-12-04-08-00-00" endTime="2050-12-31-00-00-00" point="" type="fish" rewardLimitType="day" rewardLimitCount="999999" prop="10000" code="" target="00000001-63000059,63000061-99999999" individualDropBoxID="320000468" buffID="" buffLevel="" feature="" locale="CN" />
          <promotion id="9" promotionType="normal" startTime="2018-07-01-07-00" endTime="2018-07-26-07-00" point="" type="harvest" rewardLimitType="day" rewardLimitCount="1" prop="10000" code="" target="" individualDropBoxID="330001219" buffID="" buffLevel="" feature="" locale="KR" />
          <promotion id="11" promotionType="normal" startTime="2018-07-01-07-00" endTime="2018-07-26-07-00" point="" type="item_collect" rewardLimitType="day" rewardLimitCount="1" prop="10000" code="" target="" individualDropBoxID="330001219" buffID="" buffLevel="" feature="" locale="KR" />
          <promotion id="10" promotionType="normal" startTime="2018-07-01-07-00" endTime="2018-07-26-07-00" point="" type="npc" rewardLimitType="day" rewardLimitCount="1" prop="10000" code="" target="" individualDropBoxID="" buffID="" buffLevel="" feature="" locale="KR" />
        </ms2>
        """;

    private static PromotionTable Map(string locale) {
        var document = new XmlDocument();
        document.LoadXml(Xml);
        return ServerTableMapper.MapPromotion(document, row => row.GetAttribute("locale") == locale);
    }

    [Test]
    public void Ingest_KeepsTheLocaleRowsButSnowmanAndUncounted() {
        PromotionTable table = Map("KR");

        // Left out: the snowman row, playinstrument_time, item_collect (its check counts the item), a type that is no
        // condition and a row without a box.
        Assert.That(table.Entries.Select(entry => entry.Id), Is.EqualTo(new[] { 8804, 8849, 8866, 8822, 8021, 4, 112 }));
        PromotionTable.Entry coin = table.Entries[0];
        Assert.Multiple(() => {
            Assert.That(coin.Kind, Is.EqualTo(PromotionKind.normal));
            Assert.That(coin.Condition.Type, Is.EqualTo(ConditionType.fish_goldmedal));
            Assert.That(coin.Condition.Codes, Is.Null);
            Assert.That((coin.StartTime, coin.EndTime), Is.EqualTo((Start, End)));
            Assert.That((coin.LimitType, coin.LimitCount, coin.ShareId, coin.IsAccount), Is.EqualTo((PromotionLimit.day, 23, 8804, true)));
            Assert.That(coin.IndividualDropBoxId, Is.EqualTo(Box));
        });
        Assert.Multiple(() => {
            Assert.That(table.Entries[1].Condition.Codes!.Range, Is.EqualTo(new ConditionMetadata.Range<int>(80000001, 80000010)));
            Assert.That(table.Entries[2].Condition.Codes!.Integers, Is.EqualTo(new[] { 2, 3, 4, 5 }));
            Assert.That(table.Entries[2].Prop, Is.EqualTo(10000));
            Assert.That(table.Entries[3].Condition.Codes!.Strings, Is.EqualTo(new[] { "springfarm_start" }));
            // No limit type or count: no limit; the level is kept.
            Assert.That((table.Entries[4].LimitType, table.Entries[4].LimitCount, table.Entries[4].RequireLevel), Is.EqualTo((PromotionLimit.none, 0, 50)));
            // goldus: the row's own id is its counter, kept for the character.
            Assert.That((table.Entries[5].Kind, table.Entries[5].LimitType, table.Entries[5].ShareId, table.Entries[5].IsAccount),
                Is.EqualTo((PromotionKind.goldus, PromotionLimit.week, 4, false)));
            // "Npc" is the npc condition; the times to the second are read.
            Assert.That(table.Entries[6].Condition.Type, Is.EqualTo(ConditionType.npc));
            Assert.That(table.Entries[6].EndTime, Is.EqualTo(new DateTime(2020, 7, 8, 23, 59, 59)));
        });
    }

    [Test]
    public void Ingest_OtherLocale_ItsRowsOnly() {
        PromotionTable table = Map("CN");

        // A target list of two ranges: one entry per range.
        Assert.That(table.Entries.Select(entry => (entry.Id, entry.Kind)), Is.EqualTo(new[] { (7201, PromotionKind.pesca), (7201, PromotionKind.pesca) }));
        Assert.That(table.Entries.Select(entry => entry.Condition.Target!.Range), Is.EqualTo(new ConditionMetadata.Range<int>?[] {
            new ConditionMetadata.Range<int>(1, 63000059), new ConditionMetadata.Range<int>(63000061, 99999999),
        }));
    }

    [Test]
    public void Expiration_DayWeekAndEventPeriod() {
        PromotionTable table = Map("KR");
        var now = new DateTime(2022, 6, 15, 13, 30, 0); // a Wednesday
        static long Seconds(DateTime time) => new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Local)).ToUnixTimeSeconds();

        Assert.Multiple(() => {
            Assert.That(PromotionRules.Expiration(table.Entries[0], now), Is.EqualTo(Seconds(new DateTime(2022, 6, 16)) - 1));
            Assert.That(PromotionRules.Expiration(table.Entries[5], now), Is.EqualTo(Seconds(WeeklyResetRules.NextStart(now)) - 1));
            Assert.That(PromotionRules.Expiration(table.Entries[0] with { LimitType = PromotionLimit.eventPeriod }, now), Is.EqualTo(Seconds(End) - 1));
            Assert.That(PromotionRules.Runs(table.Entries[0], Start), Is.True);
            Assert.That(PromotionRules.Runs(table.Entries[0], End), Is.False);
            Assert.That(PromotionRules.Runs(table.Entries[0], Start.AddSeconds(-1)), Is.False);
        });
    }

    private TestGameSession test = null!;
    private DateTime clock;
    private int roll;

    private static PromotionTable.Entry Row(int id, ConditionType type, ConditionMetadata.Parameters? codes = null, int limit = 2,
        PromotionLimit limitType = PromotionLimit.day, int shareId = 8804, bool isAccount = true, int requireLevel = 0, int prop = 10000,
        PromotionKind kind = PromotionKind.normal) =>
        new(id, kind, new ConditionMetadata(type, 1, codes, null), Start, End, prop, Box, requireLevel, limitType,
            limitType == PromotionLimit.none ? 0 : limit, shareId, isAccount);

    private GameSession Login(params PromotionTable.Entry[] rows) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Coin));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [Box] = TestGameSession.DropBox([(Coin, 1)]),
        }));
        test.SetServerTable(new PromotionTable(rows));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        clock = new DateTime(2022, 6, 15, 13, 30, 0);
        roll = 0;
        session.GameEvent.Clock = () => clock;
        session.GameEvent.Roll = _ => roll;
        test.TakeSent();
        return session;
    }

    private static int Held(GameSession session) => session.Item.Inventory.Find(Coin).Sum(item => item.Amount);

    private List<(long CharacterId, int ShareId, int Count)> Stored() {
        using var context = new Ms2Context(test.Options);
        return context.PromotionCount.AsNoTracking().Select(row => new { row.CharacterId, row.ShareId, row.Count }).ToList()
            .Select(row => (row.CharacterId, row.ShareId, row.Count)).ToList();
    }

    [Test]
    public void Fish_GivesTheBox_AndTheNotice() {
        GameSession session = Login(Row(8806, ConditionType.fish, new ConditionMetadata.Parameters(Integers: [FishId])));

        session.ConditionUpdate(ConditionType.fish, codeLong: FishId, targetLong: 2000001);

        Assert.That(Held(session), Is.EqualTo(1));
        // TaskEvent: type 0, id 0, rank 0, one item, no notice, the coin and its amount.
        byte[] notice = test.TakeSent().Single(packet => BitConverter.ToUInt16(packet, 0) == (ushort) SendOp.TaskEvent);
        Assert.That(Convert.ToHexString(notice), Is.EqualTo("F900" + "00" + "00000000" + "0000" + "01000000" + "00"
                                                            + Convert.ToHexString(BitConverter.GetBytes(Coin)) + "0100"));
        Assert.That(Stored(), Is.EqualTo(new[] { (0L, 8804, 1) }));
    }

    // The count and the drop are written together: the count was written on its own first, so an item write that
    // failed spent it with nothing given. Now nothing is given or counted, and the row can drop again.
    [Test]
    public void LimitedDrop_ItemWriteFails_CountsNothing() {
        GameSession session = Login(Row(8806, ConditionType.fish, new ConditionMetadata.Parameters(Integers: [FishId]), limit: 1));
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.Item>().Any(entry => entry.State == EntityState.Added);

        session.ConditionUpdate(ConditionType.fish, codeLong: FishId, targetLong: 2000001);
        test.FailSave = null;

        Assert.That(Held(session), Is.EqualTo(0));
        Assert.That(Stored(), Is.Empty);
        Assert.That(test.TakeSent().Any(packet => BitConverter.ToUInt16(packet, 0) == (ushort) SendOp.TaskEvent), Is.False);

        session.ConditionUpdate(ConditionType.fish, codeLong: FishId, targetLong: 2000001);
        Assert.That(Held(session), Is.EqualTo(1));
        Assert.That(Stored(), Is.EqualTo(new[] { (0L, 8804, 1) }));
    }

    [Test]
    public void OtherCode_LowLevel_FailedRoll_OutsideDates_GiveNothing() {
        GameSession session = Login(Row(8806, ConditionType.fish, new ConditionMetadata.Parameters(Integers: [FishId]), prop: 150),
            Row(8021, ConditionType.mastery_harvest, limitType: PromotionLimit.none, requireLevel: 50));

        session.ConditionUpdate(ConditionType.fish, codeLong: FishId + 1);
        roll = 150;
        session.ConditionUpdate(ConditionType.fish, codeLong: FishId);
        roll = 0;
        session.ConditionUpdate(ConditionType.mastery_harvest);
        clock = End;
        session.ConditionUpdate(ConditionType.fish, codeLong: FishId);
        Assert.That(Held(session), Is.EqualTo(0));

        clock = Start;
        session.Player.Value.Character.Level = 50;
        session.ConditionUpdate(ConditionType.mastery_harvest);
        Assert.That(Held(session), Is.EqualTo(1));
        // A row without a limit keeps no counter.
        Assert.That(Stored(), Is.Empty);
    }

    [Test]
    public void SharedDailyLimit_StopsEveryRowOfTheShare_UntilTheNextDay() {
        GameSession session = Login(Row(8822, ConditionType.trigger, new ConditionMetadata.Parameters(Strings: ["springfarm_start"])),
            Row(8849, ConditionType.map, new ConditionMetadata.Parameters(Range: new ConditionMetadata.Range<int>(80000001, 80000010))));

        session.ConditionUpdate(ConditionType.trigger, codeString: "springfarm_start");
        session.ConditionUpdate(ConditionType.map, codeLong: 80000004);
        session.ConditionUpdate(ConditionType.map, codeLong: 80000004);
        session.ConditionUpdate(ConditionType.trigger, codeString: "springfarm_start");
        Assert.That(Held(session), Is.EqualTo(2));

        clock = clock.Date.AddDays(1);
        session.ConditionUpdate(ConditionType.map, codeLong: 80000004);
        Assert.That(Held(session), Is.EqualTo(3));
        Assert.That(Stored(), Is.EqualTo(new[] { (0L, 8804, 1) }));
        // A reached limit is no failure: nothing is logged as an error.
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void CharacterRow_CountsForTheCharacter_AccountRowForTheAccount() {
        GameSession session = Login(Row(4, ConditionType.pvp_win, limit: 1, limitType: PromotionLimit.week, shareId: 4, isAccount: false, kind: PromotionKind.goldus),
            Row(8804, ConditionType.fish_goldmedal, limit: 1));

        session.ConditionUpdate(ConditionType.pvp_win, codeLong: 65000001);
        session.ConditionUpdate(ConditionType.pvp_win, codeLong: 65000001);
        session.ConditionUpdate(ConditionType.fish_goldmedal, codeLong: FishId);

        Assert.That(Held(session), Is.EqualTo(2));
        Assert.That(Stored().OrderBy(row => row.ShareId), Is.EqualTo(new[] { (session.CharacterId, 4, 1), (0L, 8804, 1) }));
        // The goldus row's notice is type 1 (Goldus).
        Assert.That(test.TakeSent().Count(packet => BitConverter.ToUInt16(packet, 0) == (ushort) SendOp.TaskEvent && packet[2] == 1), Is.EqualTo(1));
    }

    // Two sessions of one account restart the same expired counter: the count is 1 before and after, so only the
    // end tells the second writer that the row moved; its save fails and it reads again (TakePromotionCount).
    [Test]
    public void ExpiredCounter_RestartedByAnotherSession_LosesTheRace() {
        test = new TestGameSession();
        test.Start();
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.TakePromotionCount(1, 0, 4, 1, 1_010, 1_000), Is.True);
        }

        using var stale = new Ms2Context(test.Options);
        var row = stale.PromotionCount.AsTracking().Single(count => count.ShareId == 4);
        using (GameStorage.Request other = test.Storage.Context()) {
            Assert.That(other.TakePromotionCount(1, 0, 4, 1, 1_100, 1_050), Is.True);
        }
        row.Count = 1;
        row.ExpirationTime = 1_100;

        Assert.Throws<DbUpdateConcurrencyException>(() => stale.SaveChanges());
        using (GameStorage.Request again = test.Storage.Context()) {
            // Read again, the restarted counter is full.
            Assert.That(again.TakePromotionCount(1, 0, 4, 1, 1_100, 1_050), Is.False);
        }
    }

    [Test]
    public void Counter_PastItsEnd_StartsAgain_AndAnotherCharacterHasItsOwn() {
        test = new TestGameSession();
        test.Start();
        long now = 1_000;
        using GameStorage.Request db = test.Storage.Context();

        Assert.Multiple(() => {
            Assert.That(db.TakePromotionCount(1, 7, 4, 2, now + 10, now), Is.True);
            Assert.That(db.TakePromotionCount(1, 7, 4, 2, now + 10, now), Is.True);
            Assert.That(db.TakePromotionCount(1, 7, 4, 2, now + 10, now), Is.False);
            Assert.That(db.TakePromotionCount(1, 8, 4, 2, now + 10, now), Is.True);
            // Past the end: counted from 0 again, with the new end.
            Assert.That(db.TakePromotionCount(1, 7, 4, 2, now + 100, now + 11), Is.True);
            Assert.That(db.TakePromotionCount(1, 7, 4, 2, now + 100, now + 11), Is.True);
            Assert.That(db.TakePromotionCount(1, 7, 4, 2, now + 100, now + 11), Is.False);
        });
    }
}
