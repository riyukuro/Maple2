using Maple2.Database.Extensions;
using Maple2.Model;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Core.Packets.Helper;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class PlayerInfoPacket {
    public static ByteWriter NotFound(long characterId) {
        var pWriter = Packet.Of(SendOp.CharacterInfo);
        pWriter.WriteLong(characterId);
        pWriter.WriteBool(false);

        return pWriter;
    }

    // A session can be written once it has entered the game: Player is set then.
    public static bool CanLoad(GameSession session) {
        return session.Player is not null;
    }

    public static ByteWriter Load(GameSession session) {
        var pWriter = Packet.Of(SendOp.CharacterInfo);
        pWriter.WriteLong(session.Player.Value.Character.Id);
        pWriter.WriteBool(true);
        pWriter.WriteLong(); // Unknown (AccountId probably, but why is it not set?)
        pWriter.WriteLong(session.Player.Value.Character.Id);
        pWriter.WriteLong(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        using (var buffer = new PoolByteWriter()) {
            buffer.WriteDetailsBuffer(session.Player, GuildRankName(session.Guild?.GetRank(session.CharacterId)));

            pWriter.WriteInt(buffer.Length);
            pWriter.WriteBytes(buffer.Buffer, 0, buffer.Length);
        }

        using (var buffer = new PoolByteWriter()) {
            int count = session.Item.Equips.Gear.Count + session.Item.Equips.Outfit.Count;
            buffer.WriteByte((byte) count);
            foreach (Item item in session.Item.Equips.Gear.Values) {
                buffer.WriteEquip(item);
            }
            foreach (Item item in session.Item.Equips.Outfit.Values) {
                buffer.WriteEquip(item);
            }

            // The second outfit tab: whether it is active (the viewer's dialog shows the tab by it) and its period.
            Character character = session.Player.Value.Character;
            buffer.WriteBool(SkinTabPeriod.IsActive(character.SkinTabStart, character.SkinTabEnd, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            buffer.WriteLong(character.SkinTabStart);
            buffer.WriteLong(character.SkinTabEnd);
            // The second outfit tab's items.
            buffer.WriteByte((byte) session.Item.Equips.Outfit2.Count);
            foreach (Item item in session.Item.Equips.Outfit2.Values) {
                buffer.WriteEquip(item);
            }

            pWriter.WriteInt(buffer.Length);
            pWriter.WriteBytes(buffer.Buffer, 0, buffer.Length);
        }

        using (var buffer = new PoolByteWriter()) {
            buffer.WriteByte((byte) session.Item.Equips.Badge.Count);
            foreach (Item item in session.Item.Equips.Badge.Values) {
                buffer.WriteBadge(item);
            }

            pWriter.WriteInt(buffer.Length);
            pWriter.WriteBytes(buffer.Buffer, 0, buffer.Length);
        }

        return pWriter;
    }

    // The name of the player's guild rank, shown after the guild name (s_char_info_guild_name); empty without a guild.
    public static string GuildRankName(GuildRank? rank) {
        return rank?.Name ?? string.Empty;
    }

    // The home's architect scores as the info window reads them: CPCData m_n408 then m_n40c, which
    // UIPCInfoDialog::vfunc_102 (uipcinfodialog.cpp:1328) shows as "{m_n40c} / {m_n408}" (s_char_info_home_commend),
    // the same "this month / all time" order as the home info window (UIHomeInfoDialog, uihomeinfodialog.cpp:410). So
    // the all time score comes first.
    public static void WriteArchitectScores(IByteWriter buffer, Home home) {
        buffer.WriteInt(home.ArchitectScore);
        buffer.WriteInt(home.CurrentArchitectScore);
    }

    private static void WriteDetailsBuffer(this PoolByteWriter buffer, FieldPlayer fieldPlayer, string guildRank) {
        Player player = fieldPlayer;
        buffer.WriteLong(player.Account.Id);
        buffer.WriteLong(player.Character.Id);
        buffer.WriteUnicodeString(player.Character.Name);
        buffer.WriteShort(player.Character.Level);
        buffer.WriteInt((int) player.Character.Job.Code());
        buffer.Write<Job>(player.Character.Job);
        buffer.WriteInt((int) player.Character.Gender);
        buffer.WriteInt(player.Account.PrestigeLevel);
        buffer.WriteByte();

        #region Stats
        for (int i = 0; i < Stat.TOTAL; i++) {
            for (int j = 0; j < Stats.BASIC_TOTAL; j++) {
                buffer.WriteLong(fieldPlayer.Stats.Values[(BasicAttribute) j][i]);
            }
        }
        // Client: CPCData::Read (commonfiles/pcdata.cpp:101): after the 3 x 35 longs an int pair count, then per pair
        // a short special attribute and an int value. Only the non-zero ones are listed.
        var specials = new List<(short Type, int Value)>();
        for (int i = 0; i < Stats.SPECIAL_TOTAL; i++) {
            long total = fieldPlayer.Stats.Values[(SpecialAttribute) i].Total;
            if (total != 0) {
                specials.Add(((short) i, (int) Math.Clamp(total, int.MinValue, int.MaxValue)));
            }
        }
        buffer.WriteInt(specials.Count);
        foreach ((short type, int value) in specials) {
            buffer.WriteShort(type);
            buffer.WriteInt(value);
        }
        #endregion

        buffer.WriteUnicodeString(player.Character.Picture);
        buffer.WriteUnicodeString(player.Character.Motto);
        buffer.WriteUnicodeString(player.Character.GuildName);
        buffer.WriteUnicodeString(guildRank);

        buffer.WriteUnicodeString(player.Home.Name);
        buffer.WriteInt(player.Home.PlotMapId);
        buffer.WriteInt(player.Home.PlotNumber);
        buffer.WriteInt(player.Home.ApartmentNumber);

        buffer.WriteInt(player.Character.Title);
        int[] titles = player.Unlock.TitlesCopy(); // read for another player's info window
        buffer.WriteInt(titles.Length);
        foreach (int title in titles) {
            buffer.WriteInt(title);
        }

        buffer.WriteInt(player.Character.AchievementInfo.Total);
        buffer.WriteInt(fieldPlayer.Stats.Values.GearScore);
        buffer.WriteLong(player.Character.LastModified.ToEpochSeconds()); // Time entered map/logged in
        WriteArchitectScores(buffer, player.Home);
        buffer.Write<SkinColor>(player.Character.SkinColor);
        buffer.WriteClass<MarriageInfo>(player.Character.MarriageInfo);
    }
}
