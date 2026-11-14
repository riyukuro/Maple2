namespace Maple2.File.Ingest.Helpers;

// Game event rows the server table itself marks as not for play, by their reference note: "unused data"
// (KR 50000 and 50001, the UserCondition rows of 2015) and "test" (KR 660006, a
// FindDungeonHelperBonus test row, and 90000000, a StringBoard row made for a game event shutdown test). The ingest
// drops them, so neither the live table nor a replay (AttendGiftReplay) runs them. KR 50000 was replayed: its notice
// rows (Table/userConditionEvent.xml group 1) give rewards for the job codes 1 and 10 to 80 only, so for a later
// job the client's notice (UISystemMailNotifyDialog::ShowMail) set no text and showed its placeholders, and its
// detail button opened the 2015 event page.
public static class GameEventScreen {
    public const string UnusedMark = "\uBBF8\uC0AC\uC6A9"; // "unused"
    public const string TestMark = "\uD14C\uC2A4\uD2B8"; // "test"
    public const string ContestMark = "\uCF58\uD14C\uC2A4\uD2B8"; // "contest", which holds "test"

    public static bool MarkedNotForPlay(string? reference) {
        if (string.IsNullOrEmpty(reference)) {
            return false;
        }
        return reference.Contains(UnusedMark) || reference.Replace(ContestMark, string.Empty).Contains(TestMark);
    }
}
