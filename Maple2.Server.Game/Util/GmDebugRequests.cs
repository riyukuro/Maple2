using System.Collections.Concurrent;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Game.Util;

// 0x50 SetDebugMode, mode 4: the client's NPC control window (opened by picking an NPC with the cursor
// in debug mode). The body after the mode byte, read as the client writes it: the NPC's object id, a
// command byte and the command's own fields.
// SkillId and SkillLevel are the first entry of the skill list (command 8).
public readonly record struct GmNpcControlRequest(int ObjectId, byte Command, long Value, int EffectId, int EffectLevel, int SkillCount, string Text, int SkillId = 0, int SkillLevel = 0) {
    public const byte Select = 0; // the NPC was picked; no fields
    public const byte HealthPercent = 1; // long, 1 to 100
    public const byte HealthValue = 2; // long, above 0
    // UINpcControlDialog buttons (uinpccontroldialog.cpp:517-534): each writes one long, 0.
    public const byte AiOn = 4;
    public const byte AiOff = 5;
    public const byte Extra = 0xB;
    // UINpcControlDialog (uinpccontroldialog.cpp:395-402): short loop, short count, count x (int skill id, int level)
    public const byte UseSkill = 8;
    public const byte StopSkill = 9; // no fields
    public const byte AiFile = 0xD; // one string
    public const byte AddEffect = 0xE; // int id, int level, int
    public const byte RemoveEffect = 0xF; // long id

    // More skills than the window's list can hold are not read.
    public const int MaxSkills = 64;

    public static GmNpcControlRequest Read(IByteReader packet) {
        int objectId = packet.ReadInt();
        byte command = packet.ReadByte();
        long value = 0;
        int effectId = 0;
        int effectLevel = 0;
        int skillCount = 0;
        string text = string.Empty;
        int skillId = 0;
        int skillLevel = 0;

        switch (command) {
            case Select:
            case StopSkill:
                break;
            case UseSkill:
                value = packet.ReadShort(); // loop
                skillCount = Math.Clamp((int) packet.ReadShort(), 0, MaxSkills);
                for (int i = 0; i < skillCount; i++) {
                    int id = packet.ReadInt();
                    int level = packet.ReadInt();
                    if (i == 0) {
                        skillId = id;
                        skillLevel = level;
                    }
                }
                break;
            case AiFile:
                text = packet.ReadUnicodeString();
                break;
            case AddEffect:
                effectId = packet.ReadInt();
                effectLevel = packet.ReadInt();
                value = packet.ReadInt();
                break;
            case RemoveEffect:
                value = packet.ReadLong();
                effectId = value is > 0 and <= int.MaxValue ? (int) value : 0;
                break;
            case 1 or 2 or 3 or 4 or 5 or 6 or 7 or 0xA or 0xB or 0x10:
                // One long each: the two health commands and the battle setting carry a value, the AI
                // on/off, trace, assist and list buttons write 0.
                value = packet.ReadLong();
                break;
        }

        return new GmNpcControlRequest(objectId, command, value, effectId, effectLevel, skillCount, text, skillId, skillLevel);
    }

    // The window's extra data list has no scroll bar; more entries than this are not sent.
    public const int MaxExtra = 32;

    // The NPC's AI values for the window's extra data list: by key, at most MaxExtra.
    public static List<(string Key, int Value)> ExtraList(IEnumerable<KeyValuePair<string, int>> values) {
        return values.OrderBy(entry => entry.Key, StringComparer.Ordinal).Take(MaxExtra).Select(entry => (entry.Key, entry.Value)).ToList();
    }

    // The AI file button sends the path of a file picked under the client's Data/Xml/AI folder
    // (uinpccontroldialog.cpp:576-591). The server's name of an AI file is that path below the AI folder,
    // with forward slashes; null for a text that is no xml file name or tries to leave the folder.
    public const int MaxAiFile = 260;

    public static string? AiFileName(string? text) {
        if (string.IsNullOrEmpty(text) || text.Length > MaxAiFile) {
            return null;
        }
        string path = text.Replace('\\', '/');
        int index = path.LastIndexOf("/xml/ai/", StringComparison.OrdinalIgnoreCase);
        string name = index >= 0 ? path[(index + "/xml/ai/".Length)..] : path[(path.LastIndexOf('/') + 1)..];
        if (name.Length <= ".xml".Length || !name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }
        if (name.Split('/').Any(part => part.Length == 0 || part == "." || part == "..") || name.Any(char.IsControl)) {
            return null;
        }
        return name;
    }

    // The same from the NPC's live store. ConcurrentDictionary.ToArray copies under the store's locks; a LINQ
    // call on the store itself copies in two steps and throws when a key is added in between.
    public static List<(string Key, int Value)> ExtraList(ConcurrentDictionary<string, int> values) {
        return ExtraList(values.ToArray());
    }

    // The headline of the window's text: the AI file, marked while the AI is switched off.
    public static string Headline(string? aiName, bool aiOff) {
        string name = string.IsNullOrEmpty(aiName) ? "-" : aiName;
        return aiOff ? name + " (AI off)" : name;
    }

    // The health the two health commands ask for, or -1 when the command is another one or the value is
    // outside what the window accepts (1 to 100 percent; a value above 0). Never below 1 and never above
    // the total: the window does not kill.
    public static long HealthTarget(byte command, long value, long total) {
        if (total < 1) {
            return -1;
        }

        switch (command) {
            case HealthPercent:
                if (value is < 1 or > 100) {
                    return -1;
                }
                return Math.Max(1, (long) ((decimal) total * value / 100));
            case HealthValue:
                if (value < 1) {
                    return -1;
                }
                return Math.Min(value, total);
            default:
                return -1;
        }
    }
}

// 0x50 SetDebugMode, mode 7: the "multi execute" button of the debug guide window
// (UIDebugGuideDialog::SendCommand, uidebugguidedialog.cpp:446). After the mode byte: int count, then per
// line the command text and its argument (an empty string when the line takes none).
public static class GmGuideRequest {
    public const int MaxLines = 32;
    public const int MaxLength = 256;

    // The lines, or null when the count or a text is outside what a window sends.
    public static List<(string Command, string Argument)>? Read(IByteReader packet) {
        int count = packet.ReadInt();
        if (count is < 1 or > MaxLines) {
            return null;
        }

        var lines = new List<(string, string)>(count);
        for (int i = 0; i < count; i++) {
            string command = packet.ReadUnicodeString();
            string argument = packet.ReadUnicodeString();
            if (command.Length is < 1 or > MaxLength || argument.Length > MaxLength) {
                return null;
            }
            lines.Add((command, argument));
        }
        return lines;
    }
}
