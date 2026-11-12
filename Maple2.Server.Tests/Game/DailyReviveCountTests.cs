using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// The day's paid revives (InstantReviveCount, at most Lua.GetMesoRevivalDailyMaxCount() a day) are kept in the
// character's character-config row and cleared only by the daily reset. A new login (a character switch) reads the
// stored count, and LoadRevival sends it in RevivalCount, the only place the client learns it
// (CSceneStateGame::OnRevivalCount).
public class DailyReviveCountTests {
    private static ConfigManager Login(TestGameSession test) {
        GameSession session = test.Start();
        test.SetTable(new JobTable(new Dictionary<JobCode, JobTable.Entry> {
            [JobCode.Newbie] = new(new JobTable.Tutorial(0, 0, 0, [], [], [], []), new Dictionary<SkillRank, JobTable.Skill[]> {
                [SkillRank.Basic] = [], [SkillRank.Awakening] = [],
            }, []),
        }));
        test.SetTable(new PrestigeLevelRewardTable(new Dictionary<int, PrestigeLevelRewardMetadata>()));
        using GameStorage.Request db = test.Storage.Context();
        var config = new ConfigManager(db, session);
        session.Config = config;
        return config;
    }

    [Test]
    public void TheDaysRevivesHoldAcrossALogin() {
        var test = new TestGameSession();
        ConfigManager first = Login(test);
        Assert.That(first.InstantReviveCount, Is.EqualTo(0));
        first.InstantReviveCount = 3;
        using (GameStorage.Request db = test.Storage.Context()) {
            first.Save(db);
        }

        // Back from character select: the count is still 3, so no paid revive is left today.
        ConfigManager second = Login(test);
        Assert.That(second.InstantReviveCount, Is.EqualTo(3));
        test.TakeSent();
        second.AddInstantReviveCount(0);
        byte[] sent = test.TakeSent().Single();
        Assert.That(TestGameSession.Op(sent), Is.EqualTo(SendOp.RevivalCount));
        var reader = new ByteReader(sent, 2);
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(Maple2.Server.Game.LuaFunctions.Lua.GetMesoRevivalDailyMaxCount() - second.InstantReviveCount, Is.EqualTo(0));

        // The daily reset clears it.
        second.AddInstantReviveCount(-1);
        using (GameStorage.Request db = test.Storage.Context()) {
            second.Save(db);
        }
        Assert.That(Login(test).InstantReviveCount, Is.EqualTo(0));
    }
}
