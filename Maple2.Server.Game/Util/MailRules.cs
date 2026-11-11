namespace Maple2.Server.Game.Util;

public static class MailRules {
    // The client's send dialog refuses an empty title or text, a title over 100 characters and a text over 2000.
    public const int MaxTitleLength = 100;
    public const int MaxContentLength = 2000;

    public static bool CanSend(string title, string content) {
        return title.Length is > 0 and <= MaxTitleLength && content.Length is > 0 and <= MaxContentLength;
    }
}
