using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The prestige missions are weekly ($s_adventure_level_weekly_mission_desc): the weekly reset starts them again, so each
// week's levels can pay the boxes once more.
public class PrestigeWeeklyMissionTests {
    private TestGameSession test = null!;

    // KR adventurelevelmission: gain 1, 2 and 4 prestige levels for the reward boxes 20302615, 20302616 and 20302617.
    private GameSession Login() {
        test = new TestGameSession();
        var missions = new Dictionary<int, PrestigeMissionMetadata>();
        foreach ((int id, int count, int box) in new[] { (1, 1, 20302615), (2, 2, 20302616), (3, 4, 20302617) }) {
            test.Register(TestGameSession.Metadata(box));
            missions[id] = new PrestigeMissionMetadata(id, count, new ItemComponent(box, 4, 1, ItemTag.None));
        }
        test.SetTable(new PrestigeMissionTable(missions));
        GameSession session = test.Start();
        session.Exp.PrestigeMissions.Clear();
        foreach (int id in missions.Keys) {
            session.Exp.PrestigeMissions.Add(new PrestigeMission(id));
        }
        test.TakeSent();
        return session;
    }

    private void Claim(GameSession session, int id) {
        new PrestigeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteInt(id);
        }));
    }

    private static int Held(GameSession session, int box) => session.Item.Inventory.Find(box).Sum(item => item.Amount);

    private static void Gain(GameSession session, int levels) {
        foreach (PrestigeMission mission in session.Exp.PrestigeMissions) {
            mission.GainedLevels += levels;
        }
    }

    [Test]
    public void WeeklyReset_StartsTheMissionsAgain() {
        GameSession session = Login();
        Gain(session, 4);
        Claim(session, 1);
        Claim(session, 2);
        Claim(session, 3);
        Assume.That(new[] { 20302615, 20302616, 20302617 }.Select(box => Held(session, box)), Is.EqualTo(new[] { 1, 1, 1 }));
        test.TakeSent();

        session.WeeklyReset();

        Assert.That(session.Exp.PrestigeMissions.Select(mission => (mission.Id, mission.GainedLevels, mission.Awarded)),
            Is.EqualTo(new[] { (1L, 0L, false), (2L, 0L, false), (3L, 0L, false) }));
        // Prestige 7 with the new list, as at login.
        byte[] load = test.TakeSent().Single(packet => TestGameSession.Op(packet) == SendOp.Prestige);
        Assert.That(Convert.ToHexString(load), Is.EqualTo(Convert.ToHexString(PrestigePacket.LoadMissions(session.Player.Value.Account).ToArray())));
        Assert.That(load[2], Is.EqualTo(7));

        // A new week: nothing before a level, the first box again after one, the second after two.
        Claim(session, 1);
        Assert.That(Held(session, 20302615), Is.EqualTo(1));
        Gain(session, 2);
        Claim(session, 1);
        Claim(session, 2);
        Claim(session, 3);
        Assert.That(new[] { 20302615, 20302616, 20302617 }.Select(box => Held(session, box)), Is.EqualTo(new[] { 2, 2, 1 }));
    }

    // A mission whose levels are not met yet is counted again from none as well.
    [Test]
    public void WeeklyReset_DropsTheLevelsCountedTowardAMission() {
        GameSession session = Login();
        Gain(session, 3);
        Claim(session, 1);

        session.WeeklyReset();
        Gain(session, 1);
        Claim(session, 3);

        Assert.That(Held(session, 20302617), Is.EqualTo(0));
        Assert.That(session.Exp.PrestigeMissions.Single(mission => mission.Id == 3).Awarded, Is.False);
    }
}
