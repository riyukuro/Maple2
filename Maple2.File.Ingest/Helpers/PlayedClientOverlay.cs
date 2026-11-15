using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Maple2.File.IO;
using Maple2.File.IO.Crypto;
using Maple2.File.IO.Crypto.Common;
using Maple2.File.IO.Crypto.Keys;
using Maple2.File.IO.Crypto.Stream;

namespace Maple2.File.Ingest.Helpers;

// MS2_PLAYED_CLIENT_OVERLAY=<dir>[;<dir>...]: directories of loose files laid out as in the played client's Xml.m2d
// (itemdata/209.xml, string/en/...), read over its pack the way the client takes loose Data/Xml files over the
// pack: a file the pack has is read from the directory instead, a file it lacks is added. Names match without
// case (itemData/203.xml is the pack's itemdata/203.xml), and of two directories holding a file the later wins. It
// is how an operator's own client additions (items the played client did not ship) count as the played client's.
// The ingest writes the merged pack to a temporary directory and reads it as the played pack, so every played
// read sees the overlay. Unset, the ingest is unchanged.
public static class PlayedClientOverlay {
    public const string Option = "MS2_PLAYED_CLIENT_OVERLAY";

    // The option's directories; each must exist, as a typo would ingest the pack alone without a word.
    public static IReadOnlyList<string> Dirs(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return [];
        }
        List<string> dirs = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        foreach (string dir in dirs) {
            if (!Directory.Exists(dir)) {
                throw new DirectoryNotFoundException($"{Option} is set, but there is no directory at path: {dir}");
            }
        }
        return dirs;
    }

    // The overlay's files by pack name (/-separated, as found under the directory), the later directory winning.
    public static IReadOnlyDictionary<string, string> Files(IEnumerable<string> dirs) {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string dir in dirs) {
            foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) {
                string name = Path.GetRelativePath(dir, file).Replace('\\', '/');
                files.Remove(name);
                files[name] = file;
            }
        }
        return files;
    }

    // Added: the names the overlay added (lower case, as pack names are), which the played pack did not have.
    public readonly record struct Counts(int Kept, int Replaced, IReadOnlyList<string> Added);

    // Writes outM2d (and its .m2h) as the pack with the overlay's files over it. Untouched entries are copied as
    // they are encoded; a replaced entry keeps its index, name and flags, an added one takes the next index and the
    // flags of the pack's first entry; its name is taken in lower case, as the pack's names are and as the ingest
    // looks them up (the client's file system matches without case: the restored client's Table/na/... is the
    // overlay's Table/na/... and the merged pack's table/na/...).
    public static Counts Merge(string packM2d, IReadOnlyDictionary<string, string> overlay, string outM2d) {
        using var reader = new M2dReader(packM2d);
        IPackStream stream;
        using (var headerReader = new BinaryReader(System.IO.File.OpenRead(Path.ChangeExtension(packM2d, ".m2h")))) {
            stream = IPackStream.CreateStream(headerReader);
        }
        using var packData = new FileStream(packM2d, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Write(stream.Version, stream, reader.Files.OrderBy(entry => entry.Index).ToList(), packData, overlay, outM2d);
    }

    // Writes outM2d (and its .m2h) as an NS2F pack (the KMS2 client's) of the files alone (names in lower case),
    // indexed from 1 in name order: a pack to merge over in the tests.
    public static Counts Create(IReadOnlyDictionary<string, string> files, string outM2d) {
        return Write(PackVersion.NS2F, null, [], null, files, outM2d);
    }

    private static Counts Write(PackVersion version, IPackStream? stream, List<PackFileEntry> entries, FileStream? packData,
                                IReadOnlyDictionary<string, string> overlay, string outM2d) {
        string outM2h = Path.ChangeExtension(outM2d, ".m2h");
        var packNames = new HashSet<string>(entries.Select(entry => entry.Name), StringComparer.OrdinalIgnoreCase);
        Encryption addFlag = entries.Count > 0 ? entries[0].FileHeader.BufferFlag : Encryption.Aes | Encryption.Zlib;
        int nextIndex = entries.Count == 0 ? 1 : entries.Max(entry => entry.Index) + 1;

        var headers = new List<IPackFileHeader>();
        var names = new StringBuilder();
        ulong offset = 0;
        int kept = 0, replaced = 0;
        var added = new List<string>();
        using var outData = new FileStream(outM2d, FileMode.Create, FileAccess.Write);

        void Emit(IPackFileHeader header, int index, string? hash, string name, byte[] encoded) {
            header.FileIndex = index;
            header.Offset = offset;
            outData.Write(encoded, 0, encoded.Length);
            offset += (ulong) encoded.Length;
            headers.Add(header);
            names.Append(string.IsNullOrEmpty(hash) ? $"{index},{name}\r\n" : $"{index},{hash},{name}\r\n");
        }

        IPackFileHeader Encoded(int index, Encryption flag, byte[] data, out byte[] encoded) {
            encoded = Encode(version, data, flag, out uint size, out uint compressedSize);
            IPackFileHeader header = NewHeader(version, index, flag);
            header.BufferFlag = flag;
            header.EncodedFileSize = (uint) encoded.Length;
            header.CompressedFileSize = compressedSize;
            header.FileSize = size;
            return header;
        }

        foreach (PackFileEntry entry in entries) {
            IPackFileHeader header = entry.FileHeader;
            if (overlay.TryGetValue(entry.Name, out string? file)) {
                IPackFileHeader newHeader = Encoded(entry.Index, header.BufferFlag, System.IO.File.ReadAllBytes(file), out byte[] encoded);
                Emit(newHeader, entry.Index, entry.Hash, entry.Name, encoded);
                replaced++;
            } else {
                var raw = new byte[header.EncodedFileSize];
                packData!.Seek((long) header.Offset, SeekOrigin.Begin);
                packData.ReadExactly(raw, 0, raw.Length);
                Emit(header, entry.Index, entry.Hash, entry.Name, raw);
                kept++;
            }
        }
        foreach ((string name, string file) in overlay.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)) {
            if (packNames.Contains(name)) {
                continue;
            }
            IPackFileHeader header = Encoded(nextIndex, addFlag, System.IO.File.ReadAllBytes(file), out byte[] encoded);
            Emit(header, nextIndex, null, name.ToLowerInvariant(), encoded);
            nextIndex++;
            added.Add(name.ToLowerInvariant());
        }
        outData.Flush();

        // The file name list and the file table, both zlib + AES.
        const Encryption tableFlag = Encryption.Aes | Encryption.Zlib;
        byte[] encodedNames = Encode(version, Encoding.UTF8.GetBytes(names.ToString()), tableFlag, out uint namesSize, out uint namesCompressed);
        byte[] table;
        using (var tableStream = new MemoryStream()) {
            using (var tableWriter = new BinaryWriter(tableStream, Encoding.UTF8, true)) {
                foreach (IPackFileHeader header in headers) {
                    header.Encode(tableWriter);
                }
            }
            table = tableStream.ToArray();
        }
        byte[] encodedTable = Encode(version, table, tableFlag, out uint tableSize, out uint tableCompressed);

        using (var outHeader = new FileStream(outM2h, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(outHeader)) {
            writer.Write((uint) version);
            if (version == PackVersion.NS2F) {
                // NS2F: a u32 file count, then the six u64 sizes in the order PackStreamVer2 reads them (its Encode
                // writes a u64 count, which shifts every field).
                writer.Write((uint) headers.Count);
                writer.Write((ulong) tableCompressed);
                writer.Write((ulong) encodedTable.Length);
                writer.Write((ulong) namesSize);
                writer.Write((ulong) namesCompressed);
                writer.Write((ulong) encodedNames.Length);
                writer.Write((ulong) tableSize);
            } else {
                stream!.FileListCount = (ulong) headers.Count;
                stream.HeaderSize = namesSize;
                stream.CompressedHeaderSize = namesCompressed;
                stream.EncodedHeaderSize = (ulong) encodedNames.Length;
                stream.DataSize = tableSize;
                stream.CompressedDataSize = tableCompressed;
                stream.EncodedDataSize = (ulong) encodedTable.Length;
                stream.Encode(writer);
            }
            writer.Write(encodedNames);
            writer.Write(encodedTable);
        }
        return new Counts(kept, replaced, added);
    }

    private static IPackFileHeader NewHeader(PackVersion version, int index, Encryption flag) => version switch {
        PackVersion.MS2F => PackFileHeaderVer1.CreateHeader(index, flag, 0, []),
        PackVersion.NS2F => PackFileHeaderVer2.CreateHeader(index, flag, 0, []),
        _ => PackFileHeaderVer3.CreateHeader(version, index, flag, 0, []),
    };

    // CryptoManager.Encrypt, but AES over the whole compressed buffer: the package's transforms only the
    // uncompressed length, which breaks entries whose zlib stream is longer than their data (tiny files).
    private static byte[] Encode(PackVersion version, byte[] data, Encryption flag, out uint size, out uint compressedSize) {
        byte[] buffer = flag.HasFlag(Encryption.Zlib) ? Zlib(data) : (byte[]) data.Clone();
        size = (uint) data.Length;
        compressedSize = (uint) buffer.Length;
        if (flag.HasFlag(Encryption.Aes)) {
            CipherKeys.GetKeyAndIV(version, compressedSize, out byte[] key, out byte[] iv);
            new AESCipher(key, iv).TransformBlock(buffer, 0, (uint) buffer.Length, buffer, 0);
            buffer = Encoding.UTF8.GetBytes(Convert.ToBase64String(buffer));
        } else if (flag.HasFlag(Encryption.Xor)) {
            throw new NotSupportedException("An XOR-flagged pack entry cannot be re-encoded");
        }
        return buffer;
    }

    // A zlib stream: header, deflate, Adler-32.
    private static byte[] Zlib(byte[] data) {
        using var output = new MemoryStream();
        output.WriteByte(0x78);
        output.WriteByte(0x9C);
        using (var deflate = new DeflateStream(output, CompressionMode.Compress, true)) {
            deflate.Write(data, 0, data.Length);
        }
        uint a = 1, b = 0;
        foreach (byte value in data) {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }
        Span<byte> adler = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(adler, (b << 16) | a);
        output.Write(adler);
        return output.ToArray();
    }
}
