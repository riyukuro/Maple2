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

    // TODO: figure out the values for these
    marriedLife,
    weddingExpItem,

    // The types of the marriage's exp log the client reads (WeddingProxy::Read, weddingproxy.cpp:227) and shows as
    // s_wedding_dialog_exp_<type> (UIWeddingAffinityDialog::OnLoadComplete, uiweddingaffinitydialog.cpp:87): 11 the
    // groom's, 12 the bride's wedding mission completed.
    WeddingMissionGroom = 11,
    WeddingMissionBride = 12,
}

public enum MarriageExpLimit {
    none,
    day,
    month,
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
