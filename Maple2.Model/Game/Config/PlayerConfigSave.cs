namespace Maple2.Model.Game;

// The player config the World keeps for a character between logins (PlayerConfig Save/Get): the buffs kept across a
// logout, the skill cooldowns, the town cooldowns held through a PvP-zone stay and the death penalty. Stop times are
// epoch seconds (the save), remaining times milliseconds at that stop time.
public class PlayerConfigSave {
    public List<PlayerConfigBuff> Buffs { get; set; } = [];
    public List<PlayerConfigCooldown> SkillCooldowns { get; set; } = [];
    public List<PlayerConfigCooldown> PvpHeldCooldowns { get; set; } = [];
    public bool PvpStay { get; set; }
    public int DeathCount { get; set; }
    public int DeathMsRemaining { get; set; }
    public long DeathStopTime { get; set; }
}

public class PlayerConfigBuff {
    public int Id { get; set; }
    public int Level { get; set; }
    public int MsRemaining { get; set; }
    public long StopTime { get; set; }
    public bool Enabled { get; set; }
    public int Stacks { get; set; }
}

public class PlayerConfigCooldown {
    public int SkillId { get; set; }
    public int SkillLevel { get; set; }
    public int GroupId { get; set; }
    public int MsRemaining { get; set; }
    public long StopTime { get; set; }
    public int Charges { get; set; }
    public long RechargeMs { get; set; }
}
