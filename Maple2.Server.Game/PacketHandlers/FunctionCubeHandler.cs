using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class FunctionCubeHandler : FieldPacketHandler {

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required FunctionCubeMetadataStorage FunctionCubeMetadataStorage { private get; init; }
    public required ServerTableMetadataStorage ServerTableMetadataStorage { private get; init; }
    private ConstantsTable Constants => ServerTableMetadataStorage.ConstantsTable;
    // ReSharper restore All
    #endregion

    public override RecvOp OpCode => RecvOp.FunctionCube;

    // KMS 2525 client: both sub-types carry an i32 count and then (wstr cube id, u8) per cube.
    // Begin is sent when a gathering or nurturing state starts (StateHomemade::SendFunctionCubeMode4);
    // Use is sent when that state completes (SendFunctionCubeMode5) and when furniture is used
    // (StateUseFurniture, CFunctionCubeObject::vfunc_8, MyPC::SendFunctionCube).
    private enum Command : byte {
        Begin = 4,
        Use = 5,
        Close = 7,
    }

    // The client only lists the cubes within reach of the character.
    private const int MaxCubesPerRequest = 32;

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Begin:
            case Command.Use:
                int count = packet.ReadInt();
                if (count < 0 || count > MaxCubesPerRequest) {
                    return;
                }
                if (command == Command.Begin) {
                    session.CubeStarted.Clear();
                }
                for (int i = 0; i < count; i++) {
                    string interactId = packet.ReadUnicodeString();
                    byte via = packet.ReadByte();
                    if (command == Command.Use) {
                        HandleUseCube(session, interactId, via);
                    } else {
                        HandleBeginCube(session, interactId);
                    }
                }
                break;
            case Command.Close:
                HandleClose(session, packet.ReadUnicodeString());
                break;
        }
    }

    // The dialog opened from the cube closed: the cube the sender holds in use goes back to its default state.
    private static void HandleClose(GameSession session, string interactId) {
        FieldFunctionInteract? fieldInteract = session.Field?.TryGetFieldFunctionInteract(interactId);
        if (fieldInteract is null) {
            return;
        }

        InteractCube cube = fieldInteract.InteractCube;
        if (!FunctionCubeCloseRule.Releases(fieldInteract.Value.ControlType, cube.State, cube.Metadata.DefaultState, cube.InteractingCharacterId, session.CharacterId)) {
            return;
        }

        cube.State = cube.Metadata.DefaultState;
        cube.InteractingCharacterId = 0;
        session.Field?.Broadcast(FunctionCubePacket.UpdateFunctionCube(cube));
        session.Field?.Broadcast(FunctionCubePacket.UseFurniture(session.CharacterId, cube));
    }

    // The client begins on the farming and breeding cubes within the MasteryObjectInteractionDistance
    // constant of the character (statehomemade.cpp:177); the use that follows is taken only for those.
    private void HandleBeginCube(GameSession session, string interactId) {
        FieldFunctionInteract? fieldInteract = session.Field?.TryGetFieldFunctionInteract(interactId);
        if (fieldInteract is null || !InteractRules.NeedsBegin(fieldInteract.Value.ControlType)) {
            return;
        }
        if (!InteractRules.InReach(session.Player.Position, fieldInteract.Position, Constants.MasteryObjectInteractionDistance)) {
            return;
        }
        session.CubeStarted.Add(interactId);
    }

    // The byte after each cube id: 1 from the action key, the furniture state and the gathering states; 2 from a skill
    // hit (functioncubeobject.cpp:2838) on a cube the client marks for it: kind 3 of sub 2 (CFunctionCubeObject::vfunc_2
    // sets m_bLike, functioncubeobject.cpp:541, read by FUN_141236a60 at :489).
    private const byte UseBySkillHit = 2;

    private void HandleUseCube(GameSession session, string interactId, byte via) {
        if (!interactId.Contains('_')) {
            Logger.Error("Unexpected interactId {0}", interactId);
            return;
        }

        FieldFunctionInteract? fieldInteract = session.Field?.TryGetFieldFunctionInteract(interactId);
        if (fieldInteract is null) {
            Logger.Error("Interact cube not found for interactId {0}", interactId);
            return;
        }

        switch (fieldInteract.Value.ControlType) {
            case InteractCubeControlType.Nurturing:
                if (fieldInteract.InteractCube.Nurturing is not null) {
                    HandleNurturing(session, fieldInteract);
                }
                break;
            case InteractCubeControlType.Farming:
            case InteractCubeControlType.Breeding:
                if (!session.CubeStarted.Remove(interactId)) {
                    return;
                }
                FieldFunctionInteract? fieldFunctionInteract = session.Field?.TryGetFieldFunctionInteract(fieldInteract.InteractCube.Id);
                if (fieldFunctionInteract != null && fieldFunctionInteract.Use()) {
                    // The mastery, the drops, the cube's update, the result, then the experience.
                    session.Mastery.Gather(fieldFunctionInteract);
                }
                break;
            default:
                if (fieldInteract.InteractCube.State is InteractCubeState.InUse && fieldInteract.InteractCube.InteractingCharacterId != session.CharacterId) {
                    return;
                }
                bool isInDefaultState = fieldInteract.InteractCube.State == fieldInteract.InteractCube.Metadata.DefaultState;
                // The client takes a cube into use from its action key (CFunctionCubeObject::vfunc_8,
                // functioncubeobject.cpp:615), so only by the character, or from a skill hit within the skill's
                // area; a use from across the map toggled any furniture or switch of the field and counted for the
                // controller condition each time. A release is not checked: a player moved away from their seat
                // (a home shrink's safe move) still gets up from it.
                float reach = via == UseBySkillHit ? InteractRules.SkillHitReach : InteractRules.OtherReach;
                if (isInDefaultState && !InteractRules.InReach(session.Player.Position, fieldInteract.Position, reach)) {
                    return;
                }
                long tick = Environment.TickCount64;
                if (fieldInteract.Value.ControlType == InteractCubeControlType.Switch) {
                    // A switch has no sitting state on the client, so no second request follows.
                } else if (isInDefaultState) {
                    // The client's second request for a seat it just left is not a new sit-down.
                    if (session.SeatRelease.Swallow(interactId, tick)) {
                        return;
                    }
                } else if (fieldInteract.InteractCube.InteractingCharacterId == session.CharacterId) {
                    session.SeatRelease.Released(interactId, tick);
                }
                fieldInteract.InteractCube.State = isInDefaultState ? InteractCubeState.InUse : fieldInteract.InteractCube.Metadata.DefaultState;
                fieldInteract.InteractCube.InteractingCharacterId = isInDefaultState ? session.CharacterId : 0;
                session.Field?.Broadcast(FunctionCubePacket.UpdateFunctionCube(fieldInteract.InteractCube));
                session.Field?.Broadcast(FunctionCubePacket.UseFurniture(session.CharacterId, fieldInteract.InteractCube));
                if (isInDefaultState) {
                    // Taking furniture into use counts for controller, with the furniture's item id as the code.
                    session.ConditionUpdate(ConditionType.controller, codeLong: fieldInteract.ItemId);
                }
                break;
        }
    }

    private void HandleNurturing(GameSession session, FieldFunctionInteract fieldCube) {
        if (session.Field is null) return;

        if (fieldCube.Value.Nurturing is null) {
            return;
        }

        // The client uses a nest only when its quest tag is the NurturingQuestTag constant and that term tag runs
        // (functioncubeobject.cpp:1934-1937), and only within MasteryObjectInteractionDistance of the character
        // (statenurturing.cpp:200-201); a use of any other nest is not taken.
        string tag = fieldCube.Value.Nurturing.QuestTag;
        if (tag != Constants.NurturingQuestTag ||
            session.TermTags.Find(session.ServerTableMetadata.TermTagTable, tag) is not { } term ||
            !TermTagState.Running(term, DateTimeOffset.UtcNow.ToUnixTimeSeconds())) {
            return;
        }
        if (!InteractRules.InReach(session.Player.Position, fieldCube.Position, Constants.MasteryObjectInteractionDistance)) {
            return;
        }

        Plot? plot = session.Housing.GetFieldPlot();
        if (plot is null) {
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        Nurturing? dbNurturing = db.GetNurturing(plot.OwnerId, fieldCube.Value.Id, fieldCube.Value.Nurturing);
        if (dbNurturing is null) {
            Logger.Error("Nurturing not found for account id {0} and interact id {1}", session.AccountId, fieldCube.Value.Id);
            return;
        }

        if (dbNurturing.ClaimedGiftForStage > dbNurturing.NurturingMetadata.RequiredGrowth.Length) {
            // fully grown
            return;
        }

        // always update object reference
        fieldCube.InteractCube.Nurturing = dbNurturing;
        Nurturing? nurturing = fieldCube.InteractCube.Nurturing;

        if (session.AccountId != plot.OwnerId) {
            HandlePlayNurturing(session, plot, fieldCube, nurturing, db);
            return;
        }

        FunctionCubeMetadata.NurturingData.Growth requiredGrowth = nurturing.NurturingMetadata.RequiredGrowth.First(x => x.Stage == nurturing.ClaimedGiftForStage);
        // Claim reward
        if (nurturing.Exp >= requiredGrowth.Exp) {
            // The gift is made first: a gift that cannot be made leaves the stage unclaimed.
            Item? rewardStageItem = session.Field.ItemDrop.CreateItem(requiredGrowth.Reward.ItemId, rarity: requiredGrowth.Reward.Rarity, amount: requiredGrowth.Reward.Amount);
            if (rewardStageItem is null) {
                Logger.Error("Failed to create the reward item for account {0} and item {1}", session.AccountId, fieldCube.InteractCube.Metadata.Id);
                return;
            }
            nurturing.ClaimedGiftForStage++;
            // drop the item
            session.Field.DropItem(fieldCube.Position, fieldCube.Rotation, rewardStageItem, owner: session.Player, characterId: session.CharacterId);
            db.UpdateNurturing(session.AccountId, fieldCube.InteractCube);

            session.Field.Broadcast(FunctionCubePacket.UpdateFunctionCube(fieldCube.InteractCube));
            // Nothing was fed: mode 12, the pet acts and no fed notice is shown. The client does not show when
            // the server sends 12; taking a stage gift is the one pet action here that is not a feeding or a play.
            SendDailyRecord(session, db, fieldCube, session.AccountId, notice: false);
            return;
        }

        // Client: CNurturingCubeObject (functioncubeobject.cpp:1957-1968): with NurturingEatMaxCount (m_eat,
        // clientconstants.h:562) not 0 the feedings of the day, of all own pets, are compared with it.
        if (Constants.NurturingEatMaxCount != 0 && db.CountNurturingDaily(session.AccountId, played: false) >= Constants.NurturingEatMaxCount) {
            session.Send(FunctionCubePacket.NurturingError(session.CharacterId, fieldCube.InteractCube, 2));
            return;
        }

        RewardItem feedItem = nurturing.NurturingMetadata.Feed;
        Item? hasItem = session.Item.Inventory.Find(feedItem.ItemId, feedItem.Rarity).FirstOrDefault();
        if (hasItem is null) {
            session.Send(FunctionCubePacket.NurturingError(session.CharacterId, fieldCube.InteractCube, 1));
            return;
        }

        // A pet without rewardItemByFeeding (item 0, e.g. 50800002) is fed without a drop. The drop is made before
        // the feed is taken: a drop that cannot be made keeps the feed and the pet unfed.
        RewardItem rewardFeedItem = nurturing.NurturingMetadata.RewardFeed;
        Item? rewardItem = null;
        if (rewardFeedItem.ItemId != 0) {
            rewardItem = session.Field.ItemDrop.CreateItem(rewardFeedItem.ItemId, rarity: rewardFeedItem.Rarity, amount: rewardFeedItem.Amount);
            if (rewardItem is null) {
                Logger.Error("Failed to create the reward item for account {0} and item {1}", session.AccountId, fieldCube.InteractCube.Metadata.Id);
                return;
            }
        }
        if (!session.Item.Inventory.Consume(hasItem.Uid, amount: 1)) {
            return;
        }
        if (rewardItem is not null) {
            session.Field.DropItem(fieldCube.Position, fieldCube.Rotation, rewardItem, owner: session.Player, characterId: session.CharacterId);
        }
        long exp = nurturing.Exp;
        nurturing.Feed(Constants.NurturingEatGrowth);
        db.RecordNurturing(session.AccountId, session.AccountId, fieldCube.InteractCube, played: false);
        // 92000118 "Feed ... in your home" (nurturing_eat) and 92000096 "Contribute 100 growth points ... through
        // feeding or play" (nurturing_growth, the points the pet gained) count the owner's feeding.
        int trophyCode = nurturing.NurturingMetadata.TrophyCode(fieldCube.ItemId);
        session.ConditionUpdate(ConditionType.nurturing_eat, codeLong: trophyCode);
        if (nurturing.Exp > exp) {
            session.ConditionUpdate(ConditionType.nurturing_growth, counter: nurturing.Exp - exp, codeLong: trophyCode);
        }

        session.Field.Broadcast(FunctionCubePacket.UpdateFunctionCube(fieldCube.InteractCube));
        SendDailyRecord(session, db, fieldCube, session.AccountId);
    }

    private void HandlePlayNurturing(GameSession session, Plot plot, FieldFunctionInteract cube, Nurturing nurturing, GameStorage.Request db) {
        if (session.Field is null) return;

        if (nurturing.PlayedBy.Contains(session.AccountId)) {
            session.Send(FunctionCubePacket.NurturingError(session.CharacterId, cube.InteractCube, 4));
            return;
        }

        // Client: CNurturingCubeObject (functioncubeobject.cpp:1971-1981): a visitor who played with this cube
        // gets error 4; otherwise the plays of the day, of all pets, are compared with NurturingPlayMaxCount
        // (m_play, clientconstants.h:563) and error 3 is shown at the maximum.
        if (db.CountNurturingDaily(session.AccountId, played: true) >= Constants.NurturingPlayMaxCount) {
            session.Send(FunctionCubePacket.NurturingError(session.CharacterId, cube.InteractCube, 3));
            return;
        }

        // The drop (the pet's feed item) is made before the play counts: a drop that cannot be made leaves the pet
        // unplayed, so the play conditions are not counted for a play that is not recorded.
        RewardItem rewardPlay = nurturing.NurturingMetadata.Feed;
        Item? rewardItem = null;
        if (rewardPlay.ItemId != 0) {
            rewardItem = session.Field.ItemDrop.CreateItem(rewardPlay.ItemId, rarity: rewardPlay.Rarity, amount: rewardPlay.Amount);
            if (rewardItem is null) {
                Logger.Error("Failed to create the reward item for account {0} and item {1}", session.AccountId, cube.Value.Id);
                return;
            }
        }

        long exp = nurturing.Exp;
        if (!nurturing.Play(session.AccountId, Constants.NurturingPlayGrowth, Constants.NurturingPlayMaxGrowth)) {
            return;
        }
        // 92000095 "Play with ... in someone's home" (nurturing_play) and nurturing_growth count the visitor's play.
        int trophyCode = nurturing.NurturingMetadata.TrophyCode(cube.ItemId);
        session.ConditionUpdate(ConditionType.nurturing_play, codeLong: trophyCode);
        if (nurturing.Exp > exp) {
            session.ConditionUpdate(ConditionType.nurturing_growth, counter: nurturing.Exp - exp, codeLong: trophyCode);
        }

        if (rewardItem is not null) {
            session.Field.DropItem(cube.Position, cube.Rotation, rewardItem, owner: session.Player, characterId: session.CharacterId);
        }

        db.RecordNurturing(session.AccountId, plot.OwnerId, cube.InteractCube, played: true);

        session.Field.Broadcast(FunctionCubePacket.UpdateFunctionCube(cube.InteractCube));
        SendDailyRecord(session, db, cube, plot.OwnerId);

        // The owner's mail carries the feeding gift (rewardItemByFeeding); a pet without one sends no mail.
        if (nurturing.NurturingMetadata.RewardFeed.ItemId == 0) {
            return;
        }
        Mail? mail = CreateOwnerMail(session, plot.OwnerId, nurturing.NurturingMetadata);

        if (mail == null) {
            Logger.Error("Failed to create mail for account {0} and item {1}", session.AccountId, cube.Value.Id);
            return;
        }

        try {
            session.World.MailNotification(new MailNotificationRequest {
                AccountId = plot.OwnerId,
                MailId = mail.Id,
            });
        } catch { /* ignored */
        }
    }

    // The client replaces its whole daily record with each reply (MyPC::ReadDailyRecords, mypc.cpp:4060-4070,
    // empties both lists first), so both lists are sent whole: one entry per feeding and one per play of today.
    private static void SendDailyRecord(GameSession session, GameStorage.Request db, FieldFunctionInteract cube, long ownerId, bool notice = true) {
        int itemId = cube.InteractCube.Metadata.Id;
        List<long> fed = NurturingRecord.CubeIds(db.GetNurturingDaily(session.AccountId, played: false), ownerId, itemId, cube.CubeId);
        List<long> played = NurturingRecord.CubeIds(db.GetNurturingDaily(session.AccountId, played: true), ownerId, itemId, cube.CubeId);
        session.Send(FunctionCubePacket.Feed(session.CharacterId, cube.InteractCube, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), fed, played, notice));
    }

    private Mail? CreateOwnerMail(GameSession session, long ownerId, FunctionCubeMetadata.NurturingData nurturing) {
        using GameStorage.Request db = session.GameStorage.Context();

        string? contentId = Nurturing.OwnerMailContent(nurturing.QuestTag);
        if (contentId == null) {
            Logger.Warning("Unknown event tag {0} for nurturing mail", nurturing.QuestTag);
            return null;
        }

        // The gift is made before the mail: a gift that cannot be made sends no empty mail.
        RewardItem rewardPlay = nurturing.RewardFeed;
        Item? rewardItem = session.Field?.ItemDrop.CreateItem(rewardPlay.ItemId, rarity: rewardPlay.Rarity, amount: rewardPlay.Amount);
        if (rewardItem is null) {
            Logger.Error("Failed to create the reward item for account {0} and item {1}", session.AccountId, rewardPlay.ItemId);
            return null;
        }

        var mail = new Mail(Constants.MailExpiryDays) {
            ReceiverId = ownerId,
            Type = MailType.System,
            Content = contentId,
            SenderName = session.PlayerName,
        };

        mail = db.CreateMail(mail);
        if (mail is null) {
            Logger.Error("Failed to create mail for account {0}", session.AccountId);
            return null;
        }

        Item? item = db.CreateItem(mail.Id, rewardItem);
        if (item is null) {
            Logger.Error("Failed to create item for mail {0} and item {1}", mail.Id, rewardPlay.ItemId);
            return null;
        }

        mail.Items.Add(item);
        return mail;
    }
}
