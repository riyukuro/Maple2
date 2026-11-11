using Maple2.Model.Common;
using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// Edits of a plot's cubes: HomeAction 6 (portal cube settings), 12 (notice cube settings), 13 (open the
// settings of a cube) and the cube edits of RequestCube. The client offers them to the plot's owner only.
public static class HomeEditRules {
    // Server limits: the client has no length test of its own before it writes the strings.
    public const int MaxPortalNameLength = 64;
    public const int MaxPortalTargetLength = 64;
    public const int MaxNoticeLength = 1024;

    public const byte MaxPortalMethod = 1; // 0 interact, 1 touch
    public const byte MaxPortalDestination = 2; // 0 portal in the home, 1 selected map, 2 friend's home

    // The plot a session may edit is one of its own account. Every home has the same map id and plot
    // number, and plot numbers repeat between maps, so the number alone does not tell.
    public static bool OwnsPlot(long plotOwnerId, long accountId) {
        return accountId != 0 && plotOwnerId == accountId;
    }

    // Placing, removing, rotating and replacing cubes is also open to the build helpers the plot's owner
    // named: the client lets an account do these four on a plot whose owner ResponseCube 0x35 named to it
    // (CUgcBuildValidator vfunc_10 and vfunc_11, ugcbuildvalidator.cpp:694 and :722; rotate and remove in
    // cubemapinformationservice_3.cpp:2220). Every other edit stays with the owner.
    public static bool MayBuild(long plotOwnerId, long accountId, ICollection<long>? builders) {
        if (OwnsPlot(plotOwnerId, accountId)) {
            return true;
        }
        return accountId != 0 && plotOwnerId != 0 && builders != null && builders.Contains(accountId);
    }

    // HomeAction 6 after the sub-type byte: u8 0, i32 cube cell, string name, u8 method, u8 destination,
    // string target (UIUGCPortalDialog::OnApply).
    public static (Vector3B Cell, string Name, byte Method, byte Destination, string Target) ReadPortal(IByteReader packet) {
        packet.ReadByte();
        var cell = packet.Read<Vector3B>();
        string name = packet.ReadUnicodeString();
        byte method = packet.ReadByte();
        byte destination = packet.ReadByte();
        string target = packet.ReadUnicodeString();
        return (cell, name, method, destination, target);
    }

    // The client refuses a name another portal of the plot has (its list comes with mode 13's answer).
    public static bool PortalAllowed(string name, byte method, byte destination, string target, IEnumerable<string> otherNames) {
        if (name.Length > MaxPortalNameLength || target.Length > MaxPortalTargetLength) {
            return false;
        }
        if (method > MaxPortalMethod || destination > MaxPortalDestination) {
            return false;
        }
        return !otherNames.Contains(name);
    }

    // HomeAction 12 after the sub-type byte: u8 1, i32 cube cell, string text, u8 distance
    // (UIUGCCubeNoticeDialog::OnApply).
    public static (Vector3B Cell, string Notice, byte Distance) ReadNotice(IByteReader packet) {
        packet.ReadByte();
        var cell = packet.Read<Vector3B>();
        string notice = packet.ReadUnicodeString();
        byte distance = packet.ReadByte();
        return (cell, notice, distance);
    }

    public static bool NoticeAllowed(string notice) {
        return notice.Length <= MaxNoticeLength;
    }
}
