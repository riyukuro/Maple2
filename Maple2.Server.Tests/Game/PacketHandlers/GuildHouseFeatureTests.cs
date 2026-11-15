using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.World.Service;
using Serilog;
using GuildManager = Maple2.Server.Game.Manager.GuildManager;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Guild 101 / 102 (house rank, house theme) are sent only with GuildHouse01 on (guildmanager.cpp:940/971): with it
// off the World server gets no house request.
public class GuildHouseFeatureTests {
    private const int Town = 2000062;
    private const int HouseMap = 2000256;
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static TestGameSession Start(bool houseOn) {
        var test = new TestGameSession();
        test.Start();
        var manager = (GuildManager) RuntimeHelpers.GetUninitializedObject(typeof(GuildManager));
        typeof(GuildManager).GetField("session", Any)!.SetValue(manager, test.Session);
        typeof(GuildManager).GetField("tokenSource", Any)!.SetValue(manager, new CancellationTokenSource());
        typeof(GuildManager).GetField("logger", Any)!.SetValue(manager, Log.Logger);
        typeof(GuildManager).GetProperty("Guild", Any)!.SetValue(manager,
            new Guild(7, "Guild", test.AccountId, test.CharacterId, "Master"));
        test.Session.Guild = manager;
        if (!houseOn) {
            test.SetFeaturesOff(GuildHandler.HouseFeature);
        }
        return test;
    }

    private static void Send(TestGameSession test, byte command, int theme) {
        new GuildHandler {
            World = test.World, TableMetadata = null!, BanWordStorage = null!, ServerTableMetadata = test.ServerTableMetadata,
        }.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteInt(theme);
        }));
    }

    [TestCase((byte) 101, true, ExpectedResult = 1)]
    [TestCase((byte) 101, false, ExpectedResult = 0)]
    [TestCase((byte) 102, true, ExpectedResult = 1)]
    [TestCase((byte) 102, false, ExpectedResult = 0)]
    public int HouseRequestsNeedTheFeature(byte command, bool houseOn) {
        TestGameSession test = Start(houseOn);

        Send(test, command, 2);

        return test.World.GuildRequests.Count(request =>
            request.GuildCase is GuildRequest.GuildOneofCase.UpgradeHouse or GuildRequest.GuildOneofCase.ChangeHouseTheme);
    }

    // Guild 115 / 116 (the guild vs game ticket and its withdrawal) come only from the event buttons, which are
    // disabled with GuildMinigame01 off: the World gets no ticket request.
    [TestCase((byte) 115, false, ExpectedResult = 0)]
    [TestCase((byte) 116, true, ExpectedResult = 1)]
    [TestCase((byte) 116, false, ExpectedResult = 0)]
    public int VsGameRequestsNeedTheFeature(byte command, bool on) {
        TestGameSession test = Start(true);
        // The player holds the guild's mini game right.
        Guild guild = test.Session.Guild.Guild!;
        guild.Ranks.Add(new GuildRank { Id = 0, Name = "Master", Permission = GuildPermission.All });
        guild.Members[test.CharacterId] = new GuildMember { GuildId = guild.Id, Info = new PlayerInfo(new CharacterInfo(test.AccountId, test.CharacterId, "Master", "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []), Rank = 0 };
        if (!on) {
            test.SetFeaturesOff(GuildHandler.VsGameFeature);
        }

        new GuildHandler {
            World = test.World, TableMetadata = null!, BanWordStorage = null!, ServerTableMetadata = test.ServerTableMetadata,
        }.Handle(test.Session, TestGameSession.Packet(writer => writer.WriteByte(command)));

        return test.World.GuildRequests.Count(request =>
            request.GuildCase is GuildRequest.GuildOneofCase.VsGameRegister or GuildRequest.GuildOneofCase.VsGameUnregister);
    }
    // Guild 103, the house entry: the client sends it only out of its battle stance (guildmanager.cpp:911). In a
    // stance it must show (held past the race grace) the move is dropped; a stance just turned on, or none, moves.
    // Before, the member moved mid-fight.
    [TestCase(true, true, ExpectedResult = 0)]
    [TestCase(true, false, ExpectedResult = 1)]
    [TestCase(false, false, ExpectedResult = 1)]
    public int HouseEntry_InABattleStance(bool inBattle, bool held) {
        TestGameSession test = Start(true);
        test.SetTable(new GuildTable(
            new System.Collections.Generic.Dictionary<int, System.Collections.Generic.IReadOnlyDictionary<short, GuildTable.Buff>>(),
            new System.Collections.Generic.Dictionary<int, System.Collections.Generic.IReadOnlyDictionary<int, GuildTable.House>> {
                [test.Session.Guild.Guild!.HouseRank] = new System.Collections.Generic.Dictionary<int, GuildTable.House> {
                    [test.Session.Guild.Guild!.HouseTheme] = new(HouseMap, 1, 0, 0, []),
                },
            },
            new System.Collections.Generic.Dictionary<GuildNpcType, System.Collections.Generic.IReadOnlyDictionary<short, GuildTable.Npc>>(),
            new System.Collections.Generic.Dictionary<short, GuildTable.Property>()));
        test.EnterMap(TestGameSession.Map(Town));
        if (inBattle) {
            test.Session.Player.InBattle = true;
            if (held) {
                test.Session.Player.BackdateBattleStart(BattleStanceRules.RaceGraceMs + 1000);
            }
        }

        new GuildHandler {
            World = test.World, TableMetadata = test.TableMetadata, BanWordStorage = null!, ServerTableMetadata = test.ServerTableMetadata,
        }.Handle(test.Session, TestGameSession.Packet(writer => writer.WriteByte(103)));

        return test.World.Moves.Count;
    }
}
