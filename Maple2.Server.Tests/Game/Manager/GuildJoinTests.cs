using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.World.Service;
using Maple2.Server.Game.Packets;
using Serilog;
using GuildManager = Maple2.Server.Game.Manager.GuildManager;

namespace Maple2.Server.Tests.Game.Manager;

// What a member's session sends when the channel service tells it of a new member.
public class GuildJoinTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static GuildMember Member(long characterId, string name, byte rank) {
        var info = new PlayerInfo(new CharacterInfo(characterId + 100, characterId, name, "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []) {
            Channel = 1,
            MapId = 2000023,
            GearScore = 1995,
        };
        return new GuildMember { GuildId = 7, Info = info, Rank = rank, JoinTime = 1000 };
    }

    private static GuildManager Manager(TestGameSession test, Guild? guild) {
        var manager = (GuildManager) RuntimeHelpers.GetUninitializedObject(typeof(GuildManager));
        typeof(GuildManager).GetField("session", Any)!.SetValue(manager, test.Session);
        typeof(GuildManager).GetField("tokenSource", Any)!.SetValue(manager, new CancellationTokenSource());
        typeof(GuildManager).GetField("logger", Any)!.SetValue(manager, Log.Logger);
        typeof(GuildManager).GetProperty("Guild", Any)!.SetValue(manager, guild);
        return manager;
    }

    private static Guild OneMemberGuild() {
        GuildMember leader = Member(10, "Leader", 0);
        var guild = new Guild(7, "Guild", leader.AccountId, leader.CharacterId, leader.Name);
        guild.Members[leader.CharacterId] = leader;
        return guild;
    }

    // KMS2 sends the other members Guild 18 (the notice with the member row) and then Guild 32 (the member's data).
    [Test]
    public void AddMember_SendsTheJoinNoticeThenTheMemberData() {
        var test = new TestGameSession();
        test.Start();
        test.TakeSent();
        Guild guild = OneMemberGuild();
        GuildManager manager = Manager(test, guild);
        GuildMember joiner = Member(11, "Joiner", 5);

        Assert.That(manager.AddMember("Leader", joiner), Is.True);

        Assert.That(guild.Members.Keys, Is.EquivalentTo(new long[] { 10, 11 }));
        byte[][] sent = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Guild).ToArray();
        Assert.That(sent.Select(System.Convert.ToHexString), Is.EqualTo(new[] {
            System.Convert.ToHexString(GuildPacket.Joined("Leader", joiner).ToArray()),
            System.Convert.ToHexString(GuildPacket.UpdateMember(joiner.Info).ToArray()),
        }));
        // The member row carries the type byte 3 the client's join reader needs to redraw the list, and the gear score.
        byte[] joined = GuildPacket.Joined("Leader", joiner).ToArray();
        int row = 2 + 1 + (2 + 12) + (2 + 12) + 1;
        Assert.That(joined[row], Is.EqualTo(3));
        Assert.That(joined[row + 1], Is.EqualTo(5));

        // A member the session already has is not announced again.
        Assert.That(manager.AddMember("Leader", Member(11, "Joiner", 5)), Is.False);
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Guild), Is.Empty);
    }

    // KMS2 answers a join request with Guild 82 carrying the new request's id, not the guild's.
    [Test]
    public void Apply_AnswersWithTheRequestId() {
        var test = new TestGameSession();
        test.Start();
        test.Session.Guild = Manager(test, null);
        test.World.GuildWorld = request => new GuildResponse {
            GuildId = request.Apply.GuildId,
            ApplicationId = 818914,
            GuildName = "Guild",
        };
        test.TakeSent();
        var handler = new GuildHandler {
            World = test.World, TableMetadata = null!, BanWordStorage = null!, ServerTableMetadata = test.ServerTableMetadata,
        };

        handler.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(82);
            writer.WriteLong(7);
        }));

        Assert.That(test.World.GuildRequests.Single().Apply.GuildId, Is.EqualTo(7));
        byte[][] sent = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Guild).ToArray();
        Assert.That(sent.Select(System.Convert.ToHexString), Is.EqualTo(new[] {
            System.Convert.ToHexString(GuildPacket.SendApplication(818914, "Guild").ToArray()),
        }));
    }

    [Test]
    public void AddMember_WithoutAGuildSendsNothing() {
        var test = new TestGameSession();
        test.Start();
        test.TakeSent();
        GuildManager manager = Manager(test, null);

        Assert.That(manager.AddMember("Leader", Member(11, "Joiner", 5)), Is.False);
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Guild), Is.Empty);
    }

    // Buddy 19: the count that ends the list leaves out the requests the player sent and the blocked entries.
    [Test]
    public void BuddyListCount_LeavesOutSentRequestsAndBlocked() {
        Assert.That(BuddyManager.ListCount([BuddyType.Default, BuddyType.OutRequest, BuddyType.Blocked, BuddyType.InRequest, BuddyType.Default]),
            Is.EqualTo(3));
        Assert.That(BuddyManager.ListCount([BuddyType.OutRequest, BuddyType.Blocked]), Is.EqualTo(0));
        Assert.That(BuddyManager.ListCount([]), Is.EqualTo(0));
    }
}
