using System.ComponentModel;

namespace Maple2.Model.Enum;

public enum PartyInviteResponse : byte {
    Accept = 1,
    RejectInvite = 9,
    RejectTimeout = 12,
}

public enum PartyVoteType : byte {
    Kick = 1,
    ReadyCheck = 2,
}

public enum PartyMessage {
    [Description("The voting period has ended.")]
    s_party_vote_expired,
    [Description("The party leader has reset the dungeon.")]
    s_field_enteracne_party_notify_reset_dungeon,
    [Description("The vote to kick failed.")]
    s_party_vote_rejected_kick_user,
}

// The sort values of the KMS 2525 client's party finder (UIPartySearchDialog, FUN_140f8ef30 and FUN_140f8f170 in
// uipartysearchdialog.cpp: string 0xb29 "most members" 0xc, 0xb2a "least members" 0xb, 0xb2b "newest to oldest" 0x16,
// 0xb2c "oldest to newest" 0x15).
public enum PartySearchSort : byte {
    LeastMembers = 11,
    MostMembers = 12,
    Oldest = 21,
    Newest = 22,
}
