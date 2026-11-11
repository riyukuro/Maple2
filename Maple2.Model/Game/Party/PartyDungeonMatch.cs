using Maple2.PacketLib.Tools;

namespace Maple2.Model.Game;

// Party 32 (0x20), the dungeon match search, as the client sends it: UIDungeonSearchPage::SendPartyMode32
// (uidungeonsearchdialog.cpp:450) with the rooms picked in the search window, and
// UIDungeonControlCenter::SendPartyMode32 (uidungeoncategory.cpp:630) with the bookmarked rooms. Kind is the
// match kind the search runs under (1 from both senders; the register reply 0x1e names the same kind). Rewarded
// is true from the find button and false after the client's confirm that no picked room gives a reward any more,
// and from the control center.
public readonly record struct PartyDungeonMatchRequest(byte Kind, IReadOnlyList<int> RoomIds, bool Rewarded);

public static class PartyDungeonMatch {
    // The room list is a std::set on the client (FUN_140c0dd90): a count, then that many room ids. A count that
    // is negative or longer than the body is not the client's: null.
    public static PartyDungeonMatchRequest? ReadSearch(IByteReader packet) {
        if (packet.Available < 5) {
            return null;
        }
        byte kind = packet.ReadByte();
        int count = packet.ReadInt();
        if (count < 0 || (long) count * 4 + 1 > packet.Available) {
            return null;
        }
        var roomIds = new List<int>(count);
        for (int i = 0; i < count; i++) {
            roomIds.Add(packet.ReadInt());
        }
        bool rewarded = packet.ReadBool();
        return new PartyDungeonMatchRequest(kind, roomIds, rewarded);
    }

    // Party 33 (0x21), the party search for one room: UIDungeonRoomInfo::OnPartySearch
    // (uidungeonroomdialog.cpp) sends the room id when its search button is not on; when it is on it sends
    // Party 34 (0x22) with a byte, the match kind (Unk_ClubBuffTypeOf of the room's group type; 1 from the
    // control center). Null when the body is short.
    public static int? ReadPartySearch(IByteReader packet) {
        return packet.Available < 4 ? null : packet.ReadInt();
    }
}
