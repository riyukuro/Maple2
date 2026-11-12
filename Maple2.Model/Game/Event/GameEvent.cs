using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game.Event;

public class GameEvent : IByteSerializable {
    public GameEventMetadata Metadata { get; init; }
    public int Id => Metadata.Id;
    // The table's type is "reactor"; the client's class is GameEventReactor.
    public string Name => Metadata.Type == GameEventType.reactor ? "Reactor" : Metadata.Type.ToString();
    public long StartTime => (long) (Metadata.StartTime.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds;
    public long EndTime => (long) (Metadata.EndTime.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds;

    public GameEvent(GameEventMetadata metadata) {
        Metadata = metadata;
    }

    // The client builds each event by class name (GameEventManagerCli::Create looks up "GameEvent{name}Cli", then
    // "GameEvent{name}") and drops the rest of the list when the name is unknown. These types have no client class.
    public bool SentToClient => Metadata.Data is not (NewUser or ReturnUserCandidate or ActiveUser
        or MapleSurvivalOpenPeriod or ShutdownMapleSurvival);

    public bool IsActive() {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (Metadata.StartTime > now) {
            return false;
        }

        if (Metadata.EndTime < now) {
            return false;
        }

        if (Metadata.ActiveDays.Length > 0 && !Metadata.ActiveDays.Contains(now.DayOfWeek)) {
            return false;
        }

        if (Metadata.StartPartTime != TimeSpan.Zero && Metadata.StartPartTime > now.TimeOfDay) {
            return false;
        }

        if (Metadata.EndPartTime != TimeSpan.Zero && Metadata.EndPartTime < now.TimeOfDay) {
            return false;
        }

        return true;
    }

    public void WriteTo(IByteWriter writer) {
        writer.WriteUnicodeString(Name);
        switch (Metadata.Data) {
            case StringBoard stringBoard:
                writer.WriteInt(Id);
                writer.WriteInt(stringBoard.StringId);
                writer.WriteUnicodeString(stringBoard.Text);
                break;
            case StringBoardLink stringBoardLink:
                writer.WriteInt(Id);
                writer.WriteUnicodeString(stringBoardLink.Link);
                break;
            case SaleChat saleChat:
                writer.WriteInt(Id);
                writer.WriteInt(saleChat.WorldChatDiscount);
                writer.WriteInt(saleChat.ChannelChatDiscount);
                break;
            case EventFieldPopup eventFieldPopup:
                writer.WriteInt(Id);
                writer.WriteInt(eventFieldPopup.MapId);
                break;
            case TrafficOptimizer trafficOptimizer:
                writer.WriteInt(Id);
                writer.WriteInt(trafficOptimizer.GuideObjectSyncInterval);
                writer.WriteInt(trafficOptimizer.RideSyncInterval);
                writer.WriteInt(100);
                writer.WriteInt();
                writer.WriteInt(trafficOptimizer.LinearMovementInterval);
                writer.WriteInt(trafficOptimizer.UserSyncInterval);
                writer.WriteInt(100);
                break;
            case BlueMarble blueMarble:
                writer.WriteInt(Id);
                writer.WriteInt(blueMarble.Rounds.Length);
                foreach (BlueMarble.Round round in blueMarble.Rounds) {
                    writer.WriteInt(round.RoundCount);
                    writer.WriteInt(round.Item.ItemId);
                    writer.WriteByte((byte) round.Item.Rarity);
                    writer.WriteInt(round.Item.Amount);
                }
                break;
            case AttendGift attendGift:
                writer.WriteInt(Id);
                writer.WriteLong(StartTime);
                writer.WriteLong(EndTime);
                writer.WriteUnicodeString(attendGift.Name);
                writer.WriteString(attendGift.Link);
                writer.WriteByte();
                writer.WriteBool(true); // disable claim button
                writer.WriteInt(attendGift.RequiredPlaySeconds);
                writer.WriteByte(AttendanceRules.RequireKind(attendGift.Requirement));
                writer.WriteInt(AttendanceRules.RequireValue(attendGift.Requirement));

                // Pay type, then (when set) the paid-day limit, the price and the coupon item id
                // (client gameeventcontext.cpp:435-441).
                AttendGiftCurrencyType currencyType = AttendanceRules.PayType(attendGift.Pay);
                writer.WriteByte((byte) currencyType);
                if (currencyType != AttendGiftCurrencyType.None) {
                    writer.WriteInt(attendGift.Pay!.MaxCount);
                    writer.WriteLong(attendGift.Pay.Price);
                    writer.WriteInt(attendGift.Pay.CouponItemId);
                }

                writer.WriteInt(attendGift.Items.Length);
                foreach (RewardItem item in attendGift.Items) {
                    writer.Write<RewardItem>(item);
                }
                break;
            case MeretMarketNotice notice:
                writer.WriteUnicodeString(notice.Text);
                break;
            case Rps rps:
                writer.WriteInt(Id);
                writer.WriteUnicodeString(rps.ActionsHtml);
                writer.WriteInt(rps.Rewards.Length);
                foreach (Rps.RewardData reward in rps.Rewards) {
                    writer.WriteInt(reward.PlayCount);
                    writer.WriteInt(reward.Rewards.Length); // RpsContext::Read (gameeventcontext.cpp) reads a count per reward
                    foreach (RewardItem item in reward.Rewards) {
                        writer.Write<RewardItem>(item);
                    }
                }
                writer.WriteInt(rps.GameTicketId);
                writer.WriteInt(Id);
                writer.WriteLong(EndTime);
                break;
            case CoupleDance coupleDance:
                // GameEvent::vfunc_4 reads the id, then CoupleDanceContext::Read (gameeventcontext.cpp) the context.
                writer.WriteInt(Id);
                writer.WriteInt(Id);
                writer.WriteLong(StartTime);
                writer.WriteLong(EndTime);
                writer.WriteInt(coupleDance.DailyRewards.Length);
                foreach (RewardItem[] items in coupleDance.DailyRewards) {
                    WriteItemEntities(writer, items);
                }
                writer.WriteShort((short) coupleDance.GoodEndCount);
                WriteItemEntities(writer, coupleDance.GoodEndRewards);
                writer.WriteByte((byte) coupleDance.MaxRound);
                writer.WriteInt(coupleDance.Dances.Length);
                foreach (string dance in coupleDance.Dances) {
                    writer.WriteUnicodeString(dance);
                }
                writer.WriteInt(coupleDance.Steps.Length);
                foreach (CoupleDance.Step step in coupleDance.Steps) {
                    writer.WriteUnicodeString(step.Action);
                    writer.WriteUnicodeString(step.Message);
                }
                writer.WriteInt(coupleDance.FieldId);
                break;
            case LobbyMap lobbyMap:
                writer.WriteInt(Id);
                writer.WriteInt(lobbyMap.MapId);
                break;
            case ReturnUser returnUser:
                writer.WriteInt(Id);
                writer.WriteInt(returnUser.SeasonId); // season?
                writer.WriteLong(StartTime);
                writer.WriteLong(EndTime);
                writer.WriteInt(returnUser.QuestIds.Length);
                foreach (int questId in returnUser.QuestIds) {
                    writer.WriteInt(questId);
                }
                break;
            case LoginNotice:
                break;
            case FieldEffect fieldEffect:
                writer.WriteInt(Id);
                writer.WriteByte((byte) fieldEffect.MapIds.Length);
                foreach (int mapId in fieldEffect.MapIds) {
                    writer.WriteInt(mapId);
                }
                break;
            case Reactor reactor:
                // GameEvent::vfunc_4 reads the id, then ReactorContext::Read the same id, the count, the end
                // time and the fields.
                writer.WriteInt(Id);
                writer.WriteInt(Id);
                writer.WriteInt(reactor.DailyCount);
                writer.WriteLong(EndTime);
                writer.WriteInt(reactor.MapIds.Length);
                foreach (int mapId in reactor.MapIds) {
                    writer.WriteInt(mapId);
                }
                break;
            case WorldmapIcon worldmapIcon:
                writer.WriteInt(Id);
                writer.WriteUnicodeString(worldmapIcon.Xml);
                break;
            case DungeonExtraReward extraReward:
                // GameEventDungeonExtraReward::vfunc_4: the id, then its map by room id; each value is the room, the
                // event id and the count.
                writer.WriteInt(Id);
                writer.WriteInt(extraReward.Entries.Length);
                foreach (DungeonExtraReward.Entry entry in extraReward.Entries) {
                    writer.WriteInt(entry.DungeonId);
                    writer.WriteInt(entry.DungeonId);
                    writer.WriteInt(Id);
                    writer.WriteInt(entry.Count);
                }
                break;
            case QuestTag questTag:
                writer.WriteInt(Id);
                writer.WriteUnicodeString(questTag.Tag);
                writer.WriteLong(StartTime);
                writer.WriteLong(EndTime);
                break;
            case DTReward dtReward:
                writer.WriteInt(Id);
                writer.WriteUnicodeString(Metadata.Value1);
                writer.WriteUnicodeString(Metadata.Value2);
                writer.WriteUnicodeString(Metadata.Value3);
                writer.WriteUnicodeString(Metadata.Value4);
                break;
            case ConstructShowItem constructShowItem:
                writer.WriteInt(Id);
                writer.WriteInt(constructShowItem.CategoryId);
                writer.WriteUnicodeString(constructShowItem.CategoryName);
                writer.WriteShort(); // likely value3 but unknown what it means
                writer.WriteShort((short) constructShowItem.ItemIds.Length);
                foreach (int itemId in constructShowItem.ItemIds) {
                    writer.WriteInt(itemId);
                }
                break;
            case MassiveConstructionEvent massiveConstructionEvent:
                writer.WriteInt(Id);
                writer.WriteInt(massiveConstructionEvent.MapIds.Length);
                foreach (int mapId in massiveConstructionEvent.MapIds) {
                    writer.WriteInt(mapId);
                }
                writer.WriteInt(); // this is a certain state. 1 = active, 0 = inactive ?
                writer.WriteInt(); // account ids enabled to build in the specified maps
                /* foreach (int accountId in accountIds) {
                    writer.WriteLong(accountId);
                } */
                break;
            case UGCMapContractSale ugcMapContractSale:
                writer.WriteInt(Id);
                writer.WriteInt(ugcMapContractSale.DiscountAmount);
                break;
            case UGCMapExtensionSale ugcMapExtensionSale:
                writer.WriteInt(Id);
                writer.WriteInt(ugcMapExtensionSale.DiscountAmount);
                break;
            case Gallery gallery:
                writer.WriteInt(Id);
                writer.WriteShort((short) gallery.QuestIds.Length);
                foreach (int questId in gallery.QuestIds) {
                    writer.WriteInt(questId);
                }
                writer.WriteShort((short) gallery.RewardItems.Length);
                foreach (RewardItem rewardItem in gallery.RewardItems) {
                    writer.Write<RewardItem>(rewardItem);
                }
                writer.WriteInt(gallery.RevealDayLimit);
                writer.WriteUnicodeString(gallery.Image);
                writer.WriteLong(EndTime);
                break;
            case BingoEvent bingo:
                writer.WriteInt(Id);
                writer.WriteInt(bingo.Rewards.Length);
                foreach (BingoEvent.BingoReward bingoReward in bingo.Rewards) {
                    writer.WriteInt(bingoReward.Items.Length);
                    foreach (RewardItem rewardItem in bingoReward.Items) {
                        writer.Write<RewardItem>(rewardItem);
                    }
                }
                writer.WriteInt(bingo.PencilItemId);
                writer.WriteInt(bingo.PencilPlusItemId);
                break;
            case TimeRunEvent timeRun:
                writer.WriteInt(Id);
                writer.WriteInt(timeRun.StartItemId); // Guess

                writer.WriteInt(timeRun.Quests.Sum(q => q.Distance));
                writer.WriteInt(timeRun.Quests.Length);
                foreach (TimeRunEvent.Quest quest in timeRun.Quests) {
                    writer.WriteInt(quest.Id);
                    writer.WriteInt(quest.Distance);
                    writer.WriteShort((short) quest.OpeningDay);
                }

                writer.Write<RewardItem>(timeRun.FinalReward);
                writer.WriteInt(timeRun.StepRewards.Count);
                foreach ((int steps, RewardItem rewardItem) in timeRun.StepRewards) {
                    writer.WriteInt(steps);
                    writer.Write<RewardItem>(rewardItem);
                }
                break;
            case StampEvent stampEvent:
                // StampEventContext::Read (gameeventcontext.cpp): the tag, the end time, the stamp count, the key,
                // a flag (true: the dialog reads the account's user values), the stamps, the reward rows and the
                // complete reward.
                writer.WriteInt(Id);
                writer.WriteUnicodeString(stampEvent.EventTag);
                writer.WriteLong(EndTime);
                writer.WriteInt(stampEvent.Stamps.Length);
                writer.WriteInt(stampEvent.Key);
                writer.WriteBool(true);
                foreach (StampEvent.Stamp stamp in stampEvent.Stamps) {
                    writer.WriteInt(stamp.QuestId);
                    writer.WriteBool(stamp.Special);
                    writer.WriteInt(stamp.X);
                    writer.WriteInt(stamp.Y);
                }
                writer.WriteInt(stampEvent.Rewards.Length);
                foreach (StampEvent.Reward reward in stampEvent.Rewards) {
                    writer.WriteInt(reward.Count);
                    writer.WriteBool(reward.Special);
                    WriteItemEntity(writer, reward.Item01);
                    WriteItemEntity(writer, reward.Item02);
                }
                WriteItemEntity(writer, stampEvent.CompleteReward);
                break;
            case LongTermAttendGift longTermAttend:
                // LongTermAttendGiftContext::Read (gameeventcontext.cpp): the tag, the end time, an int and a flag
                // (0 and true in KMS2), the reward rows, then begin, end, title, merat and maxattendance.
                writer.WriteInt(Id);
                writer.WriteUnicodeString(longTermAttend.EventTag);
                writer.WriteLong(EndTime);
                writer.WriteInt();
                writer.WriteBool(true);
                writer.WriteInt(longTermAttend.Rewards.Length);
                foreach (LongTermAttendGift.Reward reward in longTermAttend.Rewards) {
                    writer.WriteInt(reward.Day);
                    writer.WriteByte();
                    WriteItemEntity(writer, reward.Item01);
                    WriteItemEntity(writer, reward.Item02);
                }
                writer.WriteLong(StartTime);
                writer.WriteLong(EndTime);
                writer.WriteUnicodeString(longTermAttend.Title);
                writer.WriteInt(longTermAttend.Merat);
                writer.WriteInt(longTermAttend.MaxAttendance);
                break;
            case SaleAutoFishing fishing:
                writer.WriteInt(Id);
                writer.WriteInt(fishing.Discount);
                writer.WriteUnicodeString(fishing.ContentType);
                break;
            case LevelUpPackage levelUpPackage:
                writer.WriteInt(Id);
                writer.WriteInt(levelUpPackage.ItemId);
                writer.WriteUnicodeString(levelUpPackage.QuestIds);
                break;
            case DungeonEnterTimeLimit enterTimeLimit:
                // A count, then EventTimeLimitDungeonContext per range: the event's id again, the room and its
                // start and end as seconds of the day.
                writer.WriteInt(Id);
                writer.WriteInt(enterTimeLimit.Times.Length);
                foreach (DungeonEnterTime time in enterTimeLimit.Times) {
                    writer.WriteInt(Id);
                    writer.WriteInt(time.DungeonId);
                    writer.WriteInt(time.StartSeconds);
                    writer.WriteInt(time.EndSeconds);
                }
                break;
            case SaleAutoPlayInstrument instrument:
                writer.WriteInt(Id);
                writer.WriteInt();
                writer.WriteUnicodeString(instrument.ContentType);
                break;
        }
    }

    // A count, then ItemEntity::Read (commonstruct_wa.cpp) per item: id, rank, count and four flags.
    private static void WriteItemEntities(IByteWriter writer, RewardItem[] items) {
        writer.WriteInt(items.Length);
        foreach (RewardItem item in items) {
            WriteItemEntity(writer, item);
        }
    }

    private static void WriteItemEntity(IByteWriter writer, RewardItem item) {
        writer.WriteInt(item.ItemId);
        writer.WriteShort(item.Rarity);
        writer.WriteInt(item.Amount);
        writer.WriteBool(false);
        writer.WriteBool(false);
        writer.WriteBool(false);
        writer.WriteBool(false);
    }
}
