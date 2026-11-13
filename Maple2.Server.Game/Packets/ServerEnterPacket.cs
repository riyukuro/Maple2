using System.Text.RegularExpressions;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class ServerEnterPacket {
    public static ByteWriter Request(IActor<Player> fieldPlayer) {
        Player player = fieldPlayer.Value;

        var pWriter = Packet.Of(SendOp.ServerEnter);
        pWriter.WriteInt(fieldPlayer.ObjectId);
        pWriter.WriteLong(player.Character.Id);
        pWriter.WriteShort(player.Character.Channel);
        pWriter.WriteLong(player.Character.Exp);
        pWriter.WriteLong(player.Character.RestExp);
        pWriter.WriteLong(player.Currency.Meso);
        // The client (CSceneStateGame::OnServerEnter, scenestategame.cpp:2088-2105) adds up the first four as the
        // merets (FUN_1409304d0, as in the Meret packet) and takes the fifth as the red merets (SetRedMeratBalance).
        pWriter.WriteLong(player.Currency.Meret);
        pWriter.WriteLong();
        pWriter.WriteLong();
        pWriter.WriteLong();
        pWriter.WriteLong(player.Currency.GameMeret);

        pWriter.WriteLong(player.Currency.ValorToken);
        pWriter.WriteLong(player.Currency.Treva);
        pWriter.WriteLong(player.Currency.Rue);
        pWriter.WriteLong(player.Currency.HaviFruit);
        pWriter.WriteLong(player.Currency.ReverseCoin);
        pWriter.WriteLong(player.Currency.MentorToken);
        pWriter.WriteLong(player.Currency.MenteeToken);
        pWriter.WriteLong(player.Currency.StarPoint);
        pWriter.WriteLong(player.Currency.MesoToken);

        pWriter.WriteUnicodeString(player.Character.Picture);
        pWriter.WriteByte();
        pWriter.WriteShort((short) player.Unlock.Maps.Count);
        foreach (int mapId in player.Unlock.Maps) {
            pWriter.WriteInt(mapId);
        }
        pWriter.WriteShort((short) player.Unlock.Taxis.Count);
        foreach (int mapId in player.Unlock.Taxis) {
            pWriter.WriteInt(mapId);
        }
        // The guild championship's guild the character cheers for (CGuildManager::SetTargetGuild), of the season it chose
        // it in (Pvp 4).
        pWriter.WriteLong(player.Character.ChampionGuildId);
        pWriter.WriteUnicodeString(player.Character.ChampionGuildName);
        pWriter.WriteUnicodeString(MaviewUrl(Target.WebUri));
        pWriter.WriteUnicodeString();
        pWriter.WriteUnicodeString(AllowedUrls(Target.WebUri));

        return pWriter;
    }

    // CSceneStateGame::OnServerEnter: the maview url (CWebBrowserService::SetMaviewUrl; the base of "maview:" urls such
    // as the notice banner's), the web api url and the url patterns the in-game browser may open (CAtlRegExp, space
    // separated; COffscreenWebBrowser::OnBeforeBrowse hands any other url to the system). Chosen here: the web server.
    public static string MaviewUrl(Uri web) => web.GetLeftPart(UriPartial.Authority);

    public static string AllowedUrls(Uri web) => $"^https?://{Regex.Escape(web.Host)}(:\\d+)?";
}
