using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// GuideRecord 0x0057: i32 count, then per record i32 guide id, i32 step. The client sends its whole map
// (guideentity_1b.cpp:217) and drops id 0 when it reads the map back (guideentity_1b.cpp:239).
public static class GuideRecordRules {
    // The most guide ids kept for one character. The list is stored with the character and sent whole at
    // every login.
    public const int MaxRecords = 2048;

    // Reads the body into records. False, with nothing read past the count, when the count is negative or
    // names more records than the packet holds. Id 0 is skipped; a new id past MaxRecords is skipped.
    public static bool Read(IByteReader packet, IDictionary<int, int> records) {
        int count = packet.ReadInt();
        if (count < 0 || count > packet.Available / 8) {
            return false;
        }

        for (int i = 0; i < count; i++) {
            int guideId = packet.ReadInt();
            int step = packet.ReadInt();
            if (guideId == 0) {
                continue;
            }
            if (records.Count >= MaxRecords && !records.ContainsKey(guideId)) {
                continue;
            }
            records[guideId] = step;
        }
        return true;
    }
}
