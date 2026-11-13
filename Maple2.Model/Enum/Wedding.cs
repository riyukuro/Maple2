namespace Maple2.Model.Enum;

public enum MaritalStatus : short {
    Single = 0,
    Engaged = 1,
    Married = 2,
    // A divorce one partner filed alone, in its cooling off. Client WeddingProxy::IsPartner (weddingproxy.cpp:639)
    // names the filer: the groom (the first partner of the wedding packet) in 3, the bride (the second) in 4.
    DivorceFiledByGroom = 3,
    DivorceFiledByBride = 4,
    // Divorced: the propose cooltime runs (WeddingProxy::IsProposeCooldownOver, weddingproxy.cpp:697).
    Divorced = 5,
}

public enum MarriageExpType : short {
    None = 0,
    coupleMessage = 2,
    Online = 4, // ??
    logoutPanelty = 6,
    attendance = 7, // ??

    // Wedding reward table rows whose log type is not known yet: kept apart from the client's log types (1-13).
    marriedLife = 101,
    marriageRegister = 104,

    // A Blessing Charm used (function WeddingExpItem): the client's log names it s_wedding_dialog_exp_9 and _10, the
    // two lines of one source like the wedding missions' 11 and 12, so 9 the groom's and 10 the bride's use. The
    // wedding reward table's weddingExpItem row is keyed by the groom's type.
    weddingExpItem = 9,
    WeddingExpItemBride = 10,

    // The types of the marriage's exp log the client reads (WeddingProxy::Read, weddingproxy.cpp:227) and shows as
    // s_wedding_dialog_exp_<type> (UIWeddingAffinityDialog::OnLoadComplete, uiweddingaffinitydialog.cpp:87): 11 the
    // groom's, 12 the bride's wedding mission completed.
    WeddingMissionGroom = 11,
    WeddingMissionBride = 12,
}

// The wedding reward table's rewardLimit, numbered as the parser reads it.
public enum MarriageExpLimit {
    none = 0,
    day = 1,
    week = 2,
    month = 3,
}

[Flags]
public enum WeddingHallEntryType {
    Guest = 1,
    Bride = 2,
    Groom = 4,
    GroomBride = Groom | Bride,
}

public enum WeddingHallState {
    weddingComplete,

}
