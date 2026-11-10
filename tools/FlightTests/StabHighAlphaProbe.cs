using System;
using System.IO;
using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// WHAT THE STABILISER GIVES AT SPIN ALPHA (STAB_HI=1). Ty 2026-11-09: "spins to the right are about right it is just
/// the left spin that has too much flattening especially at idle it must be the stabilizer at high angle of attack."
/// The gyro is nose-UP in a left spin and nose-DOWN in a right one, so only the left spin asks the tail to win; if
/// the tail is short at high alpha, that is exactly the asymmetry he sees. Compared against a flat plate, which is
/// what a stabiliser at 90 deg IS (Hoerner: a flat plate normal to the flow carries Cn about 1.8-2.0).
/// </summary>
public class StabHighAlphaProbe
{
    private readonly ITestOutputHelper _o;
    public StabHighAlphaProbe(ITestOutputHelper o) { _o = o; }

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("STAB_HI") == null) return;
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(d!.FullName, "configs", "aircraft"))) d = d.Parent;
        var c = AircraftConfigLoader.LoadFromFile(Path.Combine(d.FullName, "configs", "aircraft", "pitts-s2b-like.json"));
        if (Environment.GetEnvironmentVariable("NO_LESTALL") != null) { c.SpinAero ??= new SpinAeroConfig(); c.SpinAero.LeStallHysteresisDeg = 0; }
        var tables = Aircraft.BuildAirfoilTables(c);

        double sTail = 0, armSum = 0, n = 0;
        foreach (SurfaceConfig sf in c.Surfaces)
        {
            string i = sf.Id.ToLowerInvariant();
            if ((!i.Contains("stab") && !i.Contains("elev")) || i.Contains("vstab")) continue;
            foreach (StripConfig st in sf.Strips) { sTail += st.Area; armSum += st.Pos[0]; n++; }
        }
        double arm = Math.Abs(armSum / n - c.Mass.Cg[0]);
        _o.WriteLine($"Pitts horizontal tail: area {sTail:F3} m2, mean arm {arm:F2} m behind the CG");
        _o.WriteLine("");
        _o.WriteLine("  Looking for a CLIFF in the tail's resisting force or its pitch damping with alpha (Ty 2026-11-09).");
        _o.WriteLine("  alpha | tail Cn | tail M (N.m) | d(tailM)/dalpha | tail DAMPING dM/dq (N.m per rad/s)");
        double v = 25.0, rho = 1.1, q = 0.5 * rho * v * v;
        double prevM = double.NaN, prevA = double.NaN;
        foreach (double aDeg in new[] { 0.0, 5.0, 10.0, 12.0, 14.0, 16.0, 18.0, 20.0, 25.0, 30.0, 35.0, 40.0, 45.0, 50.0, 55.0, 60.0, 70.0, 80.0, 90.0 })
        {
            double a = aDeg * Math.PI / 180;
            var vel = new Vec3(v * Math.Cos(a), 0, v * Math.Sin(a));
            // the tail's own share: whole aeroplane less the same aeroplane with the tail masked out
            var mask = new bool[c.Surfaces.Count];
            for (int s = 0; s < c.Surfaces.Count; s++)
            {
                string i = c.Surfaces[s].Id.ToLowerInvariant();
                mask[s] = !((i.Contains("stab") || i.Contains("elev")) && !i.Contains("vstab"));
            }
            double mAll = AeroModel.Compute(c, tables, vel, Vec3.Zero, Vec3.Zero, rho, ControlDeflections.Neutral).Moment.Y;
            double mNoTail = AeroModel.Compute(c, tables, vel, Vec3.Zero, Vec3.Zero, rho, ControlDeflections.Neutral, -1.0, null, 0, 0, mask).Moment.Y;
            double mTail = mAll - mNoTail;
            double cnModel = Math.Abs(mTail) / (q * sTail * arm);
            // Pitch damping from the tail alone: perturb q and difference, with and without the tail.
            const double dq = 0.3;
            double dAll = (AeroModel.Compute(c, tables, vel, new Vec3(0, dq, 0), Vec3.Zero, rho, ControlDeflections.Neutral).Moment.Y
                         - AeroModel.Compute(c, tables, vel, new Vec3(0, -dq, 0), Vec3.Zero, rho, ControlDeflections.Neutral).Moment.Y) / (2 * dq);
            double dNo = (AeroModel.Compute(c, tables, vel, new Vec3(0, dq, 0), Vec3.Zero, rho, ControlDeflections.Neutral, -1.0, null, 0, 0, mask).Moment.Y
                        - AeroModel.Compute(c, tables, vel, new Vec3(0, -dq, 0), Vec3.Zero, rho, ControlDeflections.Neutral, -1.0, null, 0, 0, mask).Moment.Y) / (2 * dq);
            double slope = double.IsNaN(prevM) ? double.NaN : (mTail - prevM) / (aDeg - prevA);
            _o.WriteLine($"  {aDeg,5:F0} | {cnModel,7:F3} | {mTail,12:F0} | {slope,15:F0} | {dAll - dNo,18:F0}");
            prevM = mTail; prevA = aDeg;
        }
    }
}
