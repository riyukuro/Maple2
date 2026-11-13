using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

// Table/userConditionEvent.xml as CUserConditionEventData::Load reads it (commonfiles/userconditioneventdata.cpp),
// by row id. Group is the row's gameEventID: the value1 of the UserCondition game event the row runs under.
public record UserConditionEventTable(IReadOnlyDictionary<int, UserConditionEventMetadata> Entries) : Table {
    public static readonly UserConditionEventTable Empty = new(new Dictionary<int, UserConditionEventMetadata>());
}

// <condition type code target/>; code and target are 0 when empty (a level row has no code, a job row no target).
// <notify> is the notice dialog's row (null when useNotify is 0 or the node is missing): Preview the row the dialog's
// button shows after this one, DescKey and DescArg its text. <mail> is the system mail (null when useMail is 0):
// Context its systemmailcontent id.
public record UserConditionEventMetadata(
    int Id,
    int Group,
    ConditionType Type,
    long Code,
    long Target,
    UserConditionEventMetadata.NotifyData? Notify,
    UserConditionEventMetadata.MailData? Mail) {

    public record NotifyData(int Preview, string DescKey, string DescArg, Attachment[] Attachments);

    public record MailData(int Context, long Meso, long Merat, Attachment[] Attachments);

    // Job 0 is every job; else the attachment is only for that job group (the job's code, 1 for a beginner).
    public record Attachment(int Job, int ItemId, int Rarity, int Amount);
}
