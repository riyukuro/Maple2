using Maple2.Model.Game;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using Serilog;

namespace Maple2.Server.Game.Model.Widget;

/// <summary>The RainbowMonster widget (see <see cref="RainbowMonsterScore"/>): it lights each group's score meshes.</summary>
public class RainbowMonsterWidget : Widget {
    public RainbowMonsterScore Score { get; }

    public RainbowMonsterWidget(FieldManager field, Random? random = null) : base(field) {
        Score = new RainbowMonsterScore(random);
    }

    public override void Action(string function, int numericArg, string stringArg) {
        switch (function) {
            case "CreateGroup":
                Score.CreateGroup(numericArg, stringArg);
                break;
            case "SetKillerNpc":
                Score.SetKillerNpc(stringArg);
                break;
            case "SetBadNpc":
                Score.SetBadNpc(stringArg);
                break;
            case "SetBadNpcScore":
                Score.SetBadNpcScore(stringArg);
                break;
            case "SetBadNpcSoundKey":
                Score.SetBadNpcSoundKey(stringArg);
                break;
            case "SetLoseScoreTick":
                Score.SetLoseScoreTick(int.TryParse(stringArg, out int tick) ? tick : numericArg, Field.FieldTick);
                break;
            case "InitRandomMaxScore":
                Score.InitRandomMaxScore(stringArg);
                ShowMeshes(Score.Groups);
                break;
            case "InitScoreMesh":
                if (int.TryParse(stringArg, out int meshBase)) {
                    Score.InitScoreMesh(numericArg, meshBase, MeshRun(meshBase));
                    ShowMeshes([numericArg]);
                }
                break;
            default:
                Log.Logger.Warning("Unknown function called on RainbowMonsterWidget: {Function}", function);
                break;
        }
    }

    public override bool Check(string name, string arg) {
        return name == "IsMissionSuccess" && Score.IsMissionSuccess(arg);
    }

    public void Update(long tick) {
        ShowMeshes(Score.Update(tick));
    }

    public void NpcDied(int npcId, int killerNpcId) {
        IReadOnlyList<int> changed = Score.NpcDied(npcId, killerNpcId, out bool bad);
        if (bad && Score.BadNpcSoundKey.Length > 0) {
            Field.Broadcast(SoundPacket.System(Score.BadNpcSoundKey));
        }
        ShowMeshes(changed);
    }

    // The map's meshes numbered on from the base without a gap (3100-3123: 24).
    private int MeshRun(int meshBase) {
        int count = 0;
        while (Field.TriggerObjects.Meshes.ContainsKey(meshBase + count)) {
            count++;
        }
        return count;
    }

    private void ShowMeshes(IEnumerable<int> groups) {
        foreach (int group in groups) {
            (int meshBase, int meshCount, int lit) = Score.Meshes(group);
            for (int i = 0; i < meshCount; i++) {
                if (!Field.TriggerObjects.Meshes.TryGetValue(meshBase + i, out TriggerObjectMesh? mesh)) {
                    continue;
                }
                bool visible = i < lit;
                if (mesh.Visible == visible) {
                    continue;
                }
                mesh.Visible = visible;
                Field.Broadcast(TriggerPacket.Update(mesh));
            }
        }
    }
}
