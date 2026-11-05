using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim;

/// <summary>
/// A scripted, reproducible crash (Ty 2026-11-03: the Pitts on the grass, pieces losing their paint and sinking): the
/// state DebrisGroundTests crash from, and the one the game starts in with the launch argument
/// <c>-crashtest runway</c> or <c>-crashtest grass</c> (FlightSimDriver), so the phone build shows the same crash.
/// <c>-crashtest water</c> is Ty's 2026-11-05 water touch (<see cref="PittsWaterStart"/>).
/// On the first island runway's centreline (or 40 m off it, square to it — the green ground beside the runway, which the
/// island's surface map calls Rough), heading down the runway at 35 m/s, banked 60° right, nose 15° down, sinking 8 m/s,
/// its lowest hard point or tyre 10 cm up.
/// </summary>
public static class CrashScenario
{
    public const string ArgName = "-crashtest";

    public static RigidBodyState PittsStart(AircraftConfig c, bool grass)
    {
        WorldTerrain.Airport a = WorldTerrain.Airports.First(ap => IslandRunways.Ends(ap).Count > 0);
        WorldTerrain.RunwayEnd rw = IslandRunways.Ends(a)[0];
        double x = rw.CentreX, y = rw.CentreY;
        if (grass) { x += -rw.AlongY * 40.0; y += rw.AlongX * 40.0; }
        double hdg = rw.HeadingRad, bank = 60 * Math.PI / 180, pitch = -15 * Math.PI / 180;
        Quat q = Quat.Multiply(new Quat(0, 0, Math.Sin(hdg / 2), Math.Cos(hdg / 2)),
                 Quat.Multiply(new Quat(0, Math.Sin(pitch / 2), 0, Math.Cos(pitch / 2)), new Quat(Math.Sin(bank / 2), 0, 0, Math.Cos(bank / 2))));
        Vec3 cg = c.Mass.CgVec();
        double low = double.NegativeInfinity;
        foreach (ContactPoint p in AirframeContact.BuildPoints(c)) if (!p.PropDisc) low = Math.Max(low, q.Rotate(p.Body - cg).Z);
        foreach (GearConfig g in c.Gear) low = Math.Max(low, q.Rotate(g.PosVec() - cg).Z);
        double ground = WorldTerrain.GroundHeightAt(x, y);
        Vec3 vw = new(35 * rw.AlongX, 35 * rw.AlongY, 8.0);
        return new RigidBodyState(new Vec3(x, y, -(ground + low + 0.10)), q, q.Conjugate().Rotate(vw), Vec3.Zero);
    }

    /// <summary>
    /// Ty 2026-11-05 08:18 ("crashed the Pitts on the water … I had some sink rate"): wheels-first onto the island's first
    /// float lane (a "water" field, the buoy rows in his clip) at 60 kt (30.9 m/s), sinking 3 m/s, nose 6° up, heading
    /// down the lane from a third of the way along, its lowest tyre 10 cm above the surface. PittsWaterTouchdownTests flies
    /// the same touch: it digs in and goes over.
    /// </summary>
    public static RigidBodyState PittsWaterStart(AircraftConfig c)
    {
        IslandField lane = IslandSites.Fields.First(f => f.Surface == "water");
        double hdg = lane.Heading * Math.PI / 180, ux = Math.Cos(hdg), uy = Math.Sin(hdg);
        double x = lane.Centre.X - ux * lane.Length / 6, y = lane.Centre.Y - uy * lane.Length / 6;
        double surface = WorldTerrain.Active?.WaterSurfaceAt(x, y, lane.Elevation) ?? lane.Elevation;
        double pitch = 6 * Math.PI / 180;
        Quat q = Quat.Multiply(new Quat(0, 0, Math.Sin(hdg / 2), Math.Cos(hdg / 2)), new Quat(0, Math.Sin(pitch / 2), 0, Math.Cos(pitch / 2)));
        Vec3 cg = c.Mass.CgVec();
        double low = double.NegativeInfinity;
        foreach (GearConfig g in c.Gear) low = Math.Max(low, q.Rotate(g.PosVec() - cg).Z);
        Vec3 vw = new(30.9 * ux, 30.9 * uy, 3.0);
        return new RigidBodyState(new Vec3(x, y, -(surface + low + 0.10)), q, q.Conjugate().Rotate(vw), Vec3.Zero);
    }

    /// <summary>The launch argument's value ("runway" / "grass" / "water"), or null without one.</summary>
    public static string? FromArgs(string[] args)
    {
        int i = Array.IndexOf(args, ArgName);
        if (i < 0) return null;
        string v = i + 1 < args.Length ? args[i + 1] : "";
        return v == "grass" || v == "water" ? v : "runway";
    }
}
