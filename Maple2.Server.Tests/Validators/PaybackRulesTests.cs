using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Validators;

// The merat payback's rules as commonfiles/paybacktable.cpp computes them, with the KR table/paybacktable.xml rows
// and the KR server constants (PaybackStartDate 20220101, PaybackEndDate 20220228, PaybackSettleMinutes 5,
// PaybackMaxRewardMerat 10000).
public class PaybackRulesTests {
    internal static readonly PaybackTable Kr = new([
        new(1, 5), new(3, 5), new(5, 5), new(7, 5), new(9, 10), new(12, 10), new(15, 10), new(19, 20), new(23, 20), new(28, 10),
    ]);

    internal static ConstantsTable Constants(int start = 20220101, DateTime? end = null, int settleMinutes = 5, int productId = 1000200013,
        int maxReward = 10000) =>
        new(PaybackStartDate: start, PaybackEndDate: end ?? new DateTime(2022, 2, 28), PaybackSettleMinutes: settleMinutes,
            PaybackMarketProductSnList: productId, PaybackMailId: 50000020, PaybackMailPeriodDay: 90, PaybackMaxRewardMerat: maxReward);

    [Test]
    public void Period_StartDayToEndDaysLastSecond() {
        ConstantsTable constants = Constants();
        Assert.Multiple(() => {
            Assert.That(PaybackRules.InPeriod(Kr, constants, new DateTime(2021, 12, 31, 23, 59, 59)), Is.False);
            Assert.That(PaybackRules.InPeriod(Kr, constants, new DateTime(2022, 1, 1)), Is.True);
            Assert.That(PaybackRules.InPeriod(Kr, constants, new DateTime(2022, 2, 28, 23, 59, 58)), Is.True);
            Assert.That(PaybackRules.InPeriod(Kr, constants, new DateTime(2022, 2, 28, 23, 59, 59)), Is.False);
            Assert.That(PaybackRules.InPeriod(PaybackTable.Empty, constants, new DateTime(2022, 1, 10)), Is.False);
            Assert.That(PaybackRules.InPeriod(Kr, Constants(start: 0), new DateTime(2022, 1, 10)), Is.False);
        });
    }

    // The months paid in: February (January's) to March (February's).
    [Test]
    public void MonthPeriod_MonthAfterStartToMonthAfterEnd() {
        ConstantsTable constants = Constants();
        Assert.Multiple(() => {
            Assert.That(PaybackRules.InMonthPeriod(Kr, constants, new DateTime(2022, 1, 31, 23, 59, 59)), Is.False);
            Assert.That(PaybackRules.InMonthPeriod(Kr, constants, new DateTime(2022, 2, 1)), Is.True);
            Assert.That(PaybackRules.InMonthPeriod(Kr, constants, new DateTime(2022, 3, 31, 23, 59, 59)), Is.True);
            Assert.That(PaybackRules.InMonthPeriod(Kr, constants, new DateTime(2022, 4, 1)), Is.False);
        });
    }

    [Test]
    public void ResetWindow_FirstDaysFirstMinutes() {
        ConstantsTable constants = Constants();
        Assert.Multiple(() => {
            Assert.That(PaybackRules.IsResetWindow(Kr, constants, new DateTime(2022, 2, 1, 0, 0, 0)), Is.True);
            Assert.That(PaybackRules.IsResetWindow(Kr, constants, new DateTime(2022, 2, 1, 0, 5, 0)), Is.True);
            Assert.That(PaybackRules.IsResetWindow(Kr, constants, new DateTime(2022, 2, 1, 0, 5, 1)), Is.False);
            Assert.That(PaybackRules.IsResetWindow(Kr, constants, new DateTime(2022, 2, 2, 0, 1, 0)), Is.False);
            Assert.That(PaybackRules.IsResetWindow(PaybackTable.Empty, constants, new DateTime(2022, 2, 1)), Is.False);
        });
    }

    [Test]
    public void Merat_LevelShares() {
        Assert.Multiple(() => {
            Assert.That(PaybackRules.MeratUpTo(Kr, 0, 1000), Is.EqualTo(0));
            Assert.That(PaybackRules.MeratUpTo(Kr, 1, 1000), Is.EqualTo(50));
            Assert.That(PaybackRules.MeratUpTo(Kr, 9, 1000), Is.EqualTo(300));
            Assert.That(PaybackRules.MeratUpTo(Kr, 10, 1000), Is.EqualTo(300));
            Assert.That(PaybackRules.MeratUpTo(Kr, 28, 1000), Is.EqualTo(1000));
            Assert.That(PaybackRules.MeratUpTo(Kr, 99, 1000), Is.EqualTo(1000));
        });
    }

    // Floors on every level but the last, which gets what they leave: 7 merets are 0 each for the 5 and 10 weights,
    // 1 each for the 20s, and 5 for level 28.
    [Test]
    public void Merat_LastLevelTakesTheRemainder() {
        Assert.Multiple(() => {
            Assert.That(PaybackRules.LevelMerat(Kr, Kr.Levels[7], 7), Is.EqualTo(1));
            Assert.That(PaybackRules.LevelMerat(Kr, Kr.Levels[^1], 7), Is.EqualTo(5));
            Assert.That(PaybackRules.MeratUpTo(Kr, 23, 7), Is.EqualTo(2));
            Assert.That(PaybackRules.MeratUpTo(Kr, 28, 7), Is.EqualTo(7));
        });
    }

    [Test]
    public void Percent_SharesSummed() {
        Assert.Multiple(() => {
            Assert.That(PaybackRules.PercentUpTo(Kr, 0), Is.EqualTo(0f));
            Assert.That(PaybackRules.PercentUpTo(Kr, 9), Is.EqualTo(30f));
            Assert.That(PaybackRules.PercentUpTo(Kr, 28), Is.EqualTo(100f));
            Assert.That(PaybackRules.FormatPercent(PaybackRules.PercentUpTo(Kr, 9)), Is.EqualTo("30"));
            Assert.That(PaybackRules.FormatPercent(12.5f), Is.EqualTo("12.5"));
        });
    }

    [Test]
    public void Reward_CappedByMaxRewardMerat() {
        Assert.Multiple(() => {
            Assert.That(PaybackRules.Reward(Kr, Constants(), 28, 300000), Is.EqualTo(10000));
            Assert.That(PaybackRules.Reward(Kr, Constants(maxReward: 0), 28, 300000), Is.EqualTo(300000));
            Assert.That(PaybackRules.Reward(Kr, Constants(), 9, 1000), Is.EqualTo(300));
            Assert.That(PaybackRules.Reward(Kr, Constants(), 28, 0), Is.EqualTo(0));
        });
    }

    // The start's month and the month after are one period (GetPeriodText), counted in the start's month.
    [Test]
    public void PeriodKey_FirstPeriodIsTwoMonths() {
        ConstantsTable constants = Constants();
        Assert.Multiple(() => {
            Assert.That(PaybackRules.PeriodKey(constants, new DateTime(2022, 1, 20)), Is.EqualTo(202201));
            Assert.That(PaybackRules.PeriodKey(constants, new DateTime(2022, 2, 28)), Is.EqualTo(202201));
            Assert.That(PaybackRules.PeriodKey(constants, new DateTime(2022, 3, 1)), Is.EqualTo(202203));
            Assert.That(PaybackRules.PeriodKey(Constants(start: 20211215, end: new DateTime(2022, 3, 31)), new DateTime(2022, 1, 3)), Is.EqualTo(202112));
            Assert.That(PaybackRules.PeriodKey(Constants(start: 20211215, end: new DateTime(2022, 3, 31)), new DateTime(2022, 2, 3)), Is.EqualTo(202202));
            Assert.That(PaybackRules.PeriodKey(Constants(start: 0), new DateTime(2022, 2, 3)), Is.EqualTo(202202));
        });
    }

    [Test]
    public void Record_CurrentAndDue() {
        ConstantsTable constants = Constants(end: new DateTime(2022, 4, 30));
        var january = new DateTime(2022, 1, 20);
        var february = new DateTime(2022, 2, 3);
        var march = new DateTime(2022, 3, 2);
        var record = new PaybackRecord(202201, 500, 4);
        Assert.Multiple(() => {
            Assert.That(PaybackRules.Current(null, Kr, constants, january, 4), Is.EqualTo(new PaybackRecord(202201, 0, 4)));
            Assert.That(PaybackRules.Current(record, Kr, constants, january, 9), Is.SameAs(record));
            Assert.That(PaybackRules.Current(record, Kr, constants, february, 9), Is.SameAs(record));
            Assert.That(PaybackRules.Current(record, Kr, constants, march, 9), Is.EqualTo(new PaybackRecord(202203, 0, 9)));
            // After the period: no new month.
            Assert.That(PaybackRules.Current(record, Kr, Constants(), march, 9), Is.Null);
            Assert.That(PaybackRules.Due(record, constants, january), Is.False);
            Assert.That(PaybackRules.Due(record, constants, february), Is.False);
            Assert.That(PaybackRules.Due(record, constants, march), Is.True);
            Assert.That(PaybackRules.Due(record with { Spent = 0 }, constants, march), Is.False);
            Assert.That(PaybackRules.LevelsGained(record, 13), Is.EqualTo(9));
            Assert.That(PaybackRules.LevelsGained(record, 2), Is.EqualTo(0));
        });
    }

    // PaybackTable::Load reads adventure_level and payback_percent (the first_month_* columns are not read), keeps the
    // rows the filter lets through, skips level 0 and sorts by level.
    [Test]
    public void Ingest_ReadsLevelAndPercent() {
        var document = new XmlDocument();
        document.LoadXml("""
            <ms2>
              <payback adventure_level="3" payback_percent="5" first_month_adventure_level="6" first_month_payback_percent="5" feature="Payback" locale="KR" />
              <payback adventure_level="1" payback_percent="5" first_month_adventure_level="3" first_month_payback_percent="5" feature="Payback" locale="KR" />
              <payback adventure_level="0" payback_percent="50" feature="Payback" locale="KR" />
              <payback adventure_level="x" payback_percent="50" feature="Payback" locale="KR" />
              <payback adventure_level="2" payback_percent="7" feature="Payback" locale="CN" />
            </ms2>
            """);
        PaybackTable table = TableMapper.ReadPayback(document, node => node.GetAttribute("locale") == "KR");
        Assert.That(table.Levels, Is.EqualTo(new[] { new PaybackTable.Level(1, 5), new PaybackTable.Level(3, 5) }).AsCollection);
    }

    // GameEvent 910300 (FinishPayback): the client's event class reads only the id after the name.
    [Test]
    public void FinishPaybackEvent_NameAndId() {
        var metadata = new GameEventMetadata(910300, GameEventType.FinishPayback, DateTime.MinValue, DateTime.MaxValue, TimeSpan.Zero, TimeSpan.Zero, [],
            new FinishPayback(), "", "", "", "");
        var writer = new ByteWriter();
        writer.WriteClass<GameEvent>(new GameEvent(metadata));
        Assert.That(Convert.ToHexString(writer.ToArray()),
            Is.EqualTo("0D00" + "460069006E00690073006800500061007900620061006300" + "6B00" + "DCE30D00"));
    }
}
