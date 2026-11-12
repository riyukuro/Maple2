namespace Maple2.Model.Enum;

public enum MailType : byte {
    Player = 1,
    Guild = 2, // a guild mail: listed with the player mails, no sender menu
    // Listed with the player mails without a reply (UIMailListDialog disableReply); a content of digits is a
    // string/systemmailcontent id, as for System (CMailInfo::Read).
    Notice = 3,
    System = 101,
    BlackMarketSale = 102,
    BlackMarketFail = 103,
    BlackMarketListingCancel = 104,
    MesoMarket = 106,
    WeddingSystem = 111,
    WeddingInvite = 112,
    Ad = 200,
}
