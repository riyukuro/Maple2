using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Web.Controllers;

// The body of irrq.aspx, as CSoapClient::RequestGameRanking writes it: the board key's type, an int, the account,
// character and searched character ids, the key's sub (job tab or ranking id) and date (season id), then the search
// text as a byte length and UTF-16.
public record GameRankingRequest(
    GameRankingType Type,
    int Unknown,
    long AccountId,
    long CharacterId,
    long CharacterIdSearch,
    int RankingId,
    int SeasonId,
    string UserName) {
    // Type 0: the ranking dialog never built the key. UIGameRankingDialog::BuildRankingKey and BuildMyRankKey return
    // before CGameRankingCli::BuildKey when the board has no tab, and the season boards (Dark Stream among them) only
    // get tabs from a season of the client's Table/SeasonData_<type>.xml that holds the server time. The client has no
    // answer reader for type 0, so whatever is sent back, the board stays empty.
    public bool KeyNotBuilt => Type == 0;

    public static GameRankingRequest Read(IByteReader packet) {
        var type = packet.Read<GameRankingType>();
        int unknown = packet.ReadInt(); // start page maybe?
        long accountId = packet.ReadLong();
        long characterId = packet.ReadLong();
        long characterIdSearch = packet.ReadLong(); // parameter
        int rankingId = packet.ReadInt(); // boss ranking id like 23200007
        int seasonId = packet.ReadInt(); // defined in season xmls
        string userName = packet.ReadUnicodeStringWithLength(); // search string
        return new GameRankingRequest(type, unknown, accountId, characterId, characterIdSearch, rankingId, seasonId, userName);
    }
}
