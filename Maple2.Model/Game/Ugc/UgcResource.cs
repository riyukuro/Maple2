using Maple2.Model.Enum;

namespace Maple2.Model.Game;

public class UgcResource {
    public long Id { get; init; }
    public string Path { get; set; }
    public UgcType Type { get; init; }
    // The character whose upload made the resource (UgcHandler: CreateUgc with the session's character).
    public long OwnerId { get; init; }
    // The upload the game server opened for the resource: the web server takes its files until then (UTC), once each
    // (UploadedParts), under the folder of TargetId (the design's item, the banner, the guild). Null: no upload open.
    public DateTime? UploadUntil { get; init; }
    public byte UploadedParts { get; init; }
    public long TargetId { get; init; }

    public UgcResource() {
        Path = string.Empty;
    }
}
