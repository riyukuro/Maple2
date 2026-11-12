using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;
using Serilog;
using GuildManager = Maple2.Server.Game.Manager.GuildManager;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The guild rename ticket (item function ChangeGuildName): the client sends the plain item use with the new name
// from the master's name box. The ticket is used only when the World renamed the guild.
public class ChangeGuildNameTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int TicketId = 20300901;

    private TestGameSession test = null!;
    private GameSession session = null!;
    private ItemUseHandler handler = null!;
    private Item ticket = null!;
    private GuildMember? self;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with {
            GuildNameLengthMin = 2,
            GuildNameLengthMax = 12,
        });
        ticket = test.Store(TestGameSession.Metadata(TicketId,
            function: new ItemMetadataFunction(ItemFunction.ChangeGuildName, "ChangeGuildName", "", false), type: 2), amount: 2);
        var words = new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        words.BanWordMetadata.Add(new BanWordMetadata(0, "badword", false));
        words.SaveChanges();
        handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = new BanWordStorage(words) };
        test.Start();
        session = test.Session;
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() {
        self?.Dispose();
    }

    // The session as a member (the master, or of rank 1 under another master) of guild 7 "Guild".
    private void Join(bool master = true) {
        var manager = (GuildManager) RuntimeHelpers.GetUninitializedObject(typeof(GuildManager));
        typeof(GuildManager).GetField("session", Any)!.SetValue(manager, session);
        typeof(GuildManager).GetField("tokenSource", Any)!.SetValue(manager, new CancellationTokenSource());
        typeof(GuildManager).GetField("logger", Any)!.SetValue(manager, Log.Logger);
        var info = new PlayerInfo(new CharacterInfo(session.AccountId, session.CharacterId, "Self", "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []);
        self = new GuildMember { GuildId = 7, Info = info, Rank = (byte) (master ? 0 : 1), JoinTime = 1000 };
        Guild guild = master ? new Guild(7, "Guild", self) : new Guild(7, "Guild", session.AccountId + 1, session.CharacterId + 1, "Master");
        guild.Members[self.CharacterId] = self;
        typeof(GuildManager).GetProperty("Guild", Any)!.SetValue(manager, guild);
        session.Guild = manager;
        session.Player.Value.Character.GuildId = 7;
        session.Player.Value.Character.GuildName = "Guild";
    }

    private void Use(string name) => handler.Handle(session, TestGameSession.Packet(writer => {
        writer.WriteLong(ticket.Uid);
        writer.WriteUnicodeString(name);
    }));

    private int Tickets() => session.Item.Inventory.Get(ticket.Uid)?.Amount ?? 0;
    private string[] GuildAnswers() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Guild).Select(Convert.ToHexString).ToArray();
    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    // Refused on the channel: no guild, not the master, a name that is blank, too short, too long, of letters a name
    // may not have, or banned. The World is not asked and the ticket is kept.
    [TestCase("none", "NewName", GuildError.s_guild_err_null_guild)]
    [TestCase("member", "NewName", GuildError.s_guild_err_no_master)]
    [TestCase("master", "", GuildError.s_guild_err_name_value)]
    [TestCase("master", "   ", GuildError.s_guild_err_name_value)]
    [TestCase("master", "A", GuildError.s_guild_err_name_value)]
    [TestCase("master", "NameThatIsTooLong", GuildError.s_guild_err_name_value)]
    [TestCase("master", "Bad name!", GuildError.s_guild_err_name_value)]
    [TestCase("master", "mybadword", GuildError.s_guild_err_name_value)]
    public void Refused(string who, string name, GuildError error) {
        if (who != "none") {
            Join(master: who == "master");
        }

        Use(name);

        Assert.That(test.World.GuildRequests, Is.Empty);
        Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.Error(error)) }));
        Assert.That(Tickets(), Is.EqualTo(2));
    }

    // The World refuses (a name another guild has): its error is sent and the ticket is kept.
    [Test]
    public void RefusedByTheWorld() {
        Join();
        test.World.GuildWorld = _ => new GuildResponse { Error = (int) GuildError.s_guild_err_name_exist };

        Use("Taken");

        Assert.That(test.World.GuildRequests, Has.Count.EqualTo(1));
        Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.Error(GuildError.s_guild_err_name_exist)) }));
        Assert.That(Tickets(), Is.EqualTo(2));
    }

    // Renamed: the World is asked for this guild's new name by the master, and one ticket is used.
    [Test]
    public void Renamed() {
        Join();

        Use("NewName");

        GuildRequest request = test.World.GuildRequests.Single();
        Assert.That(request.GuildCase, Is.EqualTo(GuildRequest.GuildOneofCase.Rename));
        Assert.That((request.RequestorId, request.Rename.GuildId, request.Rename.Name), Is.EqualTo((session.CharacterId, 7L, "NewName")));
        Assert.That(Tickets(), Is.EqualTo(1));
        Assert.That(GuildAnswers(), Is.Empty);
    }

    // The World's rename on a member's channel: the guild and the character take the name, the member gets Guild 34
    // with it, and the World's player info gets it.
    [Test]
    public void Rename_OnTheChannel() {
        Join();

        Assert.That(session.Guild.Rename("NewName"), Is.True);

        Assert.That(session.Guild.Guild!.Name, Is.EqualTo("NewName"));
        Assert.That(session.Player.Value.Character.GuildName, Is.EqualTo("NewName"));
        Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.UpdateName("NewName")) }));
        Assert.That(test.World.PlayerUpdates.Last().GuildName, Is.EqualTo("NewName"));
    }

    // Guild 34 (CGuildManager::OnPacketGuildRenamed): the command, then the name.
    [Test]
    public void UpdateName_Packet() {
        Assert.That(Hex(GuildPacket.UpdateName("Ab")), Is.EqualTo(Hex(GuildPacket.Error(GuildError.none))[..4] + "22" + "0200" + "41006200"));
    }
}
