using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

// The server's notice config (the GM notice command, kept by the world server).
//
// Banner: NoticeDialog 0xCF mode 0. The client (UINoticeDialog::OnNoticeDialog) shows its notice dialog for
// NoticeDialogOpenSeconds when the dialog is open and not closed; the game scene opens it at field entry when the
// NoticeDialogUrl constant is set. Chosen here: sent once per channel session, at its first field entry, while the
// banner is on, and to every player when a GM shows it.
// The entry banner waits BannerDelay: the game scene queues the dialog only once the local player exists
// (CUIService::FUN_141ad9e50, uiservice.cpp:742) and CUIService::OnTick opens 3 queued dialogs per tick
// (uiservice.cpp:1898), while OnNoticeDialog (uinoticedialog.cpp) drops a 0xCF that comes before the dialog is open.
// A 0xCF in the field entry burst, next to the player's own FieldAddUser, comes too early. Chosen here: 5 s.
// The page: Show navigates to the NoticeDialogUrl constant (KR "maview:/Game/Banner"), with "maview:" expanded to the
// maview url of ServerEnter (CWebBrowserService::ExpandUrlPrefix); the web server answers /Game/Banner.
//
// Login notice: the lobby opens UILoginNotice (which sends NoticeDialog 0x7A) when CGameRule::IsLoginNoticeOn, which a
// GameEventLoginNotice sets when it starts. The KR gameEvent table has no such event, so the server adds one to the
// login's event list while the login notice is on (Chosen here: the id below, an end a day after the login).
public static class NoticeRules {
    public const int Banner = 0;
    public const int Login = 1;

    public const int Off = 0;
    public const int On = 1;
    public const int Show = 2;
    public const int Status = 3;

    // Past the table's ids and the world's custom string board ids (counted from 1).
    public const int LoginNoticeEventId = 2_100_000_001;

    public static readonly TimeSpan BannerDelay = TimeSpan.FromSeconds(5);

    public static bool BannerAtEntry(bool bannerOn, bool alreadySent) => bannerOn && !alreadySent;

    public static bool ValidRequest(int kind, int action) => kind switch {
        Banner => action is Off or On or Show or Status,
        Login => action is Off or On or Status,
        _ => false,
    };

    // The config after a request; a show or a status leaves it as it is.
    public static (bool banner, bool login) Apply(bool banner, bool login, int kind, int action) {
        if (!ValidRequest(kind, action) || action is not (Off or On)) {
            return (banner, login);
        }
        return kind == Banner ? (action == On, login) : (banner, action == On);
    }

    public static GameEvent LoginNoticeEvent(DateTime now) {
        return new GameEvent(new GameEventMetadata(
            Id: LoginNoticeEventId,
            Type: GameEventType.LoginNotice,
            StartTime: now.AddMinutes(-1),
            EndTime: now.AddDays(1),
            StartPartTime: TimeSpan.Zero,
            EndPartTime: TimeSpan.Zero,
            ActiveDays: [],
            Data: new LoginNotice(),
            Value1: string.Empty,
            Value2: string.Empty,
            Value3: string.Empty,
            Value4: string.Empty
        ));
    }
}
