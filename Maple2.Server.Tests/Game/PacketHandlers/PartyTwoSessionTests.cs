using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Sync;
using Maple2.Server.Game;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Maple2.Tools.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ChannelPartyRequest = Maple2.Server.Channel.Service.PartyRequest;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A party invite between two sessions that share one character table. The handlers and the channel service are the
// server's; the world's party state (one party, its members, who is told) is scripted here.
public class PartyTwoSessionTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int PartyId = 5;
    private const byte Error = 0;
    private const byte Joined = 2;
    private const byte Load = 9;
    private const byte Invited = 11;

    private TestGameSession inviterTest = null!;
    private TestGameSession guestTest = null!;
    private PartyHandler inviterHandler = null!;
    private PartyHandler guestHandler = null!;
    private ChannelService channel = null!;
    private PartyInfo party = null!;
    private GameSession inviter => inviterTest.Session;
    private GameSession guest => guestTest.Session;

    [SetUp]
    public void Login() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        inviterTest = new TestGameSession(options);
        guestTest = new TestGameSession(options);
        foreach (TestGameSession test in new[] { inviterTest, guestTest }) {
            test.AddMap(TestGameSession.Map(2000023));
            test.Start();
            test.EnterMap(TestGameSession.Map(2000023));
        }

        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        var sessions = new ConcurrentDictionary<long, GameSession>();
        sessions[inviter.CharacterId] = inviter;
        sessions[guest.CharacterId] = guest;
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, sessions);
        // Both sessions are on one game server; its player info comes from the inviter's world.
        channel = new ChannelService(server, inviter.PlayerInfo, inviterTest.Storage, null!, null!, null!);

        foreach (TestGameSession test in new[] { inviterTest, guestTest }) {
            test.World.AddPlayer(inviter.AccountId, inviter.CharacterId, inviter.PlayerName);
            test.World.AddPlayer(guest.AccountId, guest.CharacterId, guest.PlayerName);
            test.World.PartyWorld = World;
            GameSession session = test.Session;
            using (GameStorage.Request db = test.Storage.Context()) {
                session.Buddy = new BuddyManager(db, session);
            }
            session.Party = new PartyManager(test.World, session);
            test.TakeSent();
        }
        inviterHandler = new PartyHandler { World = inviterTest.World, ServerTableMetadata = inviterTest.ServerTableMetadata };
        guestHandler = new PartyHandler { World = guestTest.World, ServerTableMetadata = guestTest.ServerTableMetadata };
    }

    // What the world does with the three requests of an invite.
    private PartyResponse World(PartyRequest request) {
        switch (request.PartyCase) {
            case PartyRequest.PartyOneofCase.Create:
                party = new PartyInfo {
                    Id = PartyId,
                    LeaderAccountId = inviter.AccountId,
                    LeaderCharacterId = request.RequestorId,
                    LeaderName = inviter.PlayerName,
                    CreationTime = 1000,
                };
                party.Members.Add(new PartyInfo.Types.Member { CharacterId = request.RequestorId, JoinTime = 1000 });
                return new PartyResponse { Party = party };
            case PartyRequest.PartyOneofCase.Invite:
                var invite = new ChannelPartyRequest {
                    PartyId = request.Invite.PartyId,
                    Invite = new ChannelPartyRequest.Types.Invite { SenderId = inviter.CharacterId, SenderName = inviter.PlayerName },
                };
                invite.ReceiverIds.Add(request.Invite.ReceiverId);
                return new PartyResponse {
                    PartyId = request.Invite.PartyId,
                    Error = channel.Party(invite, null!).GetAwaiter().GetResult().Error,
                };
            case PartyRequest.PartyOneofCase.RespondInvite:
                if (request.RespondInvite.Reply != (int) PartyInviteResponse.Accept) {
                    return new PartyResponse { PartyId = PartyId };
                }
                var add = new ChannelPartyRequest {
                    PartyId = PartyId,
                    AddMember = new ChannelPartyRequest.Types.AddMember { CharacterId = request.RequestorId, JoinTime = 2000 },
                };
                add.ReceiverIds.Add(party.Members.Select(member => member.CharacterId));
                channel.Party(add, null!).GetAwaiter().GetResult();
                party.Members.Add(new PartyInfo.Types.Member { CharacterId = request.RequestorId, JoinTime = 2000 });
                return new PartyResponse { Party = party };
            default:
                return new PartyResponse { Error = (int) PartyError.s_party_err_not_found };
        }
    }

    private void Invite(string name) {
        inviterHandler.Handle(inviter, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteUnicodeString(name);
        }));
    }

    private void Reply(PartyInviteResponse reply) {
        guestHandler.Handle(guest, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteUnicodeString(inviter.PlayerName);
            writer.WriteByte((byte) reply);
            writer.WriteInt(PartyId);
        }));
    }

    private static byte[][] Party(TestGameSession test) {
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Party).Select(TestGameSession.NoClock).ToArray();
    }

    // The sent packets and the expected ones are compared without the time of writing in a member's info: the
    // expected packet is built after the send, and the second can change in between.
    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(TestGameSession.NoClock(packet.ToArray()));

    // A session made again (map change through migration, channel change, relog) loads its party from World. A party
    // with a finder listing sends it in the Load's last row, which the client keeps as its own listing
    // (OnPartyInfo, partymanager.cpp:1131-1135): the owner's button stays "Cancel Recruitment".
    [Test]
    public void LoadedParty_SendsItsListingInTheLoad() {
        var info = new PartyInfo {
            Id = PartyId,
            LeaderAccountId = inviter.AccountId,
            LeaderCharacterId = inviter.CharacterId,
            LeaderName = inviter.PlayerName,
            CreationTime = 1000,
            PartySearch = new PartySearchInfo {
                Id = 7,
                PartyId = PartyId,
                Name = "come",
                Size = 10,
                MemberCount = 1,
                NoApproval = false,
                LeaderAccountId = inviter.AccountId,
                LeaderCharacterId = inviter.CharacterId,
                LeaderName = inviter.PlayerName,
                CreationTime = 1000,
            },
        };
        info.Members.Add(new PartyInfo.Types.Member { CharacterId = inviter.CharacterId, JoinTime = 1000 });

        Assert.That(inviter.Party.SetParty(info), Is.True);

        Assert.That(inviter.Party.Party!.Search, Is.Not.Null);
        Assert.That(inviter.Party.Party.Search!.Id, Is.EqualTo(7));
        byte[][] sent = Party(inviterTest);
        Assert.That(sent.Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(PartyPacket.Load(inviter.Party.Party, false)),
        }));
        var listing = new Maple2.PacketLib.Tools.ByteWriter();
        listing.WriteBool(true);
        listing.WriteClass<Maple2.Model.Game.Party.PartySearch>(inviter.Party.Party.Search);
        Assert.That(Convert.ToHexString(sent[0]), Does.EndWith(Convert.ToHexString(listing.ToArray())), "the listing row ends the Load");
    }

    [Test]
    public void Invite_ReachesTheGuestAndAcceptJoinsBoth() {
        Assume.That(inviter.CharacterId, Is.Not.EqualTo(guest.CharacterId));

        Invite(guest.PlayerName);

        // The inviter is alone in a new party; the guest is asked.
        Assert.That(inviter.Party.Party, Is.Not.Null);
        Assert.That(inviter.Party.Party!.Members.Keys, Is.EqualTo(new[] { inviter.CharacterId }));
        // One Load, with the join byte set.
        Assert.That(Party(inviterTest).Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(PartyPacket.Load(inviter.Party.Party, true)),
        }));
        Assert.That(Party(guestTest).Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(PartyPacket.Invite(PartyId, inviter.PlayerName)),
        }));
        Assert.That(guest.Party.Party, Is.Null);

        Reply(PartyInviteResponse.Accept);

        Assert.That(guest.Party.Party, Is.Not.Null);
        Assert.That(guest.Party.Party!.Members.Keys, Is.EquivalentTo(new[] { inviter.CharacterId, guest.CharacterId }));
        Assert.That(guest.Party.Party.LeaderCharacterId, Is.EqualTo(inviter.CharacterId));
        Assert.That(guest.Player.Value.Character.PartyId, Is.EqualTo(PartyId));
        // The guest gets the party once, with the join notice.
        Assert.That(Party(guestTest).Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(PartyPacket.Load(guest.Party.Party, true)),
        }));
        // The inviter is told of the new member.
        Assert.That(inviter.Party.Party.Members.Keys, Is.EquivalentTo(new[] { inviter.CharacterId, guest.CharacterId }));
        Assert.That(Party(inviterTest).Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(PartyPacket.Joined(inviter.Party.Party.Members[guest.CharacterId])),
        }));
    }

    // A member who dies is named to the party once, in Party 18 after the stats: byte 0 on a first death, 1 under a
    // metal tombstone. Staying dead, more hp updates and the revive send no second one.
    [TestCase(DeathState.FirstDeath, "00")]
    [TestCase(DeathState.Metal, "01")]
    public void MemberDeath_SendsTheTombstoneNoticeOnce(DeathState state, string flag) {
        Invite(guest.PlayerName);
        Reply(PartyInviteResponse.Accept);
        Party(inviterTest);
        Maple2.Model.Game.Party.PartyMember member = inviter.Party.Party!.Members[guest.CharacterId];
        MethodInfo sync = typeof(PartyManager).GetMethod("SyncUpdate", Any)!;
        void Update(UpdateField type, DeathState death) {
            Maple2.Model.Game.PlayerInfo info = member.Info.Clone();
            info.DeathState = death;
            sync.Invoke(inviter.Party, [System.Threading.CancellationToken.None, guest.CharacterId, type, info]);
        }

        Update(UpdateField.Death, state);
        string notice = "5300" + "12" + Convert.ToHexString(BitConverter.GetBytes(guest.CharacterId)) + flag;
        Assert.That(Party(inviterTest).Select(Convert.ToHexString).ToArray(), Is.EqualTo(new[] {
            Hex(PartyPacket.UpdateStats(member)),
            notice,
        }));

        Update(UpdateField.Health, state);
        Update(UpdateField.Death, state);
        Update(UpdateField.Death, DeathState.Alive);
        Assert.That(Party(inviterTest).Select(packet => packet[2]), Has.None.EqualTo((byte) 18));

        Update(UpdateField.Health | UpdateField.Death, state);
        Assert.That(Party(inviterTest).Select(Convert.ToHexString), Does.Contain(notice));
    }

    // A member killed in the field: the actor's death goes to the world as player updates; the world compares each
    // with its record and hands the changed fields to the game server, where the party listens.
    [Test]
    public void MemberKilledInTheField_TellsThePartyOnce() {
        Invite(guest.PlayerName);
        Reply(PartyInviteResponse.Accept);
        Party(inviterTest);
        Assert.That(inviter.PlayerInfo.GetOrFetch(guest.CharacterId, out Maple2.Model.Game.PlayerInfo? fetched), Is.True);
        Maple2.Model.Game.PlayerInfo record = fetched!.Clone();
        void World() {
            // One sync step of the world: every queued update is applied, then one notice names all changed fields.
            UpdateField changed = UpdateField.None;
            foreach (PlayerUpdateRequest request in guestTest.World.PlayerUpdates.Where(update => update.CharacterId == guest.CharacterId)) {
                var update = new PlayerInfoUpdateEvent(record, request);
                if (update.Type == UpdateField.None) {
                    continue;
                }
                record.Apply(update);
                changed |= update.Type;
            }
            guestTest.World.PlayerUpdates.Clear();
            if (changed == UpdateField.None) {
                return;
            }
            var notify = new PlayerUpdateRequest { AccountId = record.AccountId, CharacterId = record.CharacterId };
            notify.SetFields(changed, record);
            channel.UpdatePlayer(notify, null!).GetAwaiter().GetResult();
        }
        MethodInfo setDead = typeof(Maple2.Server.Game.Model.FieldPlayer).GetProperty("IsDead")!.GetSetMethod(true)!;

        typeof(ConfigManager).GetField("session", Any)!.SetValue(guest.Config, guest);
        setDead.Invoke(guest.Player, [true]);
        guestTest.World.PlayerUpdates.Add(new PlayerUpdateRequest {
            AccountId = guest.AccountId,
            CharacterId = guest.CharacterId,
            Health = new HealthUpdate { CurrentHp = 0, TotalHp = record.TotalHp + 10 },
            Async = true,
        });
        World();

        Assert.That(guest.Player.IsDead, Is.True);
        string notice = "5300" + "12" + Convert.ToHexString(BitConverter.GetBytes(guest.CharacterId)) + "00";
        Assert.That(Party(inviterTest).Select(Convert.ToHexString).Count(packet => packet == notice), Is.EqualTo(1));
    }

    // The dungeon info line (Party 14) follows a member update only when the member's dungeon limits differ from the
    // ones held: not on an hp update, not on an equal table in a new dictionary, once on a changed limit or a new dungeon.
    [Test]
    public void MemberUpdate_SendsDungeonInfoOnlyOnChangedLimits() {
        Invite(guest.PlayerName);
        Reply(PartyInviteResponse.Accept);
        Party(inviterTest);
        Maple2.Model.Game.Party.PartyMember member = inviter.Party.Party!.Members[guest.CharacterId];
        MethodInfo sync = typeof(PartyManager).GetMethod("SyncUpdate", Any)!;
        int DungeonInfos(UpdateField type, params (int Id, DungeonEnterLimit Limit)[] limits) {
            Maple2.Model.Game.PlayerInfo info = member.Info.Clone();
            info.DungeonEnterLimits = limits.ToDictionary(limit => limit.Id, limit => limit.Limit);
            sync.Invoke(inviter.Party, [System.Threading.CancellationToken.None, guest.CharacterId, type, info]);
            return Party(inviterTest).Count(packet => packet[2] == 14);
        }

        Assert.That(DungeonInfos(UpdateField.Health), Is.EqualTo(0));
        Assert.That(DungeonInfos(UpdateField.Dungeon, (1, DungeonEnterLimit.Rookie)), Is.EqualTo(1));
        Assert.That(DungeonInfos(UpdateField.Dungeon, (1, DungeonEnterLimit.Rookie)), Is.EqualTo(0));
        Assert.That(DungeonInfos(UpdateField.Health | UpdateField.Dungeon, (1, DungeonEnterLimit.Rookie)), Is.EqualTo(0));
        Assert.That(DungeonInfos(UpdateField.Dungeon, (1, DungeonEnterLimit.MinLevel)), Is.EqualTo(1));
        Assert.That(DungeonInfos(UpdateField.Dungeon, (1, DungeonEnterLimit.MinLevel), (2, DungeonEnterLimit.Rookie)), Is.EqualTo(1));
        Assert.That(DungeonInfos(UpdateField.Dungeon, (2, DungeonEnterLimit.Rookie), (1, DungeonEnterLimit.MinLevel)), Is.EqualTo(0));
        Assert.That(DungeonInfos(UpdateField.Health), Is.EqualTo(0));
    }

    [TestCase(DeathState.Alive, DeathState.FirstDeath, true)]
    [TestCase(DeathState.Alive, DeathState.Metal, true)]
    [TestCase(DeathState.FirstDeath, DeathState.Metal, false)]
    [TestCase(DeathState.Metal, DeathState.Alive, false)]
    [TestCase(DeathState.Alive, DeathState.Alive, false)]
    public void DiedNow_IsTheStepFromAliveToDead(DeathState before, DeathState after, bool expected) {
        Assert.That(PartyManager.DiedNow(before, after), Is.EqualTo(expected));
    }

    [Test]
    public void Invite_RefusedLeavesTheGuestWithoutAParty() {
        Assume.That(inviter.CharacterId, Is.Not.EqualTo(guest.CharacterId));
        Invite(guest.PlayerName);
        Party(inviterTest);
        Party(guestTest);

        Reply(PartyInviteResponse.RejectInvite);

        Assert.That(guest.Party.Party, Is.Null);
        Assert.That(Party(guestTest), Is.Empty);
        Assert.That(inviter.Party.Party!.Members.Keys, Is.EqualTo(new[] { inviter.CharacterId }));
        Assert.That(Party(inviterTest), Is.Empty);
    }

    [Test]
    public void Invite_OfTheOwnNameIsRefused() {
        Invite(inviter.PlayerName);

        Assert.That(Party(inviterTest).Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(PartyPacket.Error(PartyError.s_party_err_myself)),
        }));
        Assert.That(inviter.Party.Party, Is.Null);
    }

    [Test]
    public void Invite_OfANameNobodyHasIsRefused() {
        Invite("nobody");

        byte[][] sent = Party(inviterTest);
        Assert.That(Convert.ToHexString(sent[^1]), Is.EqualTo(Hex(PartyPacket.Error(PartyError.s_party_err_cannot_invite))));
        Assert.That(Party(guestTest), Is.Empty);
    }

    [Test]
    public void Invite_OfAPlayerInAFullerPartyIsRefused() {
        Assume.That(inviter.CharacterId, Is.Not.EqualTo(guest.CharacterId));
        Invite(guest.PlayerName);
        Reply(PartyInviteResponse.Accept);
        Party(inviterTest);
        Party(guestTest);

        // The guest, now a member and not the leader, is invited again.
        Invite(guest.PlayerName);

        Assert.That(Party(guestTest), Is.Empty);
        Assert.That(Party(inviterTest).Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(PartyPacket.Error(PartyError.s_party_err_cannot_invite, guest.PlayerName)),
        }));
    }
}
