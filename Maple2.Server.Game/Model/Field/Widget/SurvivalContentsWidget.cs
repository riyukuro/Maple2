using System.Collections.Concurrent;
using System.Numerics;
using Maple2.Model.Game.Pvp;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Maple2.Tools;
using Serilog;

namespace Maple2.Server.Game.Model.Widget;

// The survival maps' SurvivalContents widget (03_storm.xml): StormData, then EnterStep 1 to 8, each until the
// condition TimeOver. The storm (SurvivalStorm) is a safe circle at the field's center that shrinks with each step; the
// steps go to the clients as SurvivalEvent 4 (ready), 5 (start) and 0 (the zone).
public class SurvivalContentsWidget : Widget {
    // Chosen here: the smallest safe zone.
    private const float MinRadius = 300f;

    public SurvivalStorm? Storm { get; private set; }
    // The client's zone (SurvivalEvent 0), keyed by the step (0: none).
    private short zoneId;
    // The game ended (Stop): no step runs or goes out until the next StormData.
    public bool Stopped { get; private set; }
    public Vector3 Center { get; private set; }

    public SurvivalContentsWidget(FieldManager field) : base(field) {
        Conditions = new ConcurrentDictionary<string, int>();
    }

    public override void Action(string function, int numericArg, string stringArg) {
        switch (function) {
            case "StormData":
                StormData();
                break;
            case "EnterStep":
                EnterStep(int.TryParse(stringArg, out int step) ? step : numericArg);
                break;
            case "ExitStep":
                break;
            default:
                Log.Logger.Warning($"Unknown function called on SurvivalContentsWidget: {function}");
                break;
        }
    }

    public override bool Check(string name, string arg) {
        return name == "TimeOver" && !Stopped && Storm != null && Storm.TimeOver(Field.FieldTick);
    }

    // The game's end: the steps stop (the client prints the storm lines 4, 5 and 6 whatever the game's state) and the
    // client's zones are cleared (SurvivalEvent 3).
    public void Stop() {
        if (Stopped) {
            return;
        }
        Stopped = true;
        zoneId = 0;
        Field.Broadcast(MapleSurvivalPacket.ClearZones());
    }

    // The storm over the field's bounds: centered, the start radius half its larger side.
    private void StormData() {
        Vector2[] points = Field.Entities.BoundingBox.Polygon is Maple2.Tools.Collision.Polygon polygon ? polygon.Points : [Vector2.Zero];
        Vector2 min = points.Aggregate(new Vector2(float.MaxValue), Vector2.Min);
        Vector2 max = points.Aggregate(new Vector2(float.MinValue), Vector2.Max);
        Center = new Vector3((min.X + max.X) / 2, (min.Y + max.Y) / 2, 0);
        float radius = Math.Max(max.X - min.X, max.Y - min.Y) / 2;
        Storm = new SurvivalStorm(Math.Max(radius, MinRadius), MinRadius);
        zoneId = 0;
        Stopped = false;
        Field.Broadcast(MapleSurvivalPacket.ClearZones());
    }

    // Each step: the "ready" message (4) and the storm bar's countdown to the step's end (6), the start message (5),
    // then the zone's new radius (1: the float is the radius, GMS 0x74b8d0). Each step is its own zone, keyed by the
    // step: the step before's zone goes (2), the step's zone comes (0) with the circle before the step and the step's
    // safe circle as its preview (the minimap's next zone). The client picks the zone's effect by its id (the A01 storm
    // up to id 2, A02 above, survivalstormfx.cpp NifPath) and puts the closing storm (B01) on at the start (5) only
    // once zone 7 exists (StormFx_OnShrinkStart), so the ids follow the steps 1 to 8.
    private void EnterStep(int step) {
        if (Stopped) {
            return;
        }
        Storm ??= new SurvivalStorm(MinRadius * 10, MinRadius);
        float before = Storm.SafeRadius;
        Storm.Enter(step, Field.FieldTick);
        short id = (short) Math.Max(1, Storm.Step);
        if (zoneId != id) {
            if (zoneId != 0) {
                Field.Broadcast(MapleSurvivalPacket.RemoveZone(zoneId));
            }
            zoneId = id;
            Field.Broadcast(MapleSurvivalPacket.AddZone(id, Short(before), (short) Storm.Step, Center, Short(Storm.SafeRadius), Center));
        }
        Field.Broadcast(MapleSurvivalPacket.ReduceReady((short) (SurvivalStorm.ReadyMs / 1000)));
        Field.Broadcast(MapleSurvivalPacket.Countdown((short) (SurvivalStorm.StepMs / 1000)));
        Field.Broadcast(MapleSurvivalPacket.ReduceStart());
        Field.Broadcast(MapleSurvivalPacket.UpdateZone(id, Storm.SafeRadius, Center));
    }

    private static short Short(float value) => (short) Math.Clamp(value, 0, short.MaxValue);
}
