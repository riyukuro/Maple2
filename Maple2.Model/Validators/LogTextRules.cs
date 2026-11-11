using System.Text;

namespace Maple2.Model.Validators;

// LogSend: text written by the client that ends in the server's log.
public static class LogTextRules {
    // The client sets no limit on the two strings (bigdatalogsystem.cpp:160-168); this is the server's.
    public const int MaxLength = 512;

    // The text as it is logged: control characters replaced by a space, cut at MaxLength.
    public static string Clean(string? text) {
        if (string.IsNullOrEmpty(text)) {
            return string.Empty;
        }

        int length = System.Math.Min(text.Length, MaxLength);
        var builder = new StringBuilder(length);
        for (int i = 0; i < length; i++) {
            char c = text[i];
            builder.Append(char.IsControl(c) || c is (char) 0x2028 or (char) 0x2029 ? ' ' : c);
        }
        return builder.ToString();
    }
}
