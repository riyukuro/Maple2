using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Guild 86 (CGuildManager::SendGuildMode86, no body): a player without a guild gets the requests they sent, each
// as CGuildJoinRequestForChar::ReadStream reads it (guilddef_1.cpp:404), with the guild's stored member count.
public class GuildAppliedListHandlerTests {
    private TestGameSession test = null!;
    private GameSession session = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        var property = (GuildTable.Property) RuntimeHelpers.GetUninitializedObject(typeof(GuildTable.Property));
        test.SetTable(new GuildTable(
            new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>>(),
            new Dictionary<int, IReadOnlyDictionary<int, GuildTable.House>>(),
            new Dictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>>(),
            new Dictionary<short, GuildTable.Property> { [1] = property with { Level = 1, Capacity = 10 } }));
        test.Start();
        session = test.Session;
        // The list names the player from their PlayerInfo, which the channel asks the World for.
        test.World.AddPlayer(session.AccountId, session.CharacterId, session.PlayerName);
        test.TakeSent();
    }

    private long CreateCharacter(string name) {
        using GameStorage.Request db = test.Storage.Context();
        var model = new Character { AccountId = session.AccountId, Name = name, Job = Job.Newbie, Level = 20, Mastery = new Mastery() };
        model.ReturnMaps.Push(2000062);
        Character? character = db.CreateCharacter(model);
        Assert.That(character, Is.Not.Null);
        return character!.Id;
    }

    private void List() {
        new GuildHandler {
            World = test.World,
            TableMetadata = test.TableMetadata,
            BanWordStorage = null!,
            ServerTableMetadata = test.ServerTableMetadata,
        }.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(86)));
    }

    [Test]
    public void OwnRequests_AreListedWithTheGuildsData() {
        long leader = CreateCharacter("Leader");
        Guild guild;
        GuildApplicationRow row;
        using (GameStorage.Request db = test.Storage.Context()) {
            guild = db.CreateGuild("Wanted", leader)!;
            row = db.CreateGuildApplication(guild.Id, session.CharacterId)!;
        }

        List();

        byte[] packet = test.TakeSent().Single(sent => TestGameSession.Op(sent) == SendOp.Guild);
        var reader = new ByteReader(packet, 2);
        Assert.Multiple(() => {
            Assert.That(reader.ReadByte(), Is.EqualTo(86));
            Assert.That(reader.ReadInt(), Is.EqualTo(1));
            Assert.That(reader.ReadBool(), Is.True);
            Assert.That(reader.ReadLong(), Is.EqualTo(row.Id));
            Assert.That(reader.ReadLong(), Is.EqualTo(guild.Id));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Wanted"));
            Assert.That(reader.ReadInt(), Is.EqualTo(guild.Experience));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo(guild.Emblem));
            reader.Read<AchievementInfo>();
            Assert.That(reader.ReadInt(), Is.EqualTo(1), "member count");
            Assert.That(reader.ReadInt(), Is.EqualTo(guild.Capacity));
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Leader"));
            Assert.That(reader.ReadLong(), Is.EqualTo(session.AccountId));
            Assert.That(reader.ReadLong(), Is.EqualTo(session.CharacterId));
            Assert.That(reader.ReadLong(), Is.EqualTo(row.CreationTime));
            Assert.That(reader.Position, Is.EqualTo(packet.Length));
        });
    }

    // No request: an empty list (count 0), so the page draws no row.
    [Test]
    public void NoRequest_EmptyList() {
        List();

        byte[] packet = test.TakeSent().Single(sent => TestGameSession.Op(sent) == SendOp.Guild);
        Assert.That(Convert.ToHexString(packet, 2, packet.Length - 2), Is.EqualTo("56" + "00000000"));
    }
}
