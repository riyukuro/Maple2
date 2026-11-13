using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Containers;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.World;

// The guild raid records: a clear on the World server adds the raid's points to the member and stores them, the
// weekly reset clears them, and the client gets them in the member block and in Guild 37.
public class GuildRaidTests {
    private const int Room = 70001;

    private TestGameSession test = null!;
    private Guild guild = null!;
    private GuildManager manager = null!;
    private GuildMember master = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        var property = (GuildTable.Property) RuntimeHelpers.GetUninitializedObject(typeof(GuildTable.Property));
        property = property with { Level = 1, Capacity = 10, FundMax = 1000 };
        test.SetTable(new GuildTable(
            new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>>(),
            new Dictionary<int, IReadOnlyDictionary<int, GuildTable.House>>(),
            new Dictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>>(),
            new Dictionary<short, GuildTable.Property> { [1] = property },
            Raids: new Dictionary<int, GuildTable.Raid> { [Room] = new(Room, 300, 1f, 1f) }));

        using (GameStorage.Request db = test.Storage.Context()) {
            guild = db.CreateGuild("Raid", test.CharacterId) ?? throw new InvalidOperationException("guild row not created");
        }
        master = Member(test.CharacterId, "Master");
        guild.Members[master.CharacterId] = master;
        manager = new GuildManager(guild) {
            GameStorage = test.Storage,
            ChannelClients = new ChannelClientLookup(),
            TableMetadata = test.TableMetadata,
        };
    }

    [TearDown]
    public void TearDown() {
        manager.Dispose();
        master.Dispose();
    }

    private GuildMember Member(long characterId, string name) {
        var info = new PlayerInfo(new CharacterInfo(characterId + 100, characterId, name, "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []);
        return new GuildMember { GuildId = guild.Id, Info = info, JoinTime = 1000 };
    }

    private IDictionary<int, int>? StoredRaids() {
        using var context = new Ms2Context(test.Options);
        return context.GuildMember.Single(member => member.CharacterId == master.CharacterId).Raids;
    }

    [Test]
    public void Clear_AddsThePointsAndStoresThem() {
        Assert.That(manager.RaidClear(master.CharacterId, Room), Is.EqualTo(GuildError.none));
        Assert.That(manager.RaidClear(master.CharacterId, Room), Is.EqualTo(GuildError.none));

        Assert.That(master.Raids[Room], Is.EqualTo(600));
        Assert.That(StoredRaids(), Is.EquivalentTo(new Dictionary<int, int> { [Room] = 600 }));
    }

    [Test]
    public void Clear_RefusedForAStrangerOrARoomNotInTheTable() {
        Assert.That(manager.RaidClear(999, Room), Is.EqualTo(GuildError.s_guild_err_null_member));
        Assert.That(manager.RaidClear(master.CharacterId, Room + 1), Is.EqualTo(GuildError.s_guild_err_unknown));
        Assert.That(master.Raids, Is.Empty);
    }

    [Test]
    public void WeeklyReset_ClearsTheRecords() {
        manager.RaidClear(master.CharacterId, Room);
        manager.WeeklyReset();

        Assert.That(master.Raids, Is.Empty);
    }

    [Test]
    public void Rules_AddStopsAtIntMaxAndResetCountsTheMembersThatHadAny() {
        GuildMember other = Member(test.CharacterId + 500, "Other");
        Assert.That(GuildRules.AddRaidClear(master, Room, int.MaxValue - 1), Is.EqualTo(int.MaxValue - 1));
        Assert.That(GuildRules.AddRaidClear(master, Room, 5), Is.EqualTo(int.MaxValue));
        Assert.That(GuildRules.AddRaidClear(master, Room + 1, -5), Is.EqualTo(0));

        Assert.That(GuildRules.ResetRaids(new[] { master, other }), Is.EqualTo(1));
        Assert.That(master.Raids, Is.Empty);
        other.Dispose();
    }

    // The member block ends with CGuildMemberRaid (an int count, then id and value pairs) and the offline flag.
    [Test]
    public void MemberBlock_EndsWithTheRaids() {
        var empty = new ByteWriter();
        empty.WriteClass<GuildMember>(master);
        master.Raids[Room] = 300;
        var one = new ByteWriter();
        one.WriteClass<GuildMember>(master);

        byte[] emptyBytes = empty.ToArray();
        byte[] oneBytes = one.ToArray();
        Assert.That(oneBytes.Length, Is.EqualTo(emptyBytes.Length + 8));
        Assert.That(Convert.ToHexString(emptyBytes[^5..]), Is.EqualTo("00000000" + "01"));
        Assert.That(Convert.ToHexString(oneBytes[^13..]), Is.EqualTo("01000000" + "71110100" + "2C010000" + "01"));
    }

    // Guild 37 as OnPacketMemberRaidCount reads it: the name, the raid and the value.
    [Test]
    public void MemberRaid_Layout() {
        byte[] bytes = GuildPacket.MemberRaid("Ab", Room, 300).ToArray();

        string expected = Convert.ToHexString(BitConverter.GetBytes((short) SendOp.Guild))
                          + "25" + "0200" + "41006200" + "71110100" + "2C010000";
        Assert.That(Convert.ToHexString(bytes), Is.EqualTo(expected));
    }
}
