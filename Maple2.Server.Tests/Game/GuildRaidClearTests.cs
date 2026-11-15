using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Serilog;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Tests.Game;

// The Game side of the guild raid records: a guild raid room's clear that gives the clear reward goes to World (off
// the field's thread), and World's Guild 37 sets the member's value and reaches the client.
public class GuildRaidClearTests {
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const int Room = 90000300;
    private const long GuildId = 77;

    private sealed class GuildWorld : WorldClient {
        public readonly ConcurrentQueue<GuildRequest> Requests = new();
        public readonly SemaphoreSlim Received = new(0);

        public override GuildResponse Guild(GuildRequest request, CallOptions options) {
            Requests.Enqueue(request);
            Received.Release();
            return new GuildResponse();
        }

        // The clear's records are stored under the account lock.
        public override LockResponse AcquireLock(LockRequest request, CallOptions options) => new();
        public override LockResponse ReleaseLock(LockRequest request, CallOptions options) => new();
    }

    private TestGameSession test = null!;
    private GuildWorld world = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
    }

    private static void Set<T>(object target, string field, object? value) {
        (typeof(T).GetField(field, Flags) ?? throw new MissingFieldException(typeof(T).Name, field)).SetValue(target, value);
    }

    private GameSession Start(int rewardCount) {
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 90000301, rewardCount) with { GroupType = DungeonGroupType.guildRaid };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room }));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.SetTable(new GuildTable(
            new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>>(),
            new Dictionary<int, IReadOnlyDictionary<int, GuildTable.House>>(),
            new Dictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>>(),
            new Dictionary<short, GuildTable.Property>(),
            Raids: new Dictionary<int, GuildTable.Raid> { [Room] = new(Room, 300, 1.75f, 1.3f) }));

        GameSession session = test.Start();
        world = new GuildWorld();
        Set<GameSession>(session, "<World>k__BackingField", world);

        var guild = new Guild(GuildId, "Raid", test.AccountId, test.CharacterId, "Master");
        var info = new PlayerInfo(new CharacterInfo(test.AccountId, test.CharacterId, "Master", "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []);
        guild.Members[test.CharacterId] = new GuildMember { GuildId = GuildId, Info = info };
        var manager = (GuildManager) RuntimeHelpers.GetUninitializedObject(typeof(GuildManager));
        GC.SuppressFinalize(manager);
        Set<GuildManager>(manager, "session", session);
        Set<GuildManager>(manager, "logger", new LoggerConfiguration().CreateLogger());
        Set<GuildManager>(manager, "<Guild>k__BackingField", guild);
        var property = (GuildTable.Property) RuntimeHelpers.GetUninitializedObject(typeof(GuildTable.Property));
        Set<GuildManager>(manager, "<Properties>k__BackingField", property with { Level = 3, RaidExp = 180, RaidFund = 36000 });
        session.Guild = manager;

        test.EnterDungeon(room).State = DungeonState.Clear;
        test.TakeSent();
        return session;
    }

    [Test]
    public void ClearWithTheClearReward_GoesToWorld() {
        GameSession session = Start(rewardCount: 10);

        session.Dungeon.CompleteDungeon(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        Assert.That(world.Received.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(world.Requests.TryDequeue(out GuildRequest? request), Is.True);
        Assert.That(request!.RequestorId, Is.EqualTo(test.CharacterId));
        Assert.That(request.RaidClear.GuildId, Is.EqualTo(GuildId));
        Assert.That(request.RaidClear.DungeonRoomId, Is.EqualTo(Room));
        Assert.That(world.Requests, Is.Empty);
    }

    // The result's guild lines (kinds 4 and 5, shown for a guild raid room only) carry the fund and exp the clear
    // adds to the guild: KR level 3 (raidGuildFund 36000, raidGuildExp 180) at 1.75 and 1.3. Before, both showed 0.
    [Test]
    public void ClearWithTheClearReward_ResultShowsTheGuildFundAndExp() {
        GameSession session = Start(rewardCount: 10);

        session.Dungeon.CompleteDungeon(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        IDictionary<DungeonRewardType, int> rewards = session.Dungeon.UserRecord!.Rewards;
        Assert.That((rewards[DungeonRewardType.GuildFund], rewards[DungeonRewardType.GuildExp]), Is.EqualTo((63000, 234)));
    }

    // No reward count left: the clear gives no clear reward, so it does not count for the guild raid.
    [Test]
    public void ClearWithoutTheClearReward_SendsNothing() {
        GameSession session = Start(rewardCount: 0);

        session.Dungeon.CompleteDungeon(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        Assert.That(world.Received.Wait(TimeSpan.FromMilliseconds(300)), Is.False);
        Assert.That(world.Requests, Is.Empty);
        Assert.That(session.Dungeon.UserRecord!.Rewards.ContainsKey(DungeonRewardType.GuildFund), Is.False);
    }

    [Test]
    public void RaidClearOfARoomNotInTheTable_IsNotSent() {
        GameSession session = Start(rewardCount: 10);

        Assert.That(session.Guild.RaidClear(Room + 1), Is.Null);
        Assert.That(world.Requests, Is.Empty);
    }

    // World's Guild 37: the member's value is set and the client gets the update; an unknown name changes nothing.
    [Test]
    public void UpdateMemberRaid_SetsTheValueAndSendsIt() {
        GameSession session = Start(rewardCount: 10);

        Assert.That(session.Guild.UpdateMemberRaid("Master", Room, 600), Is.True);
        Assert.That(session.Guild.GetMember(test.CharacterId)!.Raids[Room], Is.EqualTo(600));
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(TestGameSession.Op(sent[0]), Is.EqualTo(Maple2.Server.Core.Constants.SendOp.Guild));
        Assert.That(sent[0][2], Is.EqualTo(0x25));

        Assert.That(session.Guild.UpdateMemberRaid("Nobody", Room, 5), Is.False);
        Assert.That(test.TakeSent(), Is.Empty);
    }
}
