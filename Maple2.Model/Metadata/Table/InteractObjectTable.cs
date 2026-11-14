using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

public record InteractObjectTable(IReadOnlyDictionary<int, InteractObjectMetadata> Entries) : Table;

public record InteractObjectMetadata(
    int Id,
    InteractType Type,
    int Collection,
    int ReactCount,
    int TargetPortalId,
    int GuildPosterId,
    int WeaponItemId,
    InteractObjectMetadataItem Item,
    InteractObjectMetadataTime Time,
    InteractObjectMetadataDrop Drop,
    InteractObjectMetadataEffect AdditionalEffect,
    InteractObjectMetadataSpawn[] Spawn,
    InteractObjectMetadataUi? Ui = null,
    // The row's <validate interactionDistance>: how near the client must be to start or keep using the object
    // (stateinteractdata.cpp:382, worldservice_1.cpp:4567). 0 when not ingested.
    float InteractionDistance = 0,
    // The row's <webOpen type url>: the page the server opens on the client when the object is used (WebOpen 0x9b);
    // null when the row has no url.
    InteractObjectMetadataWebOpen? WebOpen = null);

// The page of an object's <webOpen>: the WebOpen kind (0 the system browser, 1 the maview browser, 2 the adventure
// bar; CSceneStateGame::OnWebOpen) and the url, e.g. 1 "maview:/Rank/Guild" for the guild ranking boards in town.
public record InteractObjectMetadataWebOpen(byte Type, string Url);

// The page a Ui object opens (<ui swf value>): the swf's type name and its parameter, e.g. CardReverseGameDialog with
// the BonusGame id of its deck.
public record InteractObjectMetadataUi(string Swf, string Value);

public record InteractObjectMetadataItem(int Id, int Amount, int Rarity, int CheckAmount, int RecipeId);

public record InteractObjectMetadataTime(int Reset, int React, int Hide);

public record InteractObjectMetadataDrop(int Rarity, int[] GlobalDropBoxIds, int[] IndividualDropBoxIds, float DropHeight, float DropDistance);

public record InteractObjectMetadataEffect(
    InteractObjectMetadataEffect.ConditionEffect[] Condition,
    InteractObjectMetadataEffect.InvokeEffect[] Invoke,
    int ModifyCode,
    int ModifyTime
) {
    public record ConditionEffect(int Id, short Level);
    public record InvokeEffect(int Id, short Level, int Probability);
}

public record InteractObjectMetadataSpawn(int Id, int Radius, int Count, int Probability, int LifeTime);
