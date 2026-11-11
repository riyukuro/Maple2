using System.Collections.Generic;
using System.Xml;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;
using TriggerActions = Maple2.Server.Game.Trigger.Helpers.Trigger;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Game.Dungeon;
using Maple2.Server.Game.Manager;

namespace Maple2.Server.Tests.Game.Util;

// The Colosseum continue-play: talk_npc, the npc's choice, the reward-count conditions and
// set_user_value_from_dungeon_reward_count. Made-up ids.
public class ColosseumContinueTests {
    private static IAction Action(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        XmlElement node = document.DocumentElement!;
        return TriggerFunctionMapping.ActionMap[node.GetAttribute("name")](node.Attributes);
    }

    [Test]
    public void Actions_Parse() {
        Assert.That(Action("""<action name="talk_npc" spawn_id="901" />"""), Is.TypeOf<TriggerActions.TalkNpc>());
        Assert.That(Action("""<action name="set_user_value_from_dungeon_reward_count" key="ClearRound" dungeon_reward_id="29900001" />"""),
            Is.TypeOf<TriggerActions.SetUserValueFromDungeonRewardCount>());
    }

    [Test]
    public void RewardCount_IsTheCountInTheCooldownPeriod() {
        // DungeonRecord::GetBonusRewardCount: the count while the period runs, none after it.
        var record = new DungeonRecord(29900001) { UnionClears = 5, UnionCooldownTimestamp = 1000 };
        Assert.That(DungeonManager.RewardCount(record, 999), Is.EqualTo(5));
        Assert.That(DungeonManager.RewardCount(record, 1000), Is.EqualTo(5));
        Assert.That(DungeonManager.RewardCount(record, 1001), Is.EqualTo(0));
        Assert.That(DungeonManager.RewardCount(null, 0), Is.EqualTo(0));
    }

    [Test]
    public void RewardCondition_ReceivedAndComplete() {
        // receivedRewardDungeonID: some reward was taken; completeRewardDungeonID: no count left of the room's.
        Assert.That(DungeonManager.RewardCondition(0, 13, complete: false), Is.False);
        Assert.That(DungeonManager.RewardCondition(1, 13, complete: false), Is.True);
        Assert.That(DungeonManager.RewardCondition(12, 13, complete: true), Is.False);
        Assert.That(DungeonManager.RewardCondition(13, 13, complete: true), Is.True);
    }

    [Test]
    public void NpcScriptCondition_RewardDungeonAttributesAreRead() {
        var document = new XmlDocument();
        document.LoadXml("""
            <ms2>
                <condition npcID="11900001" scriptID="20" receivedRewardDungeonID="29900001" />
                <condition npcID="11900001" scriptID="40" completeRewardDungeonID="29900001" />
                <condition npcID="11900002" scriptID="1" maid_auth="1" />
            </ms2>
            """);
        Dictionary<(int NpcId, int ScriptId), (int Received, int Complete)> rows = ServerTableMapper.NpcScriptRewardDungeons(document);
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[(11900001, 20)], Is.EqualTo((29900001, 0)));
        Assert.That(rows[(11900001, 40)], Is.EqualTo((0, 29900001)));
    }
}
