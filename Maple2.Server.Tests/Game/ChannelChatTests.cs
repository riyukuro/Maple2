using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// The channel's chat call: a whisper to a player that is not here is not found, a rejected whisper goes back to the
// whisperer, a whisper is kept for the reply, a party or guild line reaches the members present here only, a world
// line reaches every session, and a call without a chat kind is an invalid argument.
public class ChannelChatTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private ChannelService service = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, new ConcurrentDictionary<long, GameSession> { [session.CharacterId] = session });
        service = new ChannelService(server, null!, test.Storage, null!, null!, null!);
        test.TakeSent();
    }

    private void Call(ChatRequest request) => service.Chat(request, null!).GetAwaiter().GetResult();

    private ChatRequest Request(Action<ChatRequest> fill) {
        var request = new ChatRequest { AccountId = 9, CharacterId = 9, Name = "Other", Message = "hi", Channel = 1 };
        fill(request);
        return request;
    }

    [Test]
    public void AWhisperToAPlayerNotHere_IsNotFound() {
        var request = Request(r => r.Whisper = new ChatRequest.Types.Whisper { RecipientId = session.CharacterId + 1, RecipientName = "Gone" });
        Assert.That(Assert.Throws<RpcException>(() => Call(request))!.StatusCode, Is.EqualTo(StatusCode.NotFound));
    }

    [Test]
    public void AWhisper_IsKeptForTheReplyAndSent() {
        Call(Request(r => r.Whisper = new ChatRequest.Types.Whisper { RecipientId = session.CharacterId, RecipientName = "Me" }));
        Assert.That(session.WhisperedBy["Other"], Is.EqualTo(9));
        Assert.That(test.TakeSent(), Has.Count.EqualTo(1));
    }

    [Test]
    public void ARejectedWhisper_GoesBackAndIsNotKept() {
        Call(Request(r => r.Whisper = new ChatRequest.Types.Whisper { RecipientId = session.CharacterId, Reject = true }));
        Assert.That(session.WhisperedBy, Does.Not.ContainKey("Other"));
        Assert.That(test.TakeSent(), Has.Count.EqualTo(1));
    }

    [Test]
    public void APartyLine_ReachesTheMembersHereOnly() {
        var party = new ChatRequest.Types.Party();
        party.MemberIds.Add(new[] { session.CharacterId, session.CharacterId + 5 });
        Call(Request(r => r.Party = party));
        Assert.That(test.TakeSent(), Has.Count.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AGuildLine_AndTheGuildNotice_ReachTheMembersHere(bool notice) {
        var guild = new ChatRequest.Types.Guild { Notice = notice };
        guild.MemberIds.Add(session.CharacterId);
        Call(Request(r => r.Guild = guild));
        Assert.That(test.TakeSent(), Has.Count.EqualTo(1));
    }

    [Test]
    public void AClubLine_ReachesTheMembersHere() {
        var club = new ChatRequest.Types.Club { ClubId = 4 };
        club.MemberIds.Add(session.CharacterId);
        Call(Request(r => r.Club = club));
        Assert.That(test.TakeSent(), Has.Count.EqualTo(1));
    }

    [Test]
    public void AWorldSuperAndWeddingLine_ReachEverySession() {
        Call(Request(r => r.World = new ChatRequest.Types.World()));
        Assert.That(test.TakeSent(), Has.Count.EqualTo(1));
        Call(Request(r => r.Super = new ChatRequest.Types.Super { ItemId = 1 }));
        Assert.That(test.TakeSent(), Has.Count.EqualTo(1));
        Call(Request(r => r.Wedding = new ChatRequest.Types.Wedding { ItemId = 1, Groom = "A", Bride = "B" }));
        Assert.That(test.TakeSent(), Has.Count.EqualTo(1));
    }

    // A line with linked items (the item tooltip links): the items the storage holds go first as one link packet to
    // each session the line reaches, a uid no row has is left out, then the line.
    private static readonly string[] Kinds = ["Whisper", "Party", "Guild", "Club", "World", "Super", "Wedding", "SystemNotice"];

    [TestCaseSource(nameof(Kinds))]
    public void ALineWithLinkedItems_SendsTheLinksFirst(string kind) {
        Item item = test.Store(TestGameSession.Metadata(20000001, type: 2));
        ChatRequest request = Request(r => {
            switch (kind) {
                case "Whisper": r.Whisper = new ChatRequest.Types.Whisper { RecipientId = session.CharacterId, RecipientName = "Me" }; break;
                case "Party": r.Party = new ChatRequest.Types.Party { MemberIds = { session.CharacterId } }; break;
                case "Guild": r.Guild = new ChatRequest.Types.Guild { MemberIds = { session.CharacterId } }; break;
                case "Club": r.Club = new ChatRequest.Types.Club { ClubId = 4, MemberIds = { session.CharacterId } }; break;
                case "World": r.World = new ChatRequest.Types.World(); break;
                case "Super": r.Super = new ChatRequest.Types.Super { ItemId = 1 }; break;
                case "Wedding": r.Wedding = new ChatRequest.Types.Wedding { ItemId = 1, Groom = "A", Bride = "B" }; break;
                default: r.SystemNotice = new ChatRequest.Types.SystemNotice(); break;
            }
            r.ItemUids.Add(item.Uid);
            r.ItemUids.Add(item.Uid + 1000);
        });

        Call(request);

        Item stored;
        using (GameStorage.Request db = test.Storage.Context()) {
            stored = db.GetItem(item.Uid)!;
        }
        var sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(2));
        Assert.That(Convert.ToHexString(sent[0]), Is.EqualTo(Convert.ToHexString(MessengerBrowserPacket.Link(stored).ToArray())));
    }

    [Test]
    public void ACallWithoutAChatKind_IsAnInvalidArgument() {
        Assert.That(Assert.Throws<RpcException>(() => Call(new ChatRequest()))!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
    }
}
