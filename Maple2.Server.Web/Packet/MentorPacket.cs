
using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Web.Packet;

public static class MentorPacket {
    public static ByteWriter MenteeList(IList<PlayerInfo> mentees) {
        var pWriter = new ByteWriter();
        pWriter.WriteInt(0); // 0
        pWriter.WriteInt(mentees.Count);

        foreach (PlayerInfo mentee in mentees) {
            // Both ids are the character's (ReadMentor, menteesearch.cpp:351). The second is the row's charSN, which the
            // right-click menu keeps as the character id for character info, trade, report and the rest
            // (uiinvitementoringdialog.cpp:322 OnPopupMenu -> UIPopUpMenu::FUN_141a7f090, uipartydialog.cpp:2111). The
            // first is only sent back with the invite, where the server keys on the name.
            pWriter.WriteLong(mentee.CharacterId);
            pWriter.WriteLong(mentee.CharacterId);
            pWriter.WriteUnicodeStringWithLength(mentee.Name);
            pWriter.WriteUnicodeStringWithLength(mentee.Picture);
            pWriter.WriteInt(mentee.Level);
            pWriter.Write<Job>(mentee.Job);
            pWriter.WriteInt(mentee.Online ? 1 : 0);
        }

        return pWriter;
    }

    // A refused search (WebController.MenteeSearchLimit). The client reads a result other than 0 as a failed search
    // and shows s_mentoring_error_request_mentee_list_search_error (ResponseMenteeList::FUN_140b6dfb0,
    // CMenteeSearchManager::OnWebResult, menteesearch.cpp:83-101, :226-259); the list it shows is not changed.
    public static ByteWriter MenteeListRefused() {
        var pWriter = new ByteWriter();
        pWriter.WriteInt(1);
        return pWriter;
    }
}
