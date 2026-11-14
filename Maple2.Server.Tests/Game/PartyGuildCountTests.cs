using System.Reflection;
using Maple2.File.Ingest;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// A condition's guildPartyCount (ConditionUtil.Check, PartyManager.GuildMemberCount) counts the party members of
// the player's guild. KR 50000109: play_ensenble_time code 2000001, value 300, partyCount 2, guildPartyCount 2
// (50000108: code 84000001). Two members of one guild meet it; two in no guild do not, as guild id 0 is no guild.
// Before, two guildless members counted as two of one guild.
public class PartyGuildCountTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static GameSession PartyOfTwo(TestGameSession test, long guildId, long otherGuildId) {
        GameSession session = test.Start();
        session.Player.Value.Character.GuildId = guildId;
        PartyMember Member(long accountId, long characterId, long memberGuildId) => new() {
            PartyId = 5,
            Info = new PlayerInfo(new CharacterInfo(accountId, characterId, $"member{characterId}", "", "", Gender.Male, Job.Newbie, 20),
                "", new AchievementInfo(), []) { GuildId = memberGuildId },
        };
        PartyMember leader = Member(session.AccountId, session.CharacterId, guildId);
        var party = new Party(5, leader);
        party.Members[leader.CharacterId] = leader;
        party.Members[session.CharacterId + 1] = Member(session.AccountId + 1, session.CharacterId + 1, otherGuildId);
        session.Party = new PartyManager(test.World, session);
        typeof(PartyManager).GetField("<Party>k__BackingField", Any)!.SetValue(session.Party, party);
        return session;
    }

    private static ConditionMetadata EnsembleTime(string map) =>
        new(ConditionType.play_ensenble_time, 300, new[] { map }.ConvertCodes(), new string[0].ConvertCodes(), 2, 2);

    [TestCase(7, 7, 2, true)]
    [TestCase(0, 0, 0, false)]
    [TestCase(7, 0, 1, false)]
    [TestCase(0, 7, 0, false)]
    [TestCase(7, 8, 1, false)]
    public void GuildPartyCount_50000109_MembersOfTheGuild(long guildId, long otherGuildId, int members, bool counts) {
        var test = new TestGameSession();
        GameSession session = PartyOfTwo(test, guildId, otherGuildId);

        Assert.That(session.Party.GuildMemberCount(), Is.EqualTo(members));
        Assert.That(EnsembleTime("2000001").Check(session, targetLong: 2000001, codeLong: 2000001), Is.EqualTo(counts));
        Assert.That(EnsembleTime("84000001").Check(session, targetLong: 2000001, codeLong: 2000001), Is.False);
    }
}
