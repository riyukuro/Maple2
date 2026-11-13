using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Containers;

namespace Maple2.Server.Tests.World;

// A guild member expelled while offline (CGuildManager::OnPacketExpelledOfflineMembers): the world keeps the expel and
// the member gets Guild 76 at its next login, once. An online member gets Guild 9 from the broadcast instead.
public class GuildExpelNoticeTests {
    private TestGameSession test = null!;
    private Guild guild = null!;
    private GuildManager manager = null!;
    private GuildMember master = null!;
    private GuildMember member = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        // The member delete is a batch delete (Z.EntityFramework.Plus), which needs a context factory in memory.
        Z.EntityFramework.Extensions.EntityFrameworkManager.ContextFactory = _ => new Ms2Context(test.Options);
        using (GameStorage.Request db = test.Storage.Context()) {
            guild = db.CreateGuild("Guild", test.CharacterId) ?? throw new InvalidOperationException("guild row not created");
        }
        master = Member(test.CharacterId + 500, "Master", 0, channel: 1);
        member = Member(test.CharacterId, "Member", 1, channel: -1);
        guild.Members[master.CharacterId] = master;
        guild.Members[member.CharacterId] = member;
        // The session character holds the stored member row (made with the guild); the master leads in memory.
        guild.LeaderCharacterId = master.CharacterId;
        manager = new GuildManager(guild) {
            GameStorage = test.Storage,
            ChannelClients = new ChannelClientLookup(),
            TableMetadata = test.TableMetadata,
        };
    }

    [TearDown]
    public void TearDown() {
        Z.EntityFramework.Extensions.EntityFrameworkManager.ContextFactory = null;
        manager.Dispose();
        master.Dispose();
        member.Dispose();
    }

    private GuildMember Member(long characterId, string name, byte rank, short channel) {
        var info = new PlayerInfo(new CharacterInfo(characterId + 100, characterId, name, "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []) {
            Channel = channel,
        };
        return new GuildMember { GuildId = guild.Id, Info = info, Rank = rank, JoinTime = 1000 };
    }

    private IList<GuildExpelNotice> Take() {
        using GameStorage.Request db = test.Storage.Context();
        return db.TakeGuildExpelNotices(member.CharacterId);
    }

    [Test]
    public void OfflineMember_IsKeptWithTheExpellerName() {
        long before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.That(manager.Expel(master.CharacterId, member.CharacterId), Is.EqualTo(GuildError.none));

        IList<GuildExpelNotice> notices = Take();
        Assert.That(notices, Has.Count.EqualTo(1));
        Assert.That(notices[0].Name, Is.EqualTo("Master"));
        Assert.That(notices[0].Time, Is.InRange(before, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        Assert.That(Take(), Is.Empty, "the notices are deleted once taken");
    }

    [Test]
    public void OnlineMember_IsNotKept() {
        member.Info.Channel = 2;
        Assert.That(manager.Expel(master.CharacterId, member.CharacterId), Is.EqualTo(GuildError.none));
        Assert.That(Take(), Is.Empty);
    }

    [Test]
    public void RefusedExpel_IsNotKept() {
        Assert.That(manager.Expel(member.CharacterId, master.CharacterId), Is.Not.EqualTo(GuildError.none));
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.TakeGuildExpelNotices(master.CharacterId), Is.Empty);
        Assert.That(Take(), Is.Empty);
    }

    // Guild 76 as the client reads it: an int count, then per entry the name, the reason string and a long time; only
    // the reason "expeled" shows s_guild_notify_expeled_from.
    [Test]
    public void ExpelledOffline_Layout() {
        byte[] bytes = GuildPacket.ExpelledOffline(new List<GuildExpelNotice> {
            new("Ab", 0x0102),
            new("C", 7),
        }).ToArray();

        string reason = "0700" + Convert.ToHexString(System.Text.Encoding.Unicode.GetBytes("expeled"));
        string expected = Convert.ToHexString(BitConverter.GetBytes((short) SendOp.Guild))
                          + "4C" + "02000000"
                          + "0200" + "41006200" + reason + "0201000000000000"
                          + "0100" + "4300" + reason + "0700000000000000";
        Assert.That(Convert.ToHexString(bytes), Is.EqualTo(expected));
    }

    // At login the kept expels go out as one Guild 76, with or without a guild, and are not sent again.
    [Test]
    public void Login_SendsKeptExpelsOnce() {
        Assert.That(manager.Expel(master.CharacterId, member.CharacterId), Is.EqualTo(GuildError.none));
        Maple2.Server.Game.Session.GameSession session = test.Start();
        // The test session's guild manager is uninitialized; give it the session as its constructor does.
        typeof(Maple2.Server.Game.Manager.GuildManager)
            .GetField("session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(session.Guild, session);
        test.TakeSent();

        session.Guild.LoadExpelNotices();
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.Guild }));
        Assert.That(sent[0][2], Is.EqualTo(76));
        Assert.That(BitConverter.ToInt32(sent[0], 3), Is.EqualTo(1));

        session.Guild.LoadExpelNotices();
        Assert.That(test.TakeSent(), Is.Empty);
    }
}
