using System.Web;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game;

public class InterfaceText : IByteSerializable {
    private readonly bool isLocalized;
    private readonly int unknown;
    private readonly StringCode code;
    private readonly string[] args;
    private readonly string message;
    // A text of the client's PvP string category (kind 3, StringIdInterface::FUN_141024e60: PVPStringId, the keys of
    // stringidinterfacebase.cpp MakePvpStringMap): its id is the client's own.
    private readonly int pvpStringId = -1;

    public InterfaceText(string message, bool htmlEncoded = false) {
        isLocalized = false;
        unknown = message.StartsWith("s_") ? 5 : 0;

        args = [];
        this.message = htmlEncoded ? message : HttpUtility.HtmlEncode(message);
    }

    public InterfaceText(StringCode code, params string[] args) {
        isLocalized = true;
        unknown = 1;

        this.code = code;
        this.args = args;
        message = string.Empty;
    }

    public static implicit operator InterfaceText(StringCode code) => new InterfaceText(code);

    private InterfaceText(int pvpStringId, string[] args) {
        isLocalized = true;
        unknown = 3;
        this.pvpStringId = pvpStringId;
        this.args = args;
        message = string.Empty;
    }

    // s_dualwin1 to 9 are 1 to 9, s_dualprepareplayer 10, s_dualstartcombat 11.
    public static InterfaceText Pvp(int pvpStringId, params string[] args) => new(pvpStringId, args);

    public void WriteTo(IByteWriter writer) {
        writer.WriteBool(isLocalized);
        writer.WriteInt(unknown);
        if (isLocalized) {
            writer.WriteInt(pvpStringId >= 0 ? pvpStringId : StringCodeMap.ToClient(code)); // KMS 2525: client string ids differ from the server enum
            writer.WriteInt(args.Length);
            foreach (string arg in args) {
                writer.WriteUnicodeString(arg);
            }
        } else {
            writer.WriteUnicodeString(message);
        }
    }
}
