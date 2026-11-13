namespace Maple2.Model.Enum;

// What happened to a friend entry while its owner was offline. The client reads each one as an action string in the
// friend notices at login (Buddy sub-type 13): "approved", "declined" or "removed".
public enum BuddyNoticeType : byte {
    Approved = 1,
    Declined = 2,
    Removed = 3,
}
