using System.Runtime.InteropServices;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game;

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 12)]
public struct AchievementInfo {
    public int Combat { get; set; }
    public int Adventure { get; set; }
    public int Lifestyle { get; set; }

    public static AchievementInfo operator +(in AchievementInfo a, in AchievementInfo b) {
        return new AchievementInfo {
            Combat = a.Combat + b.Combat,
            Adventure = a.Adventure + b.Adventure,
            Lifestyle = a.Lifestyle + b.Lifestyle,
        };
    }

    public static AchievementInfo operator -(in AchievementInfo a, in AchievementInfo b) {
        return new AchievementInfo {
            Combat = a.Combat - b.Combat,
            Adventure = a.Adventure - b.Adventure,
            Lifestyle = a.Lifestyle - b.Lifestyle,
        };
    }
    public int Total => Combat + Adventure + Lifestyle;

    // The client's trophy totals (CAchieveManager::Tally, achievemanager.cpp:725): a trophy of type 2 adds each of
    // its cleared grades to the total of its category (1 combat, 2 adventure, 3 life); every other trophy adds
    // nothing. A negative count takes grades off, not below 0.
    public void Add(AchievementMetadata metadata, int grades) {
        if (metadata.Type != 2) {
            return;
        }
        switch (metadata.Category) {
            case AchievementCategory.Combat:
                Combat = Math.Max(0, Combat + grades);
                break;
            case AchievementCategory.Adventure:
                Adventure = Math.Max(0, Adventure + grades);
                break;
            case AchievementCategory.Life:
                Lifestyle = Math.Max(0, Lifestyle + grades);
                break;
        }
    }
}

public class Achievement : IByteSerializable {
    public readonly AchievementMetadata Metadata;
    public readonly int Id;

    public bool Completed => Metadata.Grades.Count == Grades.Count;
    public AchievementStatus Status => Completed ? AchievementStatus.Completed : AchievementStatus.InProgress;
    public int CurrentGrade { get; set; }
    public int RewardGrade { get; set; }
    public bool Favorite { get; set; }
    public long Counter { get; set; }
    public AchievementCategory Category { get; init; }

    public IDictionary<int, long> Grades { get; set; } = new Dictionary<int, long>();

    public Achievement(AchievementMetadata metadata) {
        Metadata = metadata;
        Id = metadata.Id;
        Category = metadata.Category;
    }

    public void WriteTo(IByteWriter writer) {
        writer.Write<AchievementStatus>(Status);
        writer.WriteInt(Completed ? 1 : 0);
        writer.WriteInt(CurrentGrade);
        writer.WriteInt(RewardGrade);
        writer.WriteBool(Favorite);
        writer.WriteLong(Counter);
        writer.WriteInt(Grades.Count);

        foreach ((int grade, long timeAcquired) in Grades.OrderBy(grade => grade.Key).ToList()) {
            writer.WriteInt(grade);
            writer.WriteLong(timeAcquired);
        }
    }
}
