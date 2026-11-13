using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Model.Game;

public class Marriage : IByteSerializable {
    public static Marriage Default => new() {
        Status = MaritalStatus.Single,
        Partner1 = new MarriagePartner(),
        Partner2 = new MarriagePartner(),
    };
    public long Id { get; init; }
    public MaritalStatus Status { get; set; }

    public required MarriagePartner Partner1 { get; set; }
    public required MarriagePartner Partner2 { get; set; }
    public long Exp { get; set; }
    public string Profile { get; set; } = string.Empty;
    public long CreationTime { get; set; }
    // The time the vow was completed in the wedding hall; 0 until then.
    public long MarriageTime { get; set; }
    // The time a divorce was filed (states 3 and 4) or became complete (state 5); 0 otherwise.
    public long DivorceTime { get; set; }

    public IList<MarriageExp> ExpHistory { get; set; } = [];

    // Only an engaged couple can be married by a ceremony.
    public static bool CanMarry(MaritalStatus status) => status == MaritalStatus.Engaged;


    public void WriteTo(IByteWriter writer) {
        writer.WriteLong(Id);
        writer.WriteByte((byte) Status);
        // Client: WeddingProxy::Read (weddingproxy.cpp:227) reads the state time and then the marriage time, the
        // one its divorce check counts from. The state time is what the client counts the cooling off
        // (GetDivorceTime, weddingproxy.cpp:658) and the propose cooltime (:697) from in the states 3 to 5.
        writer.WriteLong(DivorceRules.StateTime(Status, CreationTime, DivorceTime));
        writer.WriteLong(MarriageTime);
        writer.WriteClass<MarriagePartner>(Partner1);
        writer.WriteClass<MarriagePartner>(Partner2);
        writer.WriteLong(Exp);
        writer.WriteUnicodeString(Partner1.Message);
        writer.WriteUnicodeString(Partner2.Message);
        writer.WriteUnicodeString();
        writer.WriteUnicodeString(Profile);
        writer.WriteInt(ExpHistory.Count);
        foreach (MarriageExp exp in ExpHistory) {
            writer.Write<MarriageExpType>(exp.Type);
            writer.WriteLong(exp.Amount);
            writer.WriteLong(exp.Time);
        }
    }
}

public class MarriageExp : IByteSerializable {
    public MarriageExpType Type { get; set; }
    public long Amount { get; set; }
    public long Time { get; set; }
    public void WriteTo(IByteWriter writer) {
        writer.Write<MarriageExpType>(Type);
        writer.WriteLong(Amount);
        writer.WriteLong(Time);
    }
}

public class MarriagePartner : IByteSerializable, IDisposable {
    public PlayerInfo? Info { get; set; }
    public long AccountId => Info?.AccountId ?? 0;
    public long CharacterId => Info?.CharacterId ?? 0;
    public string Name => Info?.Name ?? string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsOnline => Info?.Online ?? false;
    public CancellationTokenSource? TokenSource;
    public void WriteTo(IByteWriter writer) {
        writer.WriteLong(CharacterId);
        writer.WriteLong(AccountId);
        writer.WriteUnicodeString(Name);
        // Client: Unk_WeddingPartner::Read (weddingproxy.cpp:77) reads this as the online flag. The wedding recall
        // scroll (RecallWedding, itemfunctiondispatch.cpp:1383) asks WeddingManager::ApplyPartner
        // (weddingmanager.cpp:441) and refuses to send when the partner's flag is false.
        writer.WriteBool(IsOnline);
    }

    public void Dispose() {
        TokenSource?.Cancel();
        TokenSource?.Dispose();
        TokenSource = null;
    }
}

public class MarriageInfo : IByteSerializable {
    public MaritalStatus Status { get; set; }
    public long CreationTime { get; set; }
    public long MarriageTime { get; set; }
    public string Partner1Name { get; set; } = string.Empty;
    public string Partner2Name { get; set; } = string.Empty;
    public void WriteTo(IByteWriter writer) {
        // Client: CPCData::Read (commonfiles/pcdata.cpp:142): short status, 2 names, long promise time, long marriage time.
        writer.WriteShort((short) Status);
        writer.WriteUnicodeString(Partner1Name);
        writer.WriteUnicodeString(Partner2Name);
        writer.WriteLong(Status == MaritalStatus.Engaged ? CreationTime : 0);
        writer.WriteLong(Status == MaritalStatus.Married ? MarriageTime : 0);
    }
}
