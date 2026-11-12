using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Grpc.Core;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Microsoft.EntityFrameworkCore;
using Serilog;
using GuildManager = Maple2.Server.Game.Manager.GuildManager;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The guild requests that cost meso, on the channel: a refusal takes nothing and asks the world nothing, and a
// refusal by the world gives the meso back.
public class GuildMoneyHandlerTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int DonateMeso = 10000;
    private const int CreatePrice = 2000;

    private TestGameSession test = null!;
    private GameSession session = null!;
    private GuildHandler handler = null!;
    private GuildMember self = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with {
            guildDonateMeso = DonateMeso,
            GuildCreatePrice = CreatePrice,
            GuildCreateMinLevel = 10,
            GuildNameLengthMin = 2,
            GuildNameLengthMax = 12,
        });
        test.Start();
        session = test.Session;
        session.Player.Value.Character.Level = 20;
        handler = new GuildHandler {
            World = test.World,
            TableMetadata = test.TableMetadata,
            BanWordStorage = new BanWordStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options)),
            ServerTableMetadata = test.ServerTableMetadata,
        };
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() {
        self?.Dispose();
    }

    // The session as a member of a guild whose level allows three donations a day (or of no guild).
    private void Join(bool inGuild = true) {
        var manager = (GuildManager) RuntimeHelpers.GetUninitializedObject(typeof(GuildManager));
        typeof(GuildManager).GetField("session", Any)!.SetValue(manager, session);
        typeof(GuildManager).GetField("tokenSource", Any)!.SetValue(manager, new CancellationTokenSource());
        typeof(GuildManager).GetField("logger", Any)!.SetValue(manager, Log.Logger);
        var property = (GuildTable.Property) RuntimeHelpers.GetUninitializedObject(typeof(GuildTable.Property));
        typeof(GuildManager).GetProperty("Properties", Any)!.SetValue(manager, property with { Level = 1, DonateMax = 3 });
        if (inGuild) {
            var info = new PlayerInfo(new CharacterInfo(session.AccountId, session.CharacterId, "Self", "", "", Gender.Male, Job.Newbie, 20), "",
                new AchievementInfo(), []);
            self = new GuildMember { GuildId = 7, Info = info, Rank = 0, JoinTime = 1000 };
            var guild = new Guild(7, "Guild", self);
            guild.Members[self.CharacterId] = self;
            typeof(GuildManager).GetProperty("Guild", Any)!.SetValue(manager, guild);
        }
        session.Guild = manager;
    }

    private void Donate(int count) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(113);
            writer.WriteInt(count);
        }));
    }

    private void Create(string name) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteUnicodeString(name);
        }));
    }

    private string[] GuildAnswers() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Guild).Select(Convert.ToHexString).ToArray();
    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    // A donation the channel refuses: no count, more than the day allows, more meso than the player has, or no
    // guild. No meso is taken and the world is not asked.
    [TestCase(0, 50000L, true, null)]
    [TestCase(-2, 50000L, true, null)]
    [TestCase(4, 50000L, true, GuildError.s_guild_err_unknown)]
    [TestCase(2, 19999L, true, GuildError.s_guild_err_no_money)]
    [TestCase(1, 0L, true, GuildError.s_guild_err_no_money)]
    [TestCase(1, 50000L, false, null)]
    public void Donate_Refused(int count, long meso, bool inGuild, GuildError? error) {
        Join(inGuild);
        session.Currency.Meso = meso;
        test.TakeSent();

        Donate(count);

        Assert.That(session.Currency.Meso, Is.EqualTo(meso));
        Assert.That(test.World.GuildRequests, Is.Empty);
        Assert.That(GuildAnswers(), Is.EqualTo(error == null ? [] : new[] { Hex(GuildPacket.Error(error.Value)) }));
    }

    // The world takes the donation: the meso of the count is taken once and the answer carries the world's count.
    [Test]
    public void Donate_TakesTheMesoOfTheCount() {
        Join();
        session.Currency.Meso = 50000;
        test.World.GuildWorld = request => new GuildResponse { DonationCount = request.Donate.Count, DonationTime = 123456 };
        test.TakeSent();

        Donate(2);

        Assert.That(session.Currency.Meso, Is.EqualTo(30000));
        GuildRequest sent = test.World.GuildRequests.Single();
        Assert.That((sent.RequestorId, sent.Donate.GuildId, sent.Donate.Count, sent.Donate.Fund), Is.EqualTo((session.CharacterId, 7L, 2, 20000L)));
        Assert.That((self.DailyDonationCount, self.DonationTime), Is.EqualTo((2, 123456L)));
        Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.Donated(self)) }));
    }

    // The world refuses (the day's count is used up there, or the fund is full): the meso comes back.
    [Test]
    public void Donate_RefusedByTheWorldReturnsTheMeso() {
        Join();
        session.Currency.Meso = 50000;
        test.World.GuildWorld = _ => new GuildResponse { Error = (int) GuildError.s_guild_err_unknown };
        test.TakeSent();

        Donate(3);

        Assert.That(session.Currency.Meso, Is.EqualTo(50000));
        Assert.That(self.DailyDonationCount, Is.EqualTo(0));
        Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.Error(GuildError.s_guild_err_unknown)) }));
    }

    // The call to the world fails without an answer: the world may have added the fund, so the meso stays taken
    // (the guild cannot get fund without meso) and the player gets the error.
    [Test]
    public void Donate_WorldCallFailingKeepsTheMesoTaken() {
        Join();
        session.Currency.Meso = 50000;
        test.World.GuildWorld = _ => throw new RpcException(new Status(StatusCode.Unavailable, "down"));
        test.TakeSent();

        Donate(1);

        Assert.That(session.Currency.Meso, Is.EqualTo(40000));
        Assert.That(self.DailyDonationCount, Is.EqualTo(0));
        Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.Error(GuildError.s_guild_err_unknown)) }));
    }

    // A guild is not founded and nothing is paid: a bad name, a level too low, too little meso (the world is
    // not asked), or the world's refusal (the name is taken).
    [TestCase("G", 20, 5000L, false, GuildError.s_guild_err_name_value)]
    [TestCase("", 20, 5000L, false, GuildError.s_guild_err_name_value)]
    [TestCase("NameThatIsTooLong", 20, 5000L, false, GuildError.s_guild_err_name_value)]
    [TestCase("Guild", 9, 5000L, false, GuildError.s_guild_err_not_enough_level)]
    [TestCase("Guild", 20, 1999L, false, GuildError.s_guild_err_no_money)]
    [TestCase("Guild", 20, 5000L, true, GuildError.s_guild_err_name_exist)]
    public void Create_RefusedTakesNothing(string name, short level, long meso, bool asked, GuildError error) {
        Join(inGuild: false);
        session.Player.Value.Character.Level = level;
        session.Currency.Meso = meso;
        test.World.GuildWorld = _ => new GuildResponse { Error = (int) GuildError.s_guild_err_name_exist };
        test.TakeSent();

        Create(name);

        Assert.That(session.Currency.Meso, Is.EqualTo(meso));
        Assert.That(test.World.GuildRequests.Count, Is.EqualTo(asked ? 1 : 0));
        Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.Error(error)) }));
        Assert.That(session.Guild.Guild, Is.Null);
    }

    // A member of a guild cannot found another: nothing is asked, taken or answered.
    [Test]
    public void Create_AsAMemberDoesNothing() {
        Join();
        session.Currency.Meso = 5000;
        test.TakeSent();

        Create("Other");

        Assert.That(session.Currency.Meso, Is.EqualTo(5000));
        Assert.That(test.World.GuildRequests, Is.Empty);
        Assert.That(GuildAnswers(), Is.Empty);
    }

    // Guild 27 carries the changer's name as plain text: the client puts it into its own line.
    [Test]
    public void NotifyUpdateEmblem_TextIsTheNameAlone() {
        ByteWriter expected = Packet.Of(SendOp.Guild);
        expected.WriteByte(27);
        expected.WriteBool(false);
        expected.WriteInt(0);
        expected.WriteUnicodeString("Leader");
        expected.WriteUnicodeString("emblem");

        Assert.That(Hex(GuildPacket.NotifyUpdateEmblem("Leader", "emblem")), Is.EqualTo(Hex(expected)));
    }
}
