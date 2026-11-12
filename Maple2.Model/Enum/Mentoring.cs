// ReSharper disable InconsistentNaming

namespace Maple2.Model.Enum;

public enum MentorRole : byte {
    None = 0,
    MenteeCandidate = 1,
    Mentee = 2,
    UnregisteredMentee = 3,
    MentorCandidate = 4,
    Mentor = 5,
    UnregisteredMentor = 6,
}

// The result of Mentor 16 (CMentoringManager::OnError): each shows one notice.
public enum MentorError : byte {
    IsNotReturnUser = 1, // s_mentoring_error_is_not_return_user
    TargetNotExists = 2, // s_mentoring_error_target_not_exists
    InvalidTarget = 3, // s_mentoring_error_invalid_target
    MentorHasMaxCountWaiting = 4, // s_mentoring_error_mentor_has_max_count_waiting
    MenteeHasMaxCountReceived = 5, // s_mentoring_error_mentee_has_max_count_received
    TargetIsNotMentee = 6, // s_mentoring_error_mentee_is_not_mentee
    MenteeHasAlreadyMentor = 7, // s_mentoring_error_mentee_has_already_mento
    AlreadyRequestedToMentee = 8, // s_mentoring_error_already_requested_to_mentee
    FullEarnCoin = 9, // s_mentoring_error_full_earn_coin
}
