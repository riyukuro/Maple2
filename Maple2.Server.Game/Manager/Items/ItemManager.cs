using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Manager.Items;

public class ItemManager {
    private readonly GameSession session;
    private readonly ItemStatsCalculator itemStatsCalc;
    private readonly Serilog.ILogger logger = Serilog.Log.Logger.ForContext<ItemManager>();

    public readonly EquipManager Equips;
    public readonly InventoryManager Inventory;
    public FurnishingManager Furnishing { get; private set; }

    public ItemManager(GameStorage.Request db, GameSession session, ItemStatsCalculator itemStatsCalc) {
        this.session = session;
        this.itemStatsCalc = itemStatsCalc;

        Equips = new EquipManager(db, session);
        Inventory = new InventoryManager(db, session);
        Furnishing = new FurnishingManager(db, session);
    }

    public void ReInstantiateFurnishing() {
        using GameStorage.Request db = session.GameStorage.Context();
        Furnishing = new FurnishingManager(db, session);
        Furnishing.Load();
    }

    /// <summary>
    /// Retrieves an gear from inventory or equipment.
    /// </summary>
    /// <param name="uid">Uid of the gear to retrieve</param>
    /// <returns>Item if it exists</returns>
    public Item? GetGear(long uid) {
        Item? item = Inventory.Get(uid, InventoryType.Gear);
        return item ?? Equips.Gear.Values.FirstOrDefault(gear => gear.Uid == uid);
    }

    // The item the socket pages of the item socket dialog work on: the client lists inventory tabs 0 and 1 (gear and
    // outfit) and the equipped gear and outfits (ItemSocketRules.Inventories), so an outfit with sockets is one too.
    public Item? GetSocketItem(long uid) {
        return GetGear(uid) ?? GetOutfit(uid);
    }

    /// <summary>
    /// Retrieves an outfit from inventory or equipment.
    /// </summary>
    /// <param name="uid">Uid of the outfit to retrieve</param>
    /// <returns>Item if it exists</returns>
    public Item? GetOutfit(long uid) {
        Item? item = Inventory.Get(uid, InventoryType.Outfit);
        return item ?? Equips.Outfit.Values.FirstOrDefault(outfit => outfit.Uid == uid)
            ?? Equips.Outfit2.Values.FirstOrDefault(outfit => outfit.Uid == uid);
    }

    public void Bind(Item item) {
        if (item.Transfer?.Bind(session.Player.Value.Character) == true) {
            session.Send(ItemBindPacket.For(item, inBag: Inventory.Get(item.Uid) != null));
        }
    }

    public bool MailItem(Item item) {
        lock (session.Item) {
            using GameStorage.Request db = session.GameStorage.Context();
            var mail = new Mail {
                Type = MailType.System,
                ReceiverId = session.CharacterId,
                Content = "50000000", // id from string/en/systemmailcontentna.xml
            };

            mail = db.CreateMail(mail);
            if (mail == null) {
                return false;
            }

            if (item.Uid == 0) {
                item.Slot = -1;
                Item? newAdd = db.CreateItem(mail.Id, item);
                if (newAdd == null) {
                    // No empty mail is left behind when the item could not be created.
                    DeleteFailedMail(mail.Id);
                    return false;
                }
                item = newAdd;
            }

            mail.Items.Add(item);

            try {
                session.World.MailNotification(new MailNotificationRequest {
                    CharacterId = session.CharacterId,
                    MailId = mail.Id,
                });
            } catch { /* ignored */ }
        }
        return true;
    }

    // Adds the item to the inventory; when the bag does not take it, it goes by mail. A new item (no uid) becomes a
    // mail row; an item that already has a row moves that row to the mail.
    public bool AddOrMail(Item item, bool notifyNew = false, bool commit = false) {
        lock (session.Item) {
            if (Inventory.Add(item, notifyNew, commit)) {
                return true;
            }
            return item.Uid == 0 ? MailItem(item) : MailHeldItem(item);
        }
    }

    // Mails an item row that another owner holds (a mannequin's item): the row moves to the mail as it is.
    public bool MailHeldItem(Item item) {
        lock (session.Item) {
            using GameStorage.Request db = session.GameStorage.Context();
            Mail? mail = db.CreateMail(new Mail {
                Type = MailType.System,
                ReceiverId = session.CharacterId,
                Content = "50000000", // id from string/en/systemmailcontentna.xml
            });
            if (mail == null) {
                return false;
            }

            (ItemGroup group, short slot) = (item.Group, item.Slot);
            item.Group = ItemGroup.Default;
            item.Slot = -1;
            if (!db.SaveItems(mail.Id, item)) {
                // No empty mail is left behind, and the item keeps its place for its old owner.
                (item.Group, item.Slot) = (group, slot);
                DeleteFailedMail(mail.Id);
                return false;
            }

            try {
                session.World.MailNotification(new MailNotificationRequest {
                    CharacterId = session.CharacterId,
                    MailId = mail.Id,
                });
            } catch { /* ignored */ }
        }
        return true;
    }

    // On its own request: the failed write's changes are still tracked by the request that tried it, and its next save
    // would write them again (the item row moved to, or created for, the deleted mail).
    private void DeleteFailedMail(long mailId) {
        using GameStorage.Request db = session.GameStorage.Context();
        if (!db.DeleteMail(mailId, session.CharacterId)) {
            logger.Error("Empty mail {MailId} of {CharacterId} not deleted", mailId, session.CharacterId);
        }
    }

    public void Save(GameStorage.Request db) {
        Equips.Save(db);
        Inventory.Save(db);
        Furnishing.Save(db);
    }
}
