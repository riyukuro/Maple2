using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game.Shop;

public class RestrictedBuyData : IByteSerializable {
    public long StartTime { get; init; }
    public long EndTime { get; init; }
    public IList<BuyTimeOfDay> TimeRanges { get; init; }
    public IList<ShopBuyDay> Days { get; init; }

    public RestrictedBuyData() {
        TimeRanges = new List<BuyTimeOfDay>();
        Days = new List<ShopBuyDay>();
    }

    public RestrictedBuyData Clone() {
        return new RestrictedBuyData() {
            StartTime = StartTime,
            EndTime = EndTime,
            TimeRanges = TimeRanges.Select(time => time.Clone()).ToList(),
            Days = Days.Select(day => day).ToList(),
        };
    }

    // The client's buy-period test (CNpcShopBuyPeriod 0x1406ddc90, with the window it builds in 0x1406dcf10):
    // - without a schedule the item is refused only outside the dates, and only when an end date is set;
    // - with a schedule the dates must hold (a start date alone counts too), today must be one of the days
    //   unless none or all seven are given, and the time of day must fall in the first range that has not
    //   ended yet, from its start (inclusive) to its end (exclusive);
    // - all seven days without a time range give the client an empty window, so the item stays closed.
    public bool CanBuy(DateTime now) {
        long time = new DateTimeOffset(now).ToUnixTimeSeconds();
        bool outsideDates = EndTime > 0 && (time < StartTime || time >= EndTime);
        if (TimeRanges.Count == 0 && Days.Count == 0) {
            return !outsideDates;
        }
        if (outsideDates || (StartTime > 0 && time < StartTime)) {
            return false;
        }
        // The client keeps the days in a set, so a repeated day counts once.
        int dayCount = Days.Distinct().Count();
        if (dayCount is > 0 and < 7 && !Days.Contains((ShopBuyDay) ((int) now.DayOfWeek + 1))) {
            return false;
        }
        if (TimeRanges.Count == 0) {
            return dayCount < 7;
        }

        int seconds = (int) now.TimeOfDay.TotalSeconds;
        foreach (BuyTimeOfDay range in TimeRanges) {
            if (seconds < range.EndTimeOfDay) {
                return seconds >= range.StartTimeOfDay;
            }
        }
        return false;
    }

    // Same layout as the client's own buy-period writer (0x1406dc850) and reader (0x1406dcb40):
    // the dates follow only their flag, and the time ranges and days follow a second flag.
    public void WriteTo(IByteWriter writer) {
        bool hasDates = StartTime > 0 || EndTime > 0;
        writer.WriteBool(hasDates);
        if (hasDates) {
            writer.WriteLong(StartTime);
            writer.WriteLong(EndTime);
        }

        bool hasSchedule = TimeRanges.Count > 0 || Days.Count > 0;
        writer.WriteBool(hasSchedule);
        if (!hasSchedule) {
            return;
        }

        writer.WriteByte((byte) TimeRanges.Count);
        foreach (BuyTimeOfDay time in TimeRanges) {
            writer.WriteInt(time.StartTimeOfDay);
            writer.WriteInt(time.EndTimeOfDay);
        }

        writer.WriteByte((byte) Days.Count);
        foreach (ShopBuyDay day in Days) {
            writer.WriteByte((byte) day);
        }
    }
}

[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct BuyTimeOfDay {
    public int StartTimeOfDay { get; } // time begin in seconds. ex 1200 = 12:20 AM
    public int EndTimeOfDay { get; } // time end in seconds. ex 10600 = 2:56 AM

    [JsonConstructor]
    public BuyTimeOfDay(int startTime, int endTime) {
        StartTimeOfDay = startTime;
        EndTimeOfDay = endTime;
    }

    public BuyTimeOfDay Clone() {
        return new BuyTimeOfDay(StartTimeOfDay, EndTimeOfDay);
    }
}
