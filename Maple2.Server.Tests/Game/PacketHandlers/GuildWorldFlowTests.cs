using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Serilog;
using GuildManager = Maple2.Server.Game.Manager.GuildManager;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// What a player gets from the guild window's requests that the World answers (CGuildManager senders): a refusal
// is the client's error line, an accepted request the client's reply, and a request from a player in the wrong
// state (no guild, not the master, already in a guild) is dropped or refused before the World is asked.
public class GuildWorldFlowTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private TestGameSession test = null!;
    private GameSession session = null!;
    private GuildHandler handler = null!;
    private GuildMember self = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.SetTable(new GuildTable(
            new System.Collections.Generic.Dictionary<int, System.Collections.Generic.IReadOnlyDictionary<short, GuildTable.Buff>>(),
            new System.Collections.Generic.Dictionary<int, System.Collections.Generic.IReadOnlyDictionary<int, GuildTable.House>>(),
            new System.Collections.Generic.Dictionary<GuildNpcType, System.Collections.Generic.IReadOnlyDictionary<short, GuildTable.Npc>>(),
            new System.Collections.Generic.Dictionary<short, GuildTable.Property>()));
        test.Start();
        session = test.Session;
        handler = new GuildHandler {
            World = test.World,
            TableMetadata = test.TableMetadata,
            BanWordStorage = null!,
            ServerTableMetadata = test.ServerTableMetadata,
        };
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() => self?.Dispose();

    // The session as a member of guild 7 whose master is `masterIsSelf` or someone else.
    private void Join(bool masterIsSelf = true, GuildPermission permission = GuildPermission.All) {
        var manager = (GuildManager) RuntimeHelpers.GetUninitializedObject(typeof(GuildManager));
        typeof(GuildManager).GetField("session", Any)!.SetValue(manager, session);
        typeof(GuildManager).GetField("tokenSource", Any)!.SetValue(manager, new CancellationTokenSource());
        typeof(GuildManager).GetField("logger", Any)!.SetValue(manager, Log.Logger);
        self = new GuildMember {
            GuildId = 7,
            Info = new PlayerInfo(new CharacterInfo(session.AccountId, session.CharacterId, "Self", "", "", Gender.Male, Job.Newbie, 20), "",
                new AchievementInfo(), []),
            Rank = masterIsSelf ? (byte) 0 : (byte) 1,
            JoinTime = 1000,
        };
        var guild = masterIsSelf
            ? new Guild(7, "Guild", self)
            : new Guild(7, "Guild", session.AccountId + 1, session.CharacterId + 1, "Master");
        guild.Members[self.CharacterId] = self;
        guild.Ranks.Add(new GuildRank { Id = 0, Name = "Master", Permission = GuildPermission.All });
        guild.Ranks.Add(new GuildRank { Id = 1, Name = "Jr", Permission = permission });
        typeof(GuildManager).GetProperty("Guild", Any)!.SetValue(manager, guild);
        session.Guild = manager;
    }

    private void Send(byte mode, Action<IByteWriter>? body = null) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            body?.Invoke(writer);
        }));
    }

    private void WorldDown() {
        test.World.GuildWorld = _ => throw new RpcException(new Status(StatusCode.Unavailable, "down"));
    }

    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());
    private static string Error(GuildError error) => Hex(GuildPacket.Error(error));
    private string[] Answers() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Guild).Select(Convert.ToHexString).ToArray();

    // Guild 2 (disband): only the master asks; a player without a guild is ignored.
    [Test]
    public void Disband_WithoutAGuild_IsIgnored() {
        Send(2);

        Assert.That(test.World.GuildRequests, Is.Empty);
        Assert.That(Answers(), Is.Empty);
    }

    [Test]
    public void Disband_NotTheMaster_NoAuthorityAndNoRequest() {
        Join(masterIsSelf: false);
        Send(2);

        Assert.That(test.World.GuildRequests, Is.Empty);
        Assert.That(Answers(), Is.EqualTo(new[] { Error(GuildError.s_guild_err_no_master) }));
    }

    [Test]
    public void Disband_Accepted_SendsDisbandedAndDropsTheGuild() {
        Join();
        test.World.GuildWorld = _ => new GuildResponse();
        Send(2);

        GuildRequest sent = test.World.GuildRequests.Single();
        Assert.That((sent.RequestorId, sent.Disband.GuildId), Is.EqualTo((session.CharacterId, 7L)));
        Assert.That(Answers().First(), Is.EqualTo(Hex(GuildPacket.Disbanded()))); // then the guild-update line of RemoveGuild
        Assert.That(session.Guild.Guild, Is.Null);
    }

    [Test]
    public void Disband_RefusedByTheWorld_KeepsTheGuildAndShowsTheError() {
        Join();
        test.World.GuildWorld = _ => new GuildResponse { Error = (int) GuildError.s_guild_err_exist_member };
        Send(2);

        Assert.That(Answers(), Is.EqualTo(new[] { Error(GuildError.s_guild_err_exist_member) }));
        Assert.That(session.Guild.Guild, Is.Not.Null);
    }

    [Test]
    public void Disband_WorldDown_Unknown() {
        Join();
        WorldDown();
        Send(2);

        Assert.That(Answers(), Is.EqualTo(new[] { Error(GuildError.s_guild_err_unknown) }));
        Assert.That(session.Guild.Guild, Is.Not.Null);
    }

    // Guild 7 (leave).
    [Test]
    public void Leave_WithoutAGuild_IsIgnored() {
        Send(7);

        Assert.That(test.World.GuildRequests, Is.Empty);
        Assert.That(Answers(), Is.Empty);
    }

    [Test]
    public void Leave_Accepted_SendsLeaveAndDropsTheGuild() {
        Join(masterIsSelf: false);
        test.World.GuildWorld = _ => new GuildResponse();
        Send(7);

        Assert.That(test.World.GuildRequests.Single().Leave.GuildId, Is.EqualTo(7));
        Assert.That(Answers().First(), Is.EqualTo(Hex(GuildPacket.Leave()))); // then the guild-update line of RemoveGuild
        Assert.That(session.Guild.Guild, Is.Null);
    }

    [Test]
    public void Leave_RefusedOrDown_KeepsTheGuild() {
        Join(masterIsSelf: false);
        test.World.GuildWorld = _ => new GuildResponse { Error = (int) GuildError.s_guild_err_no_authority };
        Send(7);
        WorldDown();
        Send(7);

        Assert.That(Answers(), Is.EqualTo(new[] { Error(GuildError.s_guild_err_no_authority), Error(GuildError.s_guild_err_unknown) }));
        Assert.That(session.Guild.Guild, Is.Not.Null);
    }

    // Guild 3 (invite): a name that no character has is the client's "player not found" line; the World is not asked.
    [Test]
    public void Invite_WithoutAGuild_IsIgnored() {
        Send(3, writer => writer.WriteUnicodeString("Anyone"));

        Assert.That(test.World.GuildRequests, Is.Empty);
        Assert.That(Answers(), Is.Empty);
    }

    [Test]
    public void Invite_OfAKnownName_AsksTheWorldAndAnswersWithTheName() {
        Join();
        test.World.GuildWorld = _ => new GuildResponse();
        Send(3, writer => writer.WriteUnicodeString(session.PlayerName));

        GuildRequest sent = test.World.GuildRequests.Single();
        Assert.That((sent.Invite.GuildId, sent.Invite.ReceiverId), Is.EqualTo((7L, session.CharacterId)));
        Assert.That(Answers(), Is.EqualTo(new[] { Hex(GuildPacket.Invited(session.PlayerName)) }));
    }

    [Test]
    public void Invite_RefusedOrDown_ShowsTheErrorLine() {
        Join();
        test.World.GuildWorld = _ => new GuildResponse { Error = (int) GuildError.s_guild_err_no_authority };
        Send(3, writer => writer.WriteUnicodeString(session.PlayerName));
        WorldDown();
        Send(3, writer => writer.WriteUnicodeString(session.PlayerName));

        Assert.That(Answers(), Is.EqualTo(new[] { Error(GuildError.s_guild_err_no_authority), Error(GuildError.s_guild_err_unknown) }));
    }

    // Guild 5 (answer to an invite).
    private static void WriteInvite(IByteWriter writer, long guildId, bool accepted) {
        writer.WriteClass(new GuildInvite { GuildId = guildId, GuildName = "Guild", SenderName = "Master", ReceiverName = "Self" });
        writer.WriteBool(accepted);
    }

    [Test]
    public void RespondInvite_AcceptedInsideAnotherGuild_IsRefusedBeforeTheWorld() {
        Join();
        Send(5, writer => WriteInvite(writer, 9, true));

        Assert.That(test.World.GuildRequests, Is.Empty);
        Assert.That(Answers(), Is.EqualTo(new[] { Error(GuildError.s_guild_err_already_exist) }));
    }

    [Test]
    public void RespondInvite_Rejected_AnswersWithTheInviteAndNoGuild() {
        var invite = new GuildInvite { GuildId = 9, GuildName = "Guild", SenderName = "Master", ReceiverName = "Self" };
        test.World.GuildWorld = _ => new GuildResponse { GuildId = 9 };
        Send(5, writer => WriteInvite(writer, 9, false));

        GuildRequest sent = test.World.GuildRequests.Single();
        Assert.That((sent.RespondInvite.GuildId, sent.RespondInvite.Accepted), Is.EqualTo((9L, false)));
        Assert.That(Answers(), Is.EqualTo(new[] { Hex(GuildPacket.InviteReply(invite, false)) }));
    }

    [Test]
    public void RespondInvite_RefusedOrDown_ShowsTheErrorLine() {
        test.World.GuildWorld = _ => new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        Send(5, writer => WriteInvite(writer, 9, true));
        WorldDown();
        Send(5, writer => WriteInvite(writer, 9, true));

        Assert.That(Answers(), Is.EqualTo(new[] { Error(GuildError.s_guild_err_null_guild), Error(GuildError.s_guild_err_unknown) }));
    }

    // Guild 90, 92 and 99 (the master's skill, its upgrade and the event): the World checks and pays.
    [Test]
    public void UseBuff_AsksTheWorldAndAnswersWithTheBuff() {
        Join();
        test.World.GuildWorld = _ => new GuildResponse();
        Send(90, writer => writer.WriteInt(1234));

        Assert.That(test.World.GuildRequests.Single().UseBuff.BuffId, Is.EqualTo(1234));
        Assert.That(Answers(), Is.EqualTo(new[] { Hex(GuildPacket.UseBuff(1234)) }));
    }

    [TestCase((byte) 90)]
    [TestCase((byte) 92)]
    [TestCase((byte) 99)]
    public void BuffAndEventRequests_WithoutAGuild_AreIgnored(byte mode) {
        Send(mode, writer => writer.WriteInt(1));

        Assert.That(test.World.GuildRequests, Is.Empty);
        Assert.That(Answers(), Is.Empty);
    }

    [TestCase((byte) 90)]
    [TestCase((byte) 92)]
    [TestCase((byte) 99)]
    public void BuffAndEventRequests_RefusedOrDown_ShowTheErrorLine(byte mode) {
        Join();
        test.World.GuildWorld = _ => new GuildResponse { Error = (int) GuildError.s_guild_err_no_authority };
        Send(mode, writer => writer.WriteInt(1));
        WorldDown();
        Send(mode, writer => writer.WriteInt(1));

        Assert.That(Answers(), Is.EqualTo(new[] { Error(GuildError.s_guild_err_no_authority), Error(GuildError.s_guild_err_unknown) }));
    }

    // Guild 92: the upgrade has no reply of its own; the World's fund and skill updates are the answer.
    [Test]
    public void UpgradeBuff_Accepted_SendsNothingItself() {
        Join();
        test.World.GuildWorld = _ => new GuildResponse();
        Send(92, writer => writer.WriteInt(55));

        Assert.That(test.World.GuildRequests.Single().UpgradeBuff.BuffId, Is.EqualTo(55));
        Assert.That(Answers(), Is.Empty);
    }

    [Test]
    public void StartArcade_Accepted_AnswersWithTheEvent() {
        Join();
        test.World.GuildWorld = _ => new GuildResponse();
        Send(99, writer => writer.WriteInt(3));

        Assert.That(test.World.GuildRequests.Single().OpenEvent.EventId, Is.EqualTo(3));
        Assert.That(Answers(), Is.EqualTo(new[] { Hex(GuildPacket.OpenEvent(3)) }));
    }

    // Guild 101 and 102 (house rank and theme): the World's updates are the answer, an error is a line.
    [TestCase((byte) 101)]
    [TestCase((byte) 102)]
    public void HouseUpgrade_RefusedOrDown_ShowsTheErrorLine(byte mode) {
        Join();
        test.World.GuildWorld = _ => new GuildResponse { Error = (int) GuildError.s_guild_err_no_money };
        Send(mode, writer => writer.WriteInt(2));
        WorldDown();
        Send(mode, writer => writer.WriteInt(2));

        Assert.That(Answers(), Is.EqualTo(new[] { Error(GuildError.s_guild_err_no_money), Error(GuildError.s_guild_err_unknown) }));
    }

    [Test]
    public void HouseUpgrade_Accepted_SendsNothingItselfAndCarriesTheTheme() {
        Join();
        test.World.GuildWorld = _ => new GuildResponse();
        Send(101, writer => writer.WriteInt(4));
        Send(102, writer => writer.WriteInt(5));

        Assert.That(test.World.GuildRequests[0].UpgradeHouse.Theme, Is.EqualTo(4));
        Assert.That(test.World.GuildRequests[1].ChangeHouseTheme.Theme, Is.EqualTo(5));
        Assert.That(Answers(), Is.Empty);
    }

    // Guild 82 and 83 (apply and cancel), only from a player without a guild.
    [Test]
    public void SendApplication_InAGuild_HasGuild() {
        Join();
        Send(82, writer => writer.WriteLong(9));

        Assert.That(test.World.GuildRequests, Is.Empty);
        Assert.That(Answers(), Is.EqualTo(new[] { Error(GuildError.s_guild_err_has_guild) }));
    }

    [Test]
    public void SendApplication_BadId_NullGuild() {
        Send(82, writer => writer.WriteLong(0));

        Assert.That(test.World.GuildRequests, Is.Empty);
        Assert.That(Answers(), Is.EqualTo(new[] { Error(GuildError.s_guild_err_null_guild) }));
    }

    [Test]
    public void SendApplication_Accepted_AnswersWithTheRequestIdAndGuildName() {
        test.World.GuildWorld = _ => new GuildResponse { ApplicationId = 31, GuildName = "Guild" };
        Send(82, writer => writer.WriteLong(9));

        Assert.That(test.World.GuildRequests.Single().Apply.GuildId, Is.EqualTo(9));
        Assert.That(Answers(), Is.EqualTo(new[] { Hex(GuildPacket.SendApplication(31, "Guild")) }));
    }

    [Test]
    public void SendApplication_RefusedOrDown_ShowsTheErrorLine() {
        test.World.GuildWorld = _ => new GuildResponse { Error = (int) GuildError.s_guild_err_null_guild };
        Send(82, writer => writer.WriteLong(9));
        WorldDown();
        Send(82, writer => writer.WriteLong(9));

        Assert.That(Answers(), Is.EqualTo(new[] { Error(GuildError.s_guild_err_null_guild), Error(GuildError.s_guild_err_unknown) }));
    }

    [Test]
    public void CancelApplication_InAGuildOrBadId_NoSuchRequest() {
        Send(83, writer => writer.WriteLong(0));
        Join();
        Send(83, writer => writer.WriteLong(5));

        Assert.That(test.World.GuildRequests, Is.Empty);
        string line = Error(GuildError.s_guild_search_null_join_guild_request);
        Assert.That(Answers(), Is.EqualTo(new[] { line, line }));
    }

    [Test]
    public void CancelApplication_Accepted_AnswersWithTheGuild() {
        test.World.GuildWorld = _ => new GuildResponse { GuildId = 9, GuildName = "Guild" };
        Send(83, writer => writer.WriteLong(5));

        Assert.That(test.World.GuildRequests.Single().CancelApplication.Id, Is.EqualTo(5));
        Assert.That(Answers(), Is.EqualTo(new[] { Hex(GuildPacket.CancelApplication(9, "Guild")) }));
    }

    [Test]
    public void CancelApplication_RefusedOrDown_ShowsTheErrorLine() {
        test.World.GuildWorld = _ => new GuildResponse { Error = (int) GuildError.s_guild_search_null_join_guild_request };
        Send(83, writer => writer.WriteLong(5));
        WorldDown();
        Send(83, writer => writer.WriteLong(5));

        Assert.That(Answers(), Is.EqualTo(new[] {
            Error(GuildError.s_guild_search_null_join_guild_request), Error(GuildError.s_guild_err_unknown),
        }));
    }

    // Guild 84 (answer a request): the sender needs the invite right.
    [Test]
    public void RespondApplication_WithoutTheInviteRight_NoAuthority() {
        Join(masterIsSelf: false, permission: GuildPermission.Default & ~GuildPermission.InviteMembers);
        Send(84, writer => {
            writer.WriteLong(5);
            writer.WriteBool(true);
        });

        Assert.That(test.World.GuildRequests, Is.Empty);
        Assert.That(Answers(), Is.EqualTo(new[] { Error(GuildError.s_guild_err_no_authority) }));
    }

    [Test]
    public void RespondApplication_RefusedOrDown_ShowsTheErrorLine() {
        Join();
        test.World.GuildWorld = _ => new GuildResponse { Error = (int) GuildError.s_guild_err_full_member };
        Action<IByteWriter> body = writer => {
            writer.WriteLong(5);
            writer.WriteBool(true);
        };
        Send(84, body);
        WorldDown();
        Send(84, body);

        Assert.That(Answers(), Is.EqualTo(new[] { Error(GuildError.s_guild_err_full_member), Error(GuildError.s_guild_err_unknown) }));
    }
}
