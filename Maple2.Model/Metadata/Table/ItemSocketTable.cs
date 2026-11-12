namespace Maple2.Model.Metadata;

// SocketSystem and SocketTransfer: the locale's ItemSocketSystem (client feature 0x2e) and ItemSocketTransfer01 (0xa7)
// features, read at the ingest. The client asks 0x2e for every socket action on an item (CItem::FUN_140529560) and
// builds the socket_transfer page only with 0xa7 (UIItemSocketSystemDialog::OnLoadComplete).
public record ItemSocketTable(IReadOnlyDictionary<int, IReadOnlyDictionary<int, ItemSocketMetadata>> Entries,
    bool SocketSystem = true, bool SocketTransfer = true) : Table;

// OpenType 0 opens OpenCount (fixOpenCount); 1 opens a count from randomOpenRange.
public record ItemSocketMetadata(byte MaxCount, byte OpenCount, int OpenType = 0) {
    // CItemSocketDataContainer GetInitialSocketCount (itemsocketsystemutil.cpp): the range's low end only for
    // openType 0, else 0 (a row's range comes from fixOpenCount only when openType is 0).
    public byte InitialCount => OpenType == 0 ? OpenCount : (byte) 0;
}
