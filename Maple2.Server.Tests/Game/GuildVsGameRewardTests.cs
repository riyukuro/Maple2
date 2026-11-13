using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Serilog;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Tests.Game;

// The guild vs game's packets and the Game side of its guild rewards: the World's add answered off the caller's
// thread, and a contribution without a check-in time.
public class GuildVsGameRewardTests {
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const long GuildId = 78;

    private sealed class AddWorld : WorldClient {
        public GuildRequest? Request;
        public GuildResponse Response = new();

        public override GuildResponse Guild(GuildRequest request, CallOptions options) {
            Request = request;
            return Response;
        }
    }

    private TestGameSession test = null!;
    private AddWorld world = null!;
    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
    }

    private GameSession Start() {
        GameSession session = test.Start();
        world = new AddWorld();
        Set<GameSession>(session, "<World>k__BackingField", world);

        var guild = new Guild(GuildId, "Vs", test.AccountId, test.CharacterId, "Master");
        var info = new PlayerInfo(new CharacterInfo(test.AccountId, test.CharacterId, "Master", "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []);
        guild.Members[test.CharacterId] = new GuildMember { GuildId = GuildId, Info = info };
        var manager = (GuildManager) RuntimeHelpers.GetUninitializedObject(typeof(GuildManager));
        GC.SuppressFinalize(manager);
        Set<GuildManager>(manager, "session", session);
        Set<GuildManager>(manager, "logger", new LoggerConfiguration().CreateLogger());
        Set<GuildManager>(manager, "<Guild>k__BackingField", guild);
        session.Guild = manager;
        test.TakeSent();
        return session;
    }

    private static void Set<T>(object target, string field, object? value) {
        (typeof(T).GetField(field, Flags) ?? throw new MissingFieldException(typeof(T).Name, field)).SetValue(target, value);
    }

    [Test]
    public void VsGameResult_WritesBothGuilds() {
        byte[] packet = GuildPacket.VsGameResult([(5L, "Blue", "b.png", 30), (6L, "Red", "r.png", 10)]).ToArray();
        var reader = new ByteReader(packet, 2);

        Assert.That(reader.ReadByte(), Is.EqualTo(122));
        Assert.Multiple(() => {
            Assert.That(reader.ReadLong(), Is.EqualTo(5));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Blue"));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("b.png"));
            Assert.That(reader.ReadInt(), Is.EqualTo(30));
            Assert.That(reader.ReadLong(), Is.EqualTo(6));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Red"));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("r.png"));
            Assert.That(reader.ReadInt(), Is.EqualTo(10));
            Assert.That(reader.Available, Is.Zero);
        });
    }

    [Test]
    public void VsGameReward_WritesKindAndAmount() {
        var reader = new ByteReader(GuildPacket.VsGameReward(Maple2.Server.Game.Manager.Field.GuildVsGameField.RewardFund, 250).ToArray(), 2);

        Assert.That(reader.ReadByte(), Is.EqualTo(123));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(250));
        Assert.That(reader.Available, Is.Zero);
    }

    // The World answers what it added (here the fund clamped to 40); a refusal gives null; nothing to add sends
    // nothing.
    [Test]
    public async Task AddExpFundAsync_GivesTheWorldsAdd() {
        GameSession session = Start();
        world.Response = new GuildResponse { GuildId = GuildId, AddedExp = 0, AddedFund = 40 };
        Task<(int Exp, int Fund)?>? add = session.Guild.AddExpFundAsync(0, 300);
        Assert.That(add, Is.Not.Null);
        Assert.That(await add!, Is.EqualTo(((int, int)?) (0, 40)));
        Assert.That(world.Request!.AddExpFund.GuildId, Is.EqualTo(GuildId));
        Assert.That(world.Request.AddExpFund.Fund, Is.EqualTo(300));

        world.Response = new GuildResponse { Error = (int) Maple2.Model.Error.GuildError.s_guild_err_null_member };
        Assert.That(await session.Guild.AddExpFundAsync(5, 0)!, Is.Null);

        world.Request = null;
        Assert.That(session.Guild.AddExpFundAsync(0, 0), Is.Null);
        Assert.That(world.Request, Is.Null);
    }

    // The guild vs game's contribution has no check-in time: only the contribution update goes out.
    [Test]
    public void UpdateMemberContribution_WithoutCheckIn_SendsNoCheckInTime() {
        GameSession session = Start();
        Assert.That(session.Guild.UpdateMemberContribution(test.CharacterId, 0, 12), Is.True);
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(1));

        Assert.That(session.Guild.UpdateMemberContribution(test.CharacterId, 1000, 3), Is.True);
        Assert.That(test.TakeSent(), Has.Count.EqualTo(2));
    }
}
