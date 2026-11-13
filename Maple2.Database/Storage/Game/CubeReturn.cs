using Maple2.Model.Game;

namespace Maple2.Database.Storage;

// What GameStorage.Request.SaveCubes gives back for the cubes it deletes from a plot (the model house built over an
// unnamed home), written in the same transaction as the delete. Joins are storage stacks that get the deleted
// cubes, with their new amount; NewStacks are storage items made for them, owned by AccountId; Mailed are items
// sent to CharacterId, one system mail each. A maid cube's maid moves with it, to the stack or the mailed item
// (MaidCubeId names the cube, 0 for none).
public sealed class CubeReturn {
    // ExpiryTime: the expiry an empty stack takes from the cube that joins it (null: the stack keeps its own).
    public sealed record Join(Item Stack, int Amount, long MaidCubeId, long? ExpiryTime = null);
    public sealed record Add(Item Item, long MaidCubeId);

    public required long AccountId { get; init; }
    public required long CharacterId { get; init; }
    public List<Join> Joins { get; } = [];
    public List<Add> NewStacks { get; } = [];
    public List<Add> Mailed { get; } = [];

    // Set by a save that went through: the new storage items (NewStacks' order, with their uids) and the maids moved
    // to a storage item. The mails are added to SaveCubes' mailed list.
    public List<Item> Created { get; } = [];
    public List<Maid> MovedMaids { get; } = [];
}
