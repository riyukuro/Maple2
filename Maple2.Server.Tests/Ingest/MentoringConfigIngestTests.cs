using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

// table/mentoringconfig.xml as MentoringDataTable::Init reads it, and the table's two client rules
// (GetAttendRewardCoin, GetRewardableCoin).
public class MentoringConfigIngestTests {
    private static MentoringConfigTable Read(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return TableMapper.ReadMentoringConfig(document);
    }

    [Test]
    public void Kms2File() {
        MentoringConfigTable table = Read("""
            <?xml version="1.0" encoding="utf-8"?>
            <ms2>
              <MentoringConfig maxMentorDailyRewardCoin="400" maxMenteeDailyRewardCoin="1500" isShowIcon="1">
                <AttendReward>
                  <RewardCoin v="100" />
                  <RewardCoin v="200" />
                  <RewardCoin v="300" />
                </AttendReward>
                <MentorReward coin="1000" />
              </MentoringConfig>
            </ms2>
            """);
        Assert.That(table with { AttendRewardCoins = [] }, Is.EqualTo(MentoringConfigTable.Kms2 with { AttendRewardCoins = [] }));
        Assert.That(table.AttendRewardCoins, Is.EqualTo(new[] { 100, 200, 300 }));
    }

    // A missing value or one that is not a uint keeps the constructor's (0, the icon shown); a coin that is not a
    // number is 0; isShowIcon reads a number or true/false and keeps its value for anything else.
    [Test]
    public void MissingAndBadValuesKeepTheClientsDefaults() {
        MentoringConfigTable table = Read("""<ms2><MentoringConfig maxMentorDailyRewardCoin="-5" isShowIcon="0"><AttendReward><RewardCoin v="x"/><RewardCoin v="7"/></AttendReward></MentoringConfig></ms2>""");
        Assert.Multiple(() => {
            Assert.That(table.MaxMentorDailyRewardCoin, Is.EqualTo(0));
            Assert.That(table.MaxMenteeDailyRewardCoin, Is.EqualTo(0));
            Assert.That(table.ShowIcon, Is.False);
            Assert.That(table.AttendRewardCoins, Is.EqualTo(new[] { 0, 7 }));
            Assert.That(table.MentorRewardCoin, Is.EqualTo(0));
        });
        Assert.That(Read("""<ms2><MentoringConfig isShowIcon="false"/></ms2>""").ShowIcon, Is.False);
        Assert.That(Read("""<ms2><MentoringConfig isShowIcon="yes"/></ms2>""").ShowIcon, Is.True);
        Assert.That(Read("""<ms2><MentoringConfig isShowIcon="2"/></ms2>""").ShowIcon, Is.True);
        Assert.That(Read("""<ms2/>""") with { AttendRewardCoins = [] }, Is.EqualTo(MentoringConfigTable.Empty with { AttendRewardCoins = [] }));
    }

    [TestCase(0, 100)]
    [TestCase(2, 300)]
    [TestCase(9, 300)]
    [TestCase(-1, 100)]
    public void AttendRewardCoin_TheLastPastTheEnd(int day, int coin) {
        Assert.That(MentoringConfigTable.Kms2.AttendRewardCoin(day), Is.EqualTo(coin));
    }

    [Test]
    public void AttendRewardCoin_NoneWithoutCoins() {
        Assert.That(MentoringConfigTable.Empty.AttendRewardCoin(0), Is.EqualTo(0));
    }

    [TestCase(100, 0, false, 100)]
    [TestCase(300, 300, false, 100)]
    [TestCase(300, 400, false, 0)]
    [TestCase(300, 500, false, 0)]
    [TestCase(300, 300, true, 300)]
    [TestCase(-5, 0, true, 0)]
    public void RewardableCoin_UnderTheRolesDailyCap(long coin, long today, bool mentee, long given) {
        Assert.That(MentoringConfigTable.Kms2.RewardableCoin(coin, today, mentee), Is.EqualTo(given));
    }
}
