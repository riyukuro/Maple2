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
