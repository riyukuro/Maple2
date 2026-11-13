using System;
using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// A reward contents row (rewardcontent.xml) with an adventureExpTableID gives that row's fixed amount from the
// server's AdventureIdExpTable as prestige exp (the KR mini game and dungeon rows name 10049 and 10050), sent as
// Prestige 1, and its record (the result window's prestige) carries the amount.
public class RewardContentPrestigeTests {
    private const int Row = 10000009;
    private const int IdExp = 10050;
    private const long Amount = 50000;

    private TestGameSession test = null!;

    private GameSession Start(int idExp, short level) {
        test = new TestGameSession();
        test.SetTable(new RewardContentTable(new Dictionary<int, RewardContentTable.Base> {
            [Row] = new(Row, MesoTableId: 0, ExpTableId: 0, MesoFactor: 0f, ExpFactor: 0f, ItemTableId: 0, PrestigeExpTableId: idExp),
        }, new Dictionary<int, RewardContentTable.Item>(), new Dictionary<int, long>(), new Dictionary<int, Dictionary<int, long>>(),
            new Dictionary<int, long>()));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.SetServerTable(new PrestigeIdExpTable(new Dictionary<int, PrestigeIdExpTable.Entry> {
            [IdExp] = new(IdExp, Amount, ExpType.none),
        }));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { AdventureLevelStartLevel = 50 });
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000173));
        session.Player.Value.Character.Level = level;
        test.TakeSent();
        return session;
    }

    private long PrestigeSent() {
        byte[]? packet = test.TakeSent().Find(packet => TestGameSession.Op(packet) == SendOp.Prestige && packet[2] == 1);
        return packet == null ? 0 : BitConverter.ToInt64(packet, 11);
    }

    [Test]
    public void Reward_GivesTheRowsPrestigeExp() {
        GameSession session = Start(IdExp, 60);
        long before = session.Exp.PrestigeCurrentExp;

        RewardRecord record = session.GetRewardContent(Row);

        Assert.That(record.PrestigeExp, Is.EqualTo(Amount));
        Assert.That(PrestigeSent(), Is.EqualTo(Amount));
        Assert.That(session.Exp.PrestigeCurrentExp - before, Is.EqualTo(Amount));
    }

    [TestCase(IdExp, (short) 40, Amount, TestName = "Reward_BelowTheStartLevel_NoPrestige")]
    [TestCase(9999, (short) 60, 0L, TestName = "Reward_UnknownIdExp_NoPrestige")]
    public void Reward_PrestigeOnlyFromAKnownRowAtTheStartLevel(int idExp, short level, long recorded) {
        GameSession session = Start(idExp, level);
        long before = session.Exp.PrestigeCurrentExp;

        RewardRecord record = session.GetRewardContent(Row);

        Assert.That(record.PrestigeExp, Is.EqualTo(recorded));
        Assert.That(PrestigeSent(), Is.Zero);
        Assert.That(session.Exp.PrestigeCurrentExp, Is.EqualTo(before));
    }
}
