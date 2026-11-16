using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

// On the field thread: a push takes items, mesos and merets and gathers or adds a buff, which the field thread's
// handlers change too.
public class SmartPushHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.SmartPush;

    public override void Handle(GameSession session, IByteReader packet) {
        // The same four fields on every push; the last one only marks an extension of a running effect.
        (int smartPushId, int arg, int recipeId, bool _) = SmartPushRules.Read(packet);
        if (!session.TableMetadata.SmartPushTable.Entries.TryGetValue(smartPushId, out SmartPushMetadata? metadata)) {
            return;
        }

        switch (metadata.Type) {
            case SmartPushType.autoInteraction:
                HandleAutoInteraction(session, arg, recipeId, metadata);
                break;
            case SmartPushType.additionalEffect:
                HandleAdditionalEffect(session, arg, metadata);
                break;
            default:
                Logger.Error("Unhandled smart push type: {SmartPushType}", metadata.Type);
                break;
        }

    }

    private void HandleAutoInteraction(GameSession session, int amount, int recipeId, SmartPushMetadata metadata) {
        if (!SmartPushRules.AutoCountAllowed(amount)) {
            return;
        }

        if (!session.TableMetadata.MasteryRecipeTable.Entries.TryGetValue(recipeId, out MasteryRecipeTable.Entry? recipeMetadata)) {
            return;
        }

        int successCount = 0;
        for (int i = 0; i < amount; i++) {
            if (!TakeRequiredItem(session, metadata)) {
                // Out of items part way: the gathers already done are still reported.
                break;
            }

            float successRate = session.Mastery.GatherSuccessRate(recipeMetadata);
            if (successRate <= 0.0f) {
                break;
            }

            session.Mastery.BeforeGather(recipeMetadata);
            session.Mastery.Gather(recipeMetadata, session.Player.Position, session.Player.Rotation);
            successCount++;
        }

        if (successCount == 0) {
            return;
        }
        session.Send(SmartPushPacket.ActivateGather(metadata.Id, successCount));
    }

    private void HandleAdditionalEffect(GameSession session, int packageId, SmartPushMetadata metadata) {
        if (session.Field is null) return;

        // The price is settled before the required item is taken: a package that does not exist or
        // cannot be paid no longer costs the item.
        bool isPackage = metadata.Content is "AutoFishing" or "AutoPlayInstrument";
        AutoActionMetaData? package = null;
        if (isPackage) {
            package = FindPackage(session, metadata, packageId);
            // A package is bought only without its buff: the client re-buys when the buff has ended
            // (CSmartPushAutoFishing::vfunc_2 checks the buff is gone) and its extend box only arms that re-buy
            // (UISystemShopDialog::FUN_140fa62d0 sends nothing). A buy while held would have set the end to the new
            // package's minutes from now, shortening a longer one; a package without minutes gives nothing.
            if (package == null || package.Duration <= 0 || HoldsEventBuff(session, (int) metadata.Value)) {
                return;
            }
            if (!SmartPushRules.Affords(session.Currency.Meret, package.MeretCost)
                                || (package.MeretCost <= 0 && !SmartPushRules.Affords(session.Currency.Meso, package.MesoCost))) {
                return;
            }
        } else if (!SmartPushRules.Affords(session.Currency.Meret, metadata.MeretCost)) {
            return;
        }

        // The merets are taken before the item, without a packet (another thread can take them too: a wedding hall
        // paid by the partner), and shown where they were taken before; a failed item take gives them back.
        long meretCost = package == null ? metadata.MeretCost : Math.Max(0, package.MeretCost);
        if (meretCost > 0 && !session.Currency.TryTakeMeret(meretCost, notify: false)) {
            return;
        }
        // A package's mesos the same way (the item take sends a packet, and a spend could come before the old take).
        long mesoCost = package != null && package.MeretCost <= 0 ? Math.Max(0, package.MesoCost) : 0;
        if (mesoCost > 0 && !session.Currency.TryTakeMeso(mesoCost, notify: false)) {
            session.Currency.ReturnMeret(meretCost);
            return;
        }
        if (!TakeRequiredItem(session, metadata)) {
            session.Currency.ReturnMeret(meretCost);
            session.Currency.ReturnMeso(mesoCost, notify: false);
            return;
        }

        // A package lasts its own minutes (autoactionpricepackage duration, shown by
        // CSmartPushTable::GetPackageDurationText); the effect's durationTick (3 h for 100000011) only when bought
        // without one. The client sends the package index and sets no time itself.
        int effectId = package != null ? SmartPushRules.PackageEffectId(package.Content, package.Duration, (int) metadata.Value) : (int) metadata.Value;
        int durationMs = package != null ? package.Duration * 60 * 1000 : -1;
        // A buff that is not given (no effect data, an immunity, nothing changed) costs nothing and has no notice:
        // the price comes back. (No KR effect push needs an item, so none is lost to this.)
        if (!session.Buffs.AddBuff(session.Player, session.Player, effectId, 1, session.Field.FieldTick, durationMs: durationMs)) {
            session.Currency.ReturnMeret(meretCost);
            session.Currency.ReturnMeso(mesoCost, notify: false);
            return;
        }

        if (package != null) {
            PayPackage(session, package);
        } else if (meretCost > 0) {
            session.Currency.NotifyMeretTaken(meretCost);
        } else {
            session.Currency.ChangeMeret(-metadata.MeretCost);
        }
        session.Send(SmartPushPacket.ActivateEffect(metadata.Id, package?.Id ?? 0));
    }

    // By the tag when the push names one, else by the item id; a push with neither needs no item.
    private static bool TakeRequiredItem(GameSession session, SmartPushMetadata metadata) {
        if (metadata.RequiredItem.Tag != ItemTag.None) {
            return session.Item.Inventory.Consume([metadata.RequiredItem]);
        }
        if (metadata.RequiredItemId != 0 && metadata.RequiredItem.Amount > 0) {
            return session.Item.Inventory.ConsumeItemComponents([
                new ItemComponent(metadata.RequiredItemId, -1, metadata.RequiredItem.Amount, ItemTag.None),
            ]);
        }
        return true;
    }

    // The client's own rule: a package is bought only without a buff of the push effect's event type (auto fishing
    // type 1, auto play type 4), whichever effect of the family gave it, so an old 100000011 held also blocks.
    private static bool HoldsEventBuff(GameSession session, int pushEffectId) {
        if (!session.Field!.SkillMetadata.TryGetEffect(pushEffectId, 1, out AdditionalEffectMetadata? effect)) {
            return session.Buffs.EnumerateBuffs(pushEffectId).Count > 0;
        }
        return effect.Property.EventType != BuffEventType.None
            ? session.Buffs.HasBuff(effect.Property.EventType)
            : session.Buffs.EnumerateBuffs(pushEffectId).Count > 0;
    }

    private AutoActionMetaData? FindPackage(GameSession session, SmartPushMetadata smartPushMetadata, int packageId) {
        string content = smartPushMetadata.Content;
        switch (content) {
            case "AutoFishing":
                if (session.FindEvent(GameEventType.SaleAutoFishing).FirstOrDefault()?.Metadata.Data is SaleAutoFishing saleAutoFishing) {
                    content = saleAutoFishing.ContentType;
                }
                break;
            case "AutoPlayInstrument":
                if (session.FindEvent(GameEventType.SaleAutoPlayInstrument).FirstOrDefault()?.Metadata.Data is SaleAutoPlayInstrument saleAutoPlayInstrument) {
                    content = saleAutoPlayInstrument.ContentType;
                }
                break;
            default:
                Logger.Error("Unhandled auto action content: {Content}", content);
                return null;
        }


        if (!session.TableMetadata.AutoActionTable.Entries.TryGetValue(content, out IReadOnlyDictionary<int, AutoActionMetaData>? packages) ||
            !packages.TryGetValue(packageId, out AutoActionMetaData? found)) {
            return null;
        }
        return found;
    }

    // The caller has tested the price and taken the merets or the mesos (TryTakeMeret/TryTakeMeso without a packet).
    private static void PayPackage(GameSession session, AutoActionMetaData packageMetadata) {
        SmartPushCurrencyType currencyType;
        if (packageMetadata.MeretCost > 0) {
            currencyType = SmartPushCurrencyType.Meret;
        } else if (packageMetadata.MesoCost > 0) {
            currencyType = SmartPushCurrencyType.Meso;
        } else {
            currencyType = SmartPushCurrencyType.None;
        }

        // Check if player has enough currency
        switch (currencyType) {
            case SmartPushCurrencyType.Meret:
                session.Currency.NotifyMeretTaken(packageMetadata.MeretCost);
                break;
            case SmartPushCurrencyType.Meso:
                session.Currency.NotifyMeso();
                break;
            case SmartPushCurrencyType.None:
                break;
        }
    }
}
