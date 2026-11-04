using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim;

/// <summary>
/// A scripted, reproducible crash (Ty 2026-11-03: the Pitts on the grass, pieces losing their paint and sinking): the
/// state DebrisGroundTests crash from, and the one the game starts in with the launch argument
/// <c>-crashtest runway</c> or <c>-crashtest grass</c> (FlightSimDriver), so the phone build shows the same crash.
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

    /// <summary>The launch argument's value ("runway" / "grass"), or null without one.</summary>
    public static string? FromArgs(string[] args)
    {
        int i = Array.IndexOf(args, ArgName);
        if (i < 0) return null;
        return i + 1 < args.Length && args[i + 1] == "grass" ? "grass" : "runway";
    }
}
