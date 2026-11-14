namespace Maple2.Model.Enum;

public enum PortalType : byte {
    Field = 0,
    DungeonReturnToLobby = 1,
    Event = 3, // Pocket Realms & Player Hosted Mini games
    FieldToHome = 5,
    Quest = 6,
    Boss = 8,
    DungeonEnter = 9,
    InHome = 11,
    LeaveDungeon = 13,
    ContentsGuide = 15,
    UgcOx = 16, // The home OX quiz event's stadium portal (the client's portal type 0x10 sends HomeAction 10 sub 5)
}

public enum PortalActionType {
    Interact = 0,
    Touch = 1,
}
