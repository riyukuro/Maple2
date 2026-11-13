namespace Maple2.Model.Enum;

// CGuildInfo m_pvpState (Guild 0, Guild 0x27). The client's own check: Pvp 3 (join) is sent only in Matched.
public enum GuildPvpState : byte {
    None = 0,
    Registered = 1,
    Matched = 2,
}
