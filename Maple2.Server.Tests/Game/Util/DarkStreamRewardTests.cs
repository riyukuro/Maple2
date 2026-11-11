using System;
using System.Collections.Generic;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game.Util;

// table/darkstream.xml and the reward of a run. Made-up rows and ids.
public class DarkStreamRewardTests {
    private static XmlDocument Document() {
        var document = new XmlDocument();
        document.LoadXml("""
            <ms2>
              <reward round="0" expRate="0.1" meso="100" habi="10" items="" />
              <reward round="2" expRate="0.5" meso="2000" habi="40" items="" />
              <reward round="2" expRate="1.5" meso="3000" habi="60" items="91000001,3,2" locale="XX" />
              <reward round="4" expRate="2" meso="4000" habi="80" items="91000001,3,4;91000002,1,1" locale="XX" />
              <reward round="5" expRate="9" meso="9000" habi="90" items="" locale="YY" />
            </ms2>
            """);
        return document;
    }

    private static bool LocaleXX(XmlElement node) => node.GetAttribute("locale") is "" or "XX";

    [Test]
    public void LaterLocaleRow_ReplacesTheRound() {
        Dictionary<int, DarkStreamTable.Entry> rows = TableMapper.ReadDarkStreamRewards(Document(), LocaleXX);
        Assert.That(rows.Keys, Is.EquivalentTo(new[] { 0, 2, 4 }));
        Assert.That(rows[2].ExpRate, Is.EqualTo(1.5f));
        Assert.That(rows[2].Meso, Is.EqualTo(3000));
        Assert.That(rows[2].Habi, Is.EqualTo(60));
        Assert.That(rows[2].Items, Is.EqualTo(new[] { new RewardItem(91000001, 3, 2) }));
        Assert.That(rows[0].Items, Is.Empty);
    }

    [Test]
    public void Items_AreIdRankCountTriples() {
        Dictionary<int, DarkStreamTable.Entry> rows = TableMapper.ReadDarkStreamRewards(Document(), LocaleXX);
        Assert.That(rows[4].Items, Is.EqualTo(new[] { new RewardItem(91000001, 3, 4), new RewardItem(91000002, 1, 1) }));
    }

    [Test]
    public void Items_NotTriples_Throw() {
        var document = new XmlDocument();
        document.LoadXml("""<ms2><reward round="1" expRate="1" meso="1" habi="1" items="91000001,3" /></ms2>""");
        Assert.Throws<FormatException>(() => TableMapper.ReadDarkStreamRewards(document, _ => true));
    }

    [Test]
    public void Reward_ExpIsRoomExpTimesRate() {
        Dictionary<int, DarkStreamTable.Entry> rows = TableMapper.ReadDarkStreamRewards(Document(), LocaleXX);
        DarkStreamGame.Reward? reward = DarkStreamGame.RewardOf(rows, 2, 1000);
        Assert.That(reward, Is.Not.Null);
        Assert.That(reward!.Value.Exp, Is.EqualTo(1500));
        Assert.That(reward.Value.Meso, Is.EqualTo(3000));
        Assert.That(reward.Value.Habi, Is.EqualTo(60));
        Assert.That(reward.Value.Items, Has.Count.EqualTo(1));
    }

    [Test]
    public void Reward_WithoutItsRow_TakesTheRowBelow() {
        Dictionary<int, DarkStreamTable.Entry> rows = TableMapper.ReadDarkStreamRewards(Document(), LocaleXX);
        Assert.That(DarkStreamGame.RewardOf(rows, 3, 1000)!.Value.Meso, Is.EqualTo(3000));
        Assert.That(DarkStreamGame.RewardOf(rows, 1, 1000)!.Value.Exp, Is.EqualTo(100));
        Assert.That(DarkStreamGame.RewardOf(new Dictionary<int, DarkStreamTable.Entry>(), 3, 1000), Is.Null);
    }

    [Test]
    public void ClearRound_KeepsTheRoundsCleared() {
        var game = new DarkStreamGame(3);
        Assert.That(game.ClearRound(1), Is.False);
        Assert.That(game.RoundsCleared, Is.EqualTo(1));
        Assert.That(game.ClearRound(3), Is.True);
        Assert.That(game.RoundsCleared, Is.EqualTo(3));
        Assert.That(game.ClearRound(4), Is.False);
        Assert.That(game.RoundsCleared, Is.EqualTo(3));
    }
}
