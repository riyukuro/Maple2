using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Party;
using Maple2.Model.Game.Pvp;
using Maple2.Server.Game.PacketHandlers;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The squad register (0xC2 mode 4) refusals and the client line each one gets (OnPartyResultMessage, partymanager.cpp).
public class SurvivalSquadTests {
    private static PartyMember Member(long characterId, bool online = true) => new() {
        PartyId = 1,
        Info = new PlayerInfo(new CharacterInfo(characterId, characterId, $"c{characterId}", "", "", Gender.Male, Job.Newbie, 20),
            "", new AchievementInfo(), []) { Channel = (short) (online ? 1 : -1) },
    };

    private static Party Party(int count, long leader = 1) {
        var party = new Party(1, Member(leader));
        for (long id = 1; id <= count; id++) {
            party.Members[id] = Member(id);
        }
        return party;
    }

    [Test]
    public void Refusal_ByClientCheckOrder() {
        // No party or a party of one: refused with no line (the client lets a player with no party send it).
        Assert.That(MapleSurvivalHandler.SquadRefusal(null, 1, true, false), Is.EqualTo(PartyError.none));
        Assert.That(MapleSurvivalHandler.SquadRefusal(Party(1), 1, true, false), Is.EqualTo(PartyError.none));
        Assert.That(MapleSurvivalHandler.SquadRefusal(Party(3), 2, true, false), Is.EqualTo(PartyError.s_party_err_isNotChief));
        Assert.That(MapleSurvivalHandler.SquadRefusal(Party(5), 1, true, false),
            Is.EqualTo(PartyError.s_maple_survival_error_squad_register_over_count));
        Party offline = Party(3);
        offline.Members[3] = Member(3, online: false);
        Assert.That(MapleSurvivalHandler.SquadRefusal(offline, 1, true, false), Is.EqualTo(PartyError.s_party_err_hasOfflineUser));
        Assert.That(MapleSurvivalHandler.SquadRefusal(Party(2), 1, true, true), Is.EqualTo(PartyError.s_party_err_inside_survival));
        Assert.That(MapleSurvivalHandler.SquadRefusal(Party(2), 1, false, false), Is.EqualTo(PartyError.none));
        Assert.That(MapleSurvivalHandler.SquadRefusal(Party(4), 1, true, false), Is.Null);
        // SurvivalContentsTeam off: a squad that would go is refused with no line.
        Assert.That(MapleSurvivalHandler.SquadRefusal(Party(4), 1, true, false, squadOn: false), Is.EqualTo(PartyError.none));
    }

    [Test]
    public void WorldError_MapsToClientLine() {
        Assert.That(MapleSurvivalHandler.SquadError((int) RankDuelError.Busy), Is.EqualTo(PartyError.s_party_err_inside_survival));
        Assert.That(MapleSurvivalHandler.SquadError((int) RankDuelError.Queued), Is.EqualTo(PartyError.s_party_err_survival_has_solo_register));
        Assert.That(MapleSurvivalHandler.SquadError((int) RankDuelError.OtherQueue), Is.EqualTo(PartyError.s_party_err_another_matching));
        Assert.That(MapleSurvivalHandler.SquadError(null), Is.EqualTo(PartyError.none));
        Assert.That((byte) PartyError.s_party_err_isNotChief, Is.EqualTo(0x2c));
        Assert.That((byte) PartyError.s_maple_survival_error_squad_register_over_count, Is.EqualTo(0x31));
    }
}
