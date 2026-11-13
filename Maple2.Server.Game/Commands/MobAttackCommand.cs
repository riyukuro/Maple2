using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Commands;

public class MobAttackCommand : GameCommand {
    private readonly GameSession session;

    public MobAttackCommand(GameSession session) : base(AdminPermissions.Debug, "mob-attack", "Make the nearest mob (or --object) use a skill on you.") {
        this.session = session;

        var skillId = new Argument<int?>("skillId", () => null, "Skill id for the mob to use.");
        var skillLevel = new Option<short>(["--level", "-l"], () => 1, "Skill level.");
        var objectId = new Option<int?>(["--object", "-o"], () => null, "Object id of the mob; default the nearest living mob.");

        AddArgument(skillId);
        AddOption(skillLevel);
        AddOption(objectId);
        this.SetHandler<InvocationContext, int?, short, int?>(Handle, skillId, skillLevel, objectId);
    }

    private void Handle(InvocationContext ctx, int? skillId, short skillLevel, int? objectId) {
        if (session.Field is null) {
            ctx.Console.Error.WriteLine("No field loaded.");
            return;
        }

        // The mob asked for, else the living mob nearest to the player.
        IEnumerable<MobAttackRules.Candidate> candidates = session.Field.Mobs.Values
            .Select(npc => new MobAttackRules.Candidate(npc.ObjectId, npc.Position, npc.IsDead));
        int picked = MobAttackRules.Pick(candidates, objectId, session.Player.Position);
        if (picked == 0 || !session.Field.Mobs.TryGetValue(picked, out FieldNpc? mob)) {
            ctx.Console.Error.WriteLine(objectId is null ? "No mobs found on the current map." : $"No mob with object id {objectId}.");
            return;
        }

        ctx.Console.Out.WriteLine($"Using mob: {mob.Value.Metadata.Name} (Id: {mob.Value.Metadata.Id}, ObjectId: {mob.ObjectId})");

        // Check if mob has skills
        NpcMetadataSkill.Entry[] entries = mob.Value.Metadata.Skill.Entries;
        if (entries.Length == 0) {
            ctx.Console.Error.WriteLine("This mob has no skills.");
            return;
        }

        // If no skill id passed, list available skills grouped by id
        if (skillId is null) {
            ctx.Console.Out.WriteLine("Available skills:");
            foreach (var group in entries.GroupBy(e => e.Id)) {
                string levels = string.Join(", ", group.Select(e => e.Level));
                ctx.Console.Out.WriteLine($"  SkillId: {group.Key} (Levels: {levels})");
            }
            return;
        }

        // Validate skill exists in metadata
        if (!session.Field.SkillMetadata.TryGet(skillId.Value, skillLevel, out SkillMetadata? _)) {
            ctx.Console.Error.WriteLine($"Skill {skillId.Value} level {skillLevel} not found in metadata.");
            return;
        }

        // Warn if the mob does not own the skill in its own skill list
        if (!entries.Any(e => e.Id == skillId.Value && e.Level == skillLevel)) {
            ctx.Console.Out.WriteLine($"Warning: {mob.Value.Metadata.Name} does not have skill {skillId.Value} (level {skillLevel}) in its skill list. Casting anyway...");
        }

        // Aim the mob at the player, and report what each attack point's range takes.
        mob.BattleState.ForceTarget(session.Player);
        GameSession gm = session;
        mob.SkillState.AttackWatch = new MobAttackRules.Watch(skillId.Value, Environment.TickCount64,
            line => gm.Send(NoticePacket.Message(line, true)));

        // Cast the skill facing the player
        // The AI waits for the cast: its own next cast would cancel this one.
        mob.AiState.Hold(mob.CastAiSkill(skillId.Value, skillLevel, faceTarget: 1, facePos: session.Player.Position));
        ctx.Console.Out.WriteLine($"Mob {mob.Value.Metadata.Name} casting skill {skillId.Value} (level {skillLevel}).");
    }
}
