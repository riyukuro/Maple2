using System;
using System.Collections.Generic;
using System.Linq;
using Grpc.Core;
using Maple2.Model.Error;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The party finder's modes (PartySearch 0x006E): Add 0, Remove 1, Load 2. A banned word in a listing title answers
// 108 with request type 0, in a search word 108 with type 2 (OnMatchPartyError); an unreachable World answers the
// database error 2 and keeps the session.
public class PartySearchModeTests {
    private const int PartyId = 5;
    private const long ListingId = 7;
    // KR string/kr/banword.xml row 1.
    private const string Banned = "\uAC1C\uC0C8\uB07C";

    private TestGameSession test = null!;
    private PartySearchHandler handler = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void Login() {
        test = new TestGameSession();
        test.Start();
        test.World.AddPlayer(session.AccountId, session.CharacterId, session.PlayerName);
        session.Party = new PartyManager(test.World, session);
        handler = new PartySearchHandler { World = test.World, BanWordStorage = TestGameSession.BanWords(Banned) };
        test.TakeSent();
    }

    private static RpcException Down() => new(new Status(StatusCode.Unavailable, "down"));

    private void Add(string title) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteUnicodeString(title);
            writer.WriteBool(false);
            writer.WriteInt(10);
        }));
    }

    private void Load(string search) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteInt(0);
            writer.WriteInt(0);
            writer.WriteByte((byte) Maple2.Model.Enum.PartySearchSort.Newest);
            writer.WriteUnicodeString(search);
            writer.WriteInt(1);
            writer.WriteInt(0);
        }));
    }

    private PartyInfo PartyOf(bool listed) {
        var info = new PartyInfo {
            Id = PartyId,
            LeaderAccountId = session.AccountId,
            LeaderCharacterId = session.CharacterId,
            LeaderName = session.PlayerName,
            CreationTime = 1000,
        };
        if (listed) {
            info.PartySearch = new PartySearchInfo {
                Id = ListingId,
                PartyId = PartyId,
                Name = "come",
                Size = 10,
                MemberCount = 1,
                LeaderAccountId = session.AccountId,
                LeaderCharacterId = session.CharacterId,
                LeaderName = session.PlayerName,
                CreationTime = 1000,
            };
        }
        info.Members.Add(new PartyInfo.Types.Member { CharacterId = session.CharacterId, JoinTime = 1000 });
        return info;
    }

    private string[] Sent() => test.TakeSent()
        .Where(packet => TestGameSession.Op(packet) == SendOp.PartySearch)
        .Select(Convert.ToHexString).ToArray();

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void Add_BannedTitle_Answers108Type0_AndMakesNoParty() {
        bool partyAsked = false;
        test.World.PartyWorld = _ => {
            partyAsked = true;
            return new PartyResponse { Party = PartyOf(false) };
        };

        Add("join " + Banned);

        Assert.That(partyAsked, Is.False);
        Assert.That(session.Party.Party, Is.Null);
        Assert.That(test.World.PartySearches, Is.Empty);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_banword_title, 0)) }));
    }

    [Test]
    public void Load_BannedSearch_Answers108Type2() {
        Load(Banned);

        Assert.That(test.World.PartySearches, Is.Empty);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_banword_findword, 2)) }));
    }

    [Test]
    public void Add_WorldDownOnTheParty_AnswersDbError() {
        test.World.PartyWorld = _ => throw Down();

        Assert.DoesNotThrow(() => Add("come along"));

        Assert.That(session.Party.Party, Is.Null);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_db)) }));
    }

    [Test]
    public void Add_WorldDownOnTheListing_AnswersDbError() {
        Assert.That(session.Party.SetParty(PartyOf(false)), Is.True);
        test.TakeSent();
        test.World.PartySearchWorld = _ => throw Down();

        Assert.DoesNotThrow(() => Add("come along"));

        Assert.That(session.Party.Party!.Search, Is.Null);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_db)) }));
    }

    // A party made for the listing goes again when the listing is not made: the lone leader leaves it on the World
    // and the client gets the Disband (Party 7), so no party of one is left behind.
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void Add_ListingNotMade_UndoesTheMadeParty(bool refused, bool leaveDown) {
        bool leaveAsked = false;
        test.World.PartyWorld = request => {
            if (request.PartyCase == PartyRequest.PartyOneofCase.Create) {
                return new PartyResponse { Party = PartyOf(false) };
            }
            Assert.That(request.PartyCase, Is.EqualTo(PartyRequest.PartyOneofCase.Leave));
            Assert.That(request.Leave.PartyId, Is.EqualTo(PartyId));
            leaveAsked = true;
            return leaveDown ? throw Down() : new PartyResponse();
        };
        test.World.PartySearchWorld = _ => refused
            ? new PartySearchResponse { Error = (int) PartySearchError.s_partysearch_err_server_already_register }
            : throw Down();

        Assert.DoesNotThrow(() => Add("come along"));

        Assert.That(leaveAsked, Is.True);
        Assert.That(session.Party.Party, Is.Null);
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Where(packet => TestGameSession.Op(packet) == SendOp.Party).Select(Convert.ToHexString).Last(),
            Is.EqualTo(Hex(PartyPacket.Disband())));
        Assert.That(sent.Where(packet => TestGameSession.Op(packet) == SendOp.PartySearch).Select(Convert.ToHexString),
            Is.EqualTo(new[] { Hex(PartySearchPacket.Error(refused ? PartySearchError.s_partysearch_err_server_already_register : PartySearchError.s_partysearch_err_server_db)) }));
    }

    [Test]
    public void Remove_WorldDown_AnswersDbError_AndKeepsTheListing() {
        Assert.That(session.Party.SetParty(PartyOf(true)), Is.True);
        test.TakeSent();
        test.World.PartySearchWorld = _ => throw Down();

        Assert.DoesNotThrow(() => handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(1))));

        Assert.That(session.Party.Party!.Search, Is.Not.Null);
        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_db)) }));
    }

    // The listing is removed and the World goes down before the lone leader's party is left: the removal still
    // stands and the session stays.
    [Test]
    public void Remove_WorldDownOnTheLeave_KeepsTheRemoval() {
        Assert.That(session.Party.SetParty(PartyOf(true)), Is.True);
        test.TakeSent();
        test.World.PartySearchWorld = _ => new PartySearchResponse();
        test.World.PartyWorld = _ => throw Down();

        Assert.DoesNotThrow(() => handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(1))));

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartySearchPacket.Remove(ListingId)) }));
    }

    [Test]
    public void Load_WorldDown_AnswersDbError() {
        test.World.PartySearchWorld = _ => throw Down();

        Assert.DoesNotThrow(() => Load("come"));

        Assert.That(Sent(), Is.EqualTo(new[] { Hex(PartySearchPacket.Error(PartySearchError.s_partysearch_err_server_db)) }));
    }
}
