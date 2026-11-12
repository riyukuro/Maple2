using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Game;

namespace Maple2.Server.Tests.Validators;

// The fame log: the rows of table/fame.xml and table/famelog.xml, the grade of an alliance's points, the log a
// fame gain adds and UserEnv 12 and 13.
public class FameLogRulesTests {
    private const string FameXml = """
        <ms2>
            <fame alliance="GreenHood" grade="1" requirePoint="0" feature="" locale="" />
            <fame alliance="GreenHood" grade="2" requirePoint="10000" feature="" locale="" />
            <fame alliance="GreenHood" grade="2" requirePoint="500" feature="" locale="NA" />
            <fame alliance="GreenHood" grade="3" requirePoint="30000" feature="" locale="" />
            <fame alliance="GreenHood" grade="4" requirePoint="1" feature="Off" locale="" />
            <fame alliance="Nobody" grade="1" requirePoint="0" feature="" locale="" />
            <fame alliance="lumiknight" grade="1" requirePoint="0" />
        </ms2>
        """;

    private const string FameLogXml = """
        <ms2>
            <fame id="1" alliance="greenhood" grade="1" />
            <fame id="2" alliance="GreenHood" grade="1" />
            <fame id="3" alliance="greenhood" grade="2" />
            <fame id="4" alliance="lumiknight" grade="1" />
            <fame id="5" alliance="nobody" grade="1" />
            <fame id="6" alliance="greenhood" grade="1" feature="Off" />
        </ms2>
        """;

    private static FameTable Read() {
        var empty = new XmlDocument();
        empty.LoadXml("<ms2 />");
        var fame = new XmlDocument();
        fame.LoadXml(FameXml);
        var fameLog = new XmlDocument();
        fameLog.LoadXml(FameLogXml);
        return TableMapper.ReadFame(empty, empty, "KR", feature => feature != "Off", fame, fameLog,
            locale => locale.Length == 0 || locale == "KR");
    }

    // Rows of another locale, of a feature that is off and of an unknown alliance are left out.
    [Test]
    public void ReadFame_GradesAndLogs() {
        FameTable table = Read();

        Assert.Multiple(() => {
            Assert.That(table.Grades, Is.EqualTo(new[] {
                new FameTable.Grade(AllianceType.Lumiknights, 1, 0),
                new FameTable.Grade(AllianceType.GreenHoods, 1, 0),
                new FameTable.Grade(AllianceType.GreenHoods, 2, 10000),
                new FameTable.Grade(AllianceType.GreenHoods, 3, 30000),
            }));
            Assert.That(table.Logs, Is.EqualTo(new[] {
                new FameTable.Log(1, AllianceType.GreenHoods, 1),
                new FameTable.Log(2, AllianceType.GreenHoods, 1),
                new FameTable.Log(3, AllianceType.GreenHoods, 2),
                new FameTable.Log(4, AllianceType.Lumiknights, 1),
            }));
        });
    }

    // A table stored before the rows were read has neither.
    [Test]
    public void Table_WithoutRows() {
        var table = new FameTable([], new Dictionary<int, AllianceType>());

        Assert.Multiple(() => {
            Assert.That(table.Grades, Is.Empty);
            Assert.That(table.Logs, Is.Empty);
            Assert.That(FameLogRules.Grade(table, AllianceType.GreenHoods, 5000), Is.EqualTo(0));
            Assert.That(FameLogRules.Pick(table, AllianceType.GreenHoods, 0, [], new Random(1)), Is.EqualTo(0));
        });
    }

    // The grade before the first whose requirePoint is above the points; the highest past them all.
    [TestCase(0, 1)]
    [TestCase(9999, 1)]
    [TestCase(10000, 2)]
    [TestCase(29999, 2)]
    [TestCase(30000, 3)]
    [TestCase(int.MaxValue, 3)]
    public void Grade_ByPoints(int points, int grade) {
        Assert.That(FameLogRules.Grade(Read(), AllianceType.GreenHoods, points), Is.EqualTo(grade));
    }

    // Only the alliance's logs of the grade that are not held; none when all are held.
    [Test]
    public void Pick_OneNotHeldOfTheGrade() {
        FameTable table = Read();
        var seen = new HashSet<int>();
        for (int seed = 0; seed < 50; seed++) {
            seen.Add(FameLogRules.Pick(table, AllianceType.GreenHoods, 1, [], new Random(seed)));
        }

        Assert.Multiple(() => {
            Assert.That(seen, Is.EquivalentTo(new[] { 1, 2 }));
            Assert.That(FameLogRules.Pick(table, AllianceType.GreenHoods, 1, [1], new Random(3)), Is.EqualTo(2));
            Assert.That(FameLogRules.Pick(table, AllianceType.GreenHoods, 1, [1, 2], new Random(3)), Is.EqualTo(0));
            Assert.That(FameLogRules.Pick(table, AllianceType.GreenHoods, 2, [1, 2], new Random(3)), Is.EqualTo(3));
            Assert.That(FameLogRules.Pick(table, AllianceType.GreenHoods, 3, [], new Random(3)), Is.EqualTo(0));
            Assert.That(FameLogRules.Pick(table, AllianceType.DarkWind, 1, [], new Random(3)), Is.EqualTo(0));
        });
    }

    // About one gain in ten adds a log.
    [Test]
    public void Roll_OneInTen() {
        var random = new Random(7);
        int logs = Enumerable.Range(0, 10000).Count(_ => FameLogRules.Roll(random));

        Assert.That(logs, Is.InRange(900, 1100));
    }

    // UserEnv 12, as the client reads it: the count, then each log id and its time, oldest first.
    [Test]
    public void FameLogsPacket() {
        var logs = new Dictionary<int, long> { [256] = 1600000100, [164] = 1600000000 };

        Assert.That(Convert.ToHexString(UserEnvPacket.FameLogs(logs).ToArray()), Does.EndWith(
            "0C" + "02000000" + "A4000000" + "00105E5F00000000" + "00010000" + "64105E5F00000000"));
        Assert.That(Convert.ToHexString(UserEnvPacket.FameLogs(new Dictionary<int, long>()).ToArray()), Does.EndWith("0C" + "00000000"));
    }

    // A log of the alliance's grade is stored and sent by UserEnv 13; with all of them held nothing is sent.
    [Test]
    public void AddFameLog_StoresAndSendsUntilAllAreHeld() {
        var test = new TestGameSession();
        test.SetTable(Read());
        GameSession session = test.Start();
        test.TakeSent();
        var random = new Random(5);

        Assert.That(session.Quest.AddFameLog(AllianceType.GreenHoods, 8500, random), Is.True);
        Assert.That(session.Quest.AddFameLog(AllianceType.GreenHoods, 9000, random), Is.True);
        List<byte[]> sent = test.TakeSent();
        Assert.That(session.Quest.AddFameLog(AllianceType.GreenHoods, 9500, random), Is.False);
        Assert.That(session.Quest.AddFameLog(AllianceType.GreenHoods, 10000, random), Is.True);

        Assert.Multiple(() => {
            Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.UserEnv, SendOp.UserEnv }));
            Assert.That(sent.Select(packet => packet[2]), Is.All.EqualTo(13));
            Assert.That(sent.Select(packet => BitConverter.ToInt32(packet, 3)), Is.EquivalentTo(new[] { 1, 2 }));
            Assert.That(session.Player.Value.Unlock.FameLogs.Keys, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(session.Player.Value.Unlock.FameLogs[3], Is.EqualTo(DateTimeOffset.UtcNow.ToUnixTimeSeconds()).Within(5));
        });
    }

    // The client keys the fame board's list by the time, so logs gained in one second get rising times, also past a
    // held time that is ahead of the clock.
    [Test]
    public void AddFameLog_NeverRepeatsATime() {
        var test = new TestGameSession();
        test.SetTable(Read());
        GameSession session = test.Start();
        IDictionary<int, long> logs = session.Player.Value.Unlock.FameLogs;
        long ahead = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1000;
        var random = new Random(5);

        Assert.That(session.Quest.AddFameLog(AllianceType.GreenHoods, 8500, random), Is.True);
        Assert.That(session.Quest.AddFameLog(AllianceType.GreenHoods, 9000, random), Is.True);
        long[] first = logs.Values.OrderBy(time => time).ToArray();
        logs[logs.Keys.First()] = ahead;
        Assert.That(session.Quest.AddFameLog(AllianceType.GreenHoods, 10000, random), Is.True);

        Assert.Multiple(() => {
            Assert.That(first[1], Is.EqualTo(first[0] + 1));
            Assert.That(first[0], Is.EqualTo(DateTimeOffset.UtcNow.ToUnixTimeSeconds()).Within(5));
            Assert.That(logs[3], Is.EqualTo(ahead + 1));
            Assert.That(logs.Values.Distinct().Count(), Is.EqualTo(3));
        });
    }

    // UserEnv 13: one log id and its time.
    [Test]
    public void FameLogPacket() {
        Assert.That(Convert.ToHexString(UserEnvPacket.FameLog(164, 1600000000).ToArray()), Does.EndWith(
            "0D" + "A4000000" + "00105E5F00000000"));
    }
}
