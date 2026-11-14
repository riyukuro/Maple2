using System;
using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// A reward contents row (rewardcontent.xml) with an adventureExpTableID gives that row's fixed amount from the
// server's AdventureIdExpTable as prestige exp (the KR field war rows name 10049, the Colosseum round rows 10050), sent as
// Prestige 1, and its record (the result window's prestige) carries the amount. That amount is the row's whole
// prestige (chosen here): its exp brings none of the message's type (the client's reward of a row has a single
// adventure exp, rewardcontentstable.cpp:222).
public class RewardContentPrestigeTests {
    private const int Row = 10000009;
    private const int IdExp = 10050;
    private const long Amount = 50000;
    private const int StaticExp = 100003; // KR: the Colosseum rounds' exp row
    private const long ClearRow = 7500; // the test's dungeonClear row (KR 75000)

    private TestGameSession test = null!;

    private GameSession Start(int idExp, short level, int expTableId = 0) {
        test = new TestGameSession();
        test.SetTable(new RewardContentTable(new Dictionary<int, RewardContentTable.Base> {
            [Row] = new(Row, MesoTableId: 0, ExpTableId: expTableId, MesoFactor: 0f, ExpFactor: 0f, ItemTableId: 0, PrestigeExpTableId: idExp),
        }, new Dictionary<int, RewardContentTable.Item>(), new Dictionary<int, long>(), new Dictionary<int, Dictionary<int, long>>(),
            new Dictionary<int, long> { [StaticExp] = 1000 }));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long> { [ExpType.dungeonClear] = ClearRow }));
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

    // A Colosseum round (KR 10000009: exp, meso, items and 10050) under the dungeon message: the 50000 of 10050,
    // not that plus the dungeonClear row. A row whose id is not in the table keeps the message's row.
    [TestCase(IdExp, Amount, TestName = "Reward_WithExp_GivesTheFixedAmountOnly")]
    [TestCase(9999, ClearRow, TestName = "Reward_WithExp_UnknownIdExp_GivesTheMessageRow")]
    public void Reward_WithExp_PrestigeOnce(int idExp, long gained) {
        GameSession session = Start(idExp, 60, StaticExp);
        long before = session.Exp.PrestigeCurrentExp;

        (RewardRecord record, Action give) = session.PrepareRewardContent(Row, ExpMessageCode.dungeonRelative);
        give();

        Assert.That(record.Exp, Is.EqualTo(1000));
        Assert.That(session.Exp.PrestigeCurrentExp - before, Is.EqualTo(gained));
    }
}
