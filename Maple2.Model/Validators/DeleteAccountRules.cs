namespace Maple2.Model.Validators;

// The check of the character list's account delete button (CheckDeleteAccount). The client names the first
// character that blocks the delete with the text of its code (UICharacterListDialog::GetDeleteAccountErrorText).
public static class DeleteAccountRules {
    public const int Ok = 0;
    public const int CharacterPending = 0x0B; // s_delete_account_result_check_fail_err_characterpending
    public const int BlackMarket = 0x16;
    public const int Guild = 0x18;
    public const int UgcMarket = 0x19;
    public const int Wedding = 0x1A;
    public const int Mail = 0x1B;
    public const int MesoMarket = 0x1C;
    // s_delete_account_check_user_cert_id_query_fail: the delete itself needs the platform's identity check,
    // which this server does not have (the client sends it only in the CN region, FUN_1418f2480).
    public const int CertQueryFail = 0x20;

    public readonly record struct Holdings(
        bool DeletePending,
        bool BlackMarket,
        bool Guild,
        bool UgcMarket,
        bool Married,
        bool UnreadMail,
        bool MesoMarket);

    // The first block of one character, in the order of the client's text table; 0 when it holds none.
    public static int Block(Holdings holdings) {
        if (holdings.DeletePending) return CharacterPending;
        if (holdings.BlackMarket) return BlackMarket;
        if (holdings.Guild) return Guild;
        if (holdings.UgcMarket) return UgcMarket;
        if (holdings.Married) return Wedding;
        if (holdings.UnreadMail) return Mail;
        if (holdings.MesoMarket) return MesoMarket;
        return Ok;
    }
}
