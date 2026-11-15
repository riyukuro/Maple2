using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

// The account's registered mentoring character of a season (event value MentoringRegistration, "role,character id").
// The client allows one mentor or one returning character per account for the season: its register boxes say the
// character "cannot be changed during the event period" (s_mentoring_mentee_register_warn,
// s_mentoring_mentor_register_warn) and "You can only set 1 character as a mentor" (s_mentoring_mentor_accept_warn).
// The account's other characters get role 3 (after a mentee) or 6 (after a mentor), whose menu notice names the
// registered character from Mentor 12 (uigamemenudialog.cpp:2066-2076).
public static class MentoringRegistration {
    public static string Text(MentorRole role, long characterId) => $"{(int) role},{characterId}";

    // The stored registration, null when there is none or the text is not one.
    public static (MentorRole Role, long CharacterId)? Parse(string? text) {
        string[] parts = text?.Split(',') ?? [];
        if (parts.Length != 2 || !int.TryParse(parts[0], out int role) || !long.TryParse(parts[1], out long characterId)
            || characterId <= 0 || (MentorRole) role is not (MentorRole.Mentee or MentorRole.Mentor)) {
            return null;
        }
        return ((MentorRole) role, characterId);
    }

    // The role of `characterId` in a running season: the registered role for the registered character, 3 or 6 for
    // the account's others, the candidate role while the account has registered no one.
    public static MentorRole Role((MentorRole Role, long CharacterId)? registration, long characterId, bool returning) {
        if (registration is not { } registered) {
            return returning ? MentorRole.MenteeCandidate : MentorRole.MentorCandidate;
        }
        if (registered.CharacterId == characterId) {
            return registered.Role;
        }
        return registered.Role == MentorRole.Mentee ? MentorRole.UnregisteredMentee : MentorRole.UnregisteredMentor;
    }

    // Mentor 12: the account's mentor character first, its mentee character second (CMentoringManager::OnSetIds:
    // m_n28 is named by the role-6 notice, m_n30 by the role-3 notice); 0 for none.
    public static (long Mentor, long Mentee) Ids((MentorRole Role, long CharacterId)? registration) {
        return registration switch {
            { Role: MentorRole.Mentor } registered => (registered.CharacterId, 0),
            { Role: MentorRole.Mentee } registered => (0, registered.CharacterId),
            _ => (0, 0),
        };
    }
}
