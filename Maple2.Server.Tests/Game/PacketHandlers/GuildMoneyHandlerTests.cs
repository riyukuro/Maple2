using System;
using System.Collections.Generic;
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
// refusal by the world gives the meso back. Also the guild vs game join's refusals on the channel.
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
        var property = (GuildTable.Property) RuntimeHelpers.GetUninitializedObject(typeof(GuildTable.Property));
        test.SetTable(new GuildTable(
            new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>>(),
            new Dictionary<int, IReadOnlyDictionary<int, GuildTable.House>>(),
            new Dictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>>(),
            new Dictionary<short, GuildTable.Property> { [1] = property with { Level = 1, Capacity = 10, DonateMax = 3 } }));
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

    // Guild 3 (invite) with a name no character has: the client's "player not found" text (10, s_guild_err_null_user),
    // and the World is not asked.
    [Test]
    public void Invite_UnknownName_NullUser() {
        Join();
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteUnicodeString("NoSuchName");
        }));

        Assert.That(test.World.GuildRequests, Is.Empty);
        Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.Error(GuildError.s_guild_err_null_user)) }));
    }

    // Guild 84 in the client writer's order (CGuildManager::SendGuildMode84): u8 0x54, i64 the id of the accept page
    // row (the script sends the request id), u8 accept. The id goes to the World as it came; the answer echoes it.
    [Test]
    public void RespondApplication_SendsTheRowIdToTheWorld() {
        Join();
        session.Guild.Guild!.Ranks.Add(new GuildRank { Id = 0, Name = "Master", Permission = GuildPermission.InviteMembers });
        test.World.GuildWorld = _ => new GuildResponse { GuildId = 7, ApplicationId = 1, ApplicantName = "Applicant" };
        IByteReader packet = TestGameSession.Packet(writer => {
            writer.WriteByte(0x54);
            writer.WriteLong(1);
            writer.WriteByte(1);
        });
        handler.Handle(session, packet);

        GuildRequest sent = test.World.GuildRequests.Single();
        Assert.Multiple(() => {
            Assert.That(packet.Available, Is.EqualTo(0));
            Assert.That(sent.RequestorId, Is.EqualTo(session.CharacterId));
            Assert.That(sent.RespondApplication.GuildId, Is.EqualTo(7));
            Assert.That(sent.RespondApplication.Id, Is.EqualTo(1));
            Assert.That(sent.RespondApplication.Accepted, Is.True);
            Assert.That(test.TakeSent().Select(Convert.ToHexString),
                Does.Contain(Convert.ToHexString(GuildPacket.RespondApplication(1, "Applicant", true).ToArray())));
        });
    }

    // Guild 120 (the guild vs game's Enter) from where a match move is refused: dead or in a dungeon. The World is
    // not asked and the client's open game is cleared (Guild 119 with 0).
    [TestCase(false)]
    [TestCase(true)]
    public void VsGameJoin_RefusedWhereTheMoveIs(bool dungeon) {
        Join();
        if (dungeon) {
            test.EnterDungeon(TestGameSession.DungeonRoom(23000001, 2000062));
        } else {
            typeof(Maple2.Server.Game.Model.FieldPlayer).GetField("isDead", Any)!.SetValue(session.Player, true);
        }
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(120)));

        Assert.Multiple(() => {
            Assert.That(test.World.GuildRequests, Is.Empty);
            Assert.That(test.World.Moves, Is.Empty);
            Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(GuildPacket.VsGameOpen(0)) }));
        });
    }

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

    // The founder's answers in the order KMS2 sends them: the name tag (77), the result (1), the guild (0), the founder as a joined
    // member with an empty requestor and no notice line (18), the founder's data (32). The price is taken once.
    [Test]
    public void Create_AnswersInOrder() {
        Join(inGuild: false);
        session.Currency.Meso = 5000;
        test.World.AddPlayer(session.AccountId, session.CharacterId, "Founder");
        test.World.GuildWorld = request => new GuildResponse {
            Guild = new GuildInfo {
                Id = 7,
                Name = request.Create.GuildName,
                Emblem = "",
                Notice = "",
                Members = { new GuildInfo.Types.Member { CharacterId = session.CharacterId, Message = "", Rank = 0, JoinTime = 1000 } },
            },
        };
        test.TakeSent();

        Create("Guild");

        Assert.That(session.Currency.Meso, Is.EqualTo(5000 - CreatePrice));
        Guild guild = session.Guild.Guild!;
        self = guild.Members[session.CharacterId];
        Assert.That(GuildAnswers(), Is.EqualTo(new[] {
            Hex(GuildPacket.AddTag(session.PlayerName, "Guild")),
            Hex(GuildPacket.Created("Guild")),
            Hex(GuildPacket.Load(guild)),
            Hex(GuildPacket.Joined(string.Empty, self, notify: false)),
            Hex(GuildPacket.UpdateMember(self.Info)),
        }));

        // Guild 18 for the founder: the requestor's name is empty and the notice byte is 0.
        ByteWriter joined = GuildPacket.Joined(string.Empty, self, notify: false);
        byte[] head = joined.ToArray().Take(2 + 1 + 2 + 2 + 2 * self.Name.Length + 1).ToArray();
        ByteWriter expected = Packet.Of(SendOp.Guild);
        expected.WriteByte(18);
        expected.WriteUnicodeString(string.Empty);
        expected.WriteUnicodeString(self.Name);
        expected.WriteBool(false);
        Assert.That(Convert.ToHexString(head), Is.EqualTo(Hex(expected)));
        // Guild 1: the result byte 0 and the name.
        ByteWriter created = Packet.Of(SendOp.Guild);
        created.WriteByte(1);
        created.WriteByte(0);
        created.WriteUnicodeString("Guild");
        Assert.That(Hex(GuildPacket.Created("Guild")), Is.EqualTo(Hex(created)));
    }

    // A spend on another thread while the World founds the guild (a trade takes every meso left): the price was
    // taken before the World was asked, so only the rest could go, and the guild is founded with its price paid.
    // Before, the price was taken after the World's answer; that take threw on the empty balance, and the guild
    // stood unpaid.
    [Test]
    public void Create_ASpendWhileTheWorldAnswersCannotSkipThePrice() {
        Join(inGuild: false);
        session.Currency.Meso = 5000;
        test.World.AddPlayer(session.AccountId, session.CharacterId, "Founder");
        long spent = -1;
        test.World.GuildWorld = request => {
            long left = session.Currency.Meso;
            spent = session.Currency.TryTakeMeso(left, notify: false) ? left : 0;
            return new GuildResponse {
                Guild = new GuildInfo {
                    Id = 7,
                    Name = request.Create.GuildName,
                    Emblem = "",
                    Notice = "",
                    Members = { new GuildInfo.Types.Member { CharacterId = session.CharacterId, Message = "", Rank = 0, JoinTime = 1000 } },
                },
            };
        };
        test.TakeSent();

        Create("Guild");

        Assert.That(spent, Is.EqualTo(5000 - CreatePrice));
        Assert.That(session.Currency.Meso, Is.EqualTo(0));
        Assert.That(session.Guild.Guild, Is.Not.Null);
        self = session.Guild.Guild!.Members[session.CharacterId];
    }

    // A refusal by the World after the price was taken gives it back: the balance is as before, as is the answer.
    [Test]
    public void Create_RefusedByTheWorldGivesThePriceBack() {
        Join(inGuild: false);
        session.Currency.Meso = 5000;
        test.World.GuildWorld = _ => throw new RpcException(new Status(StatusCode.Unavailable, "down"));
        test.TakeSent();

        Create("Guild");

        Assert.That(session.Currency.Meso, Is.EqualTo(5000));
        Assert.That(test.TakeSent().Any(packet => TestGameSession.Op(packet) == SendOp.Meso), Is.False);
        Assert.That(session.Guild.Guild, Is.Null);
    }

    // A failure after the guild is founded and its price sent (here the World is down when the founder's guild data
    // goes out) leaves the price paid: the guild stands. Before, the catch gave the price back and answered an error.
    [Test]
    public void Create_AFailureAfterTheFoundingKeepsThePrice() {
        Join(inGuild: false);
        session.Currency.Meso = 5000;
        test.World.AddPlayer(session.AccountId, session.CharacterId, "Founder");
        test.World.GuildWorld = request => new GuildResponse {
            Guild = new GuildInfo {
                Id = 7,
                Name = request.Create.GuildName,
                Emblem = "",
                Notice = "",
                Members = { new GuildInfo.Types.Member { CharacterId = session.CharacterId, Message = "", Rank = 0, JoinTime = 1000 } },
            },
        };
        bool mesoSent = false;
        test.OnSend = packet => {
            if (TestGameSession.Op(packet) == SendOp.Meso) {
                mesoSent = true;
            } else if (mesoSent && TestGameSession.Op(packet) == SendOp.Guild) {
                test.OnSend = null;
                throw new RpcException(new Status(StatusCode.Unavailable, "down"));
            }
        };
        test.TakeSent();

        Create("Guild");

        Assert.That(session.Currency.Meso, Is.EqualTo(5000 - CreatePrice));
        Assert.That(session.Guild.Guild, Is.Not.Null);
        Assert.That(GuildAnswers(), Does.Not.Contain(Hex(GuildPacket.Error(GuildError.s_guild_err_unknown))));
        self = session.Guild.Guild!.Members[session.CharacterId];
    }

    // Another member's check-in as a member sees it, in the order KMS2 sends it: the guild's exp (50) and fund
    // (51), the member's contribution (53: name, the amount, the week's and the whole sum) and check-in time (36).
    [Test]
    public void Contribution_OfAnotherMemberInOrder() {
        Join();
        var info = new PlayerInfo(new CharacterInfo(900, 901, "Other", "", "", Gender.Male, Job.Newbie, 20), "", new AchievementInfo(), []);
        using var other = new GuildMember { GuildId = 7, Info = info, Rank = 2, JoinTime = 1000, WeeklyContribution = 40, TotalContribution = 340 };
        session.Guild.Guild!.Members[other.CharacterId] = other;
        test.TakeSent();

        // The channel service applies the world's two notices in the order the world sends them.
        Assert.That(session.Guild.UpdateGuildExpFunds(other.CharacterId, 105, 2030), Is.True);
        Assert.That(session.Guild.UpdateMemberContribution(other.CharacterId, 1637700000, 10), Is.True);

        ByteWriter exp = Packet.Of(SendOp.Guild);
        exp.WriteByte(50);
        exp.WriteInt(105);
        ByteWriter fund = Packet.Of(SendOp.Guild);
        fund.WriteByte(51);
        fund.WriteInt(2030);
        ByteWriter contribution = Packet.Of(SendOp.Guild);
        contribution.WriteByte(53);
        contribution.WriteUnicodeString("Other");
        contribution.WriteInt(10);
        contribution.WriteInt(50);
        contribution.WriteInt(350);
        ByteWriter time = Packet.Of(SendOp.Guild);
        time.WriteByte(36);
        time.WriteUnicodeString("Other");
        time.WriteLong(1637700000);
        Assert.That(GuildAnswers(), Is.EqualTo(new[] { Hex(exp), Hex(fund), Hex(contribution), Hex(time) }));
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

    // Guild 74 (u8, i64 last match time) and 75 (u8 alone) are answered only while GuildPvP is on; with it
    // off the client has no PvP history page and both requests are dropped.
    [TestCase(true)]
    [TestCase(false)]
    public void PvpRequests_FollowTheFeature(bool featureOn) {
        Join();
        if (!featureOn) {
            test.SetFeaturesOff(GuildHandler.PvpFeature);
        }
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(74);
            writer.WriteLong(0);
        }));
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(75)));

        Assert.That(GuildAnswers(), Has.Length.EqualTo(featureOn ? 2 : 0));
    }
}
