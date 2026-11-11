using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// Diagnostic (AFTCAMBER=1). Ty 2026-11-11: "Fuselage crossflow should trigger the sideslip camber on the aft
/// fuselage and produce a pronounced negative pitching moment on the pitts and extra which should result in flatter
/// inverted spins than upright."
///
/// The term he describes ALREADY EXISTS - CrossflowConfig.SectionCamberCl, applied in AeroModel.ApplyFuselage - so
/// the first question is not how to build it but how AWAKE it is in a developed spin. This measures, at the settled
/// upright and inverted flat spin: the local lateral velocity at the camber station (and how much of it is the
/// rotation term r*(x-x_cg) rather than body sideslip), the force and pitching moment the camber makes there, and
/// the cowl's opposing share - against the total aerodynamic pitching moment.
///
/// It also prints the CROSSFLOW DYNAMIC PRESSURE the model uses against the one NACA TN 4176 / NASA TR R-29
/// publishes for the same effect, (C_Y)_nonpotential = c_y'(S/A)(cos^2 a sin^2 b + sin^2 a): the model's camber law
/// uses only the cos^2 a sin^2 b half. docs/aircraft/CM-BETA.md section 6 measured that gap at 2.41x at a -40 / b 45.
/// </summary>
[Collection("WorldTerrainActive")]
public class AftCamberSpinProbe
{
    private readonly ITestOutputHelper _out;
    public AftCamberSpinProbe(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void HowAwakeIsTheCamberInTheSpin()
    {
        if (Environment.GetEnvironmentVariable("AFTCAMBER") == null) return;
        foreach (string id in (Environment.GetEnvironmentVariable("AFTCAMBER_IDS") ?? "pitts-s2b-like,extra-300-like").Split(','))
        {
            var cfg = SpoilerOnsetSpinSweep.Load(id.Trim());
            var cf = cfg.Fuselage.Crossflow;
            _out.WriteLine($"=== {cfg.Id}: sectionCamberCl {cf.SectionCamberCl:F2} area {cf.SectionCamberArea:F2} x {cf.SectionCamberCenterX:F2}"
                         + $" | cowlCamberCl {cf.CowlCamberCl:F2} area {cf.CowlCamberArea:F2} x {cf.CowlCamberCenterX:F2} | cg.x {cfg.Mass.CgVec().X:F2} ===");
            foreach (bool inverted in new[] { false, true })
            {
                var (st, ac) = SpinBalanceProbe.Developed(cfg, rudder: inverted ? 1.0 : -1.0, lever: -1.0, inverted: inverted, aileron: 1.0);
                // Average over one more second of the settled spin, with the force overlay on.
                ac.CaptureForces = true;
                var cg = cfg.Mass.CgVec();
                double h = SimLoop.DefaultFixedDtSec;
                int n = 0;
                double vDeck = 0, vDeckSlip = 0, mDeck = 0, mCowl = 0, mTot = 0, qModel = 0, qPub = 0, bet = 0, alf = 0;
                for (double t = 0; t < 2.0; t += h)
                {
                    ac.Step(new ControlInputs(1.0, inverted ? 1 : -1, inverted ? 1.0 : -1.0, -1.0), h);
                    var s = ac.State; var v = s.Velocity; var om = s.Rates;
                    double sp = Math.Max(1e-6, v.Length);
                    double a = Math.Atan2(v.Z, v.X), b = Math.Asin(Math.Clamp(v.Y / sp, -1, 1));
                    alf += a * 57.3; bet += b * 57.3;
                    double vl = v.Y + om.Z * (cf.SectionCamberCenterX - cg.X);
                    vDeck += vl; vDeckSlip += v.Y;
                    qModel += Math.Cos(a) * Math.Cos(a) * Math.Sin(b) * Math.Sin(b);
                    qPub += Math.Cos(a) * Math.Cos(a) * Math.Sin(b) * Math.Sin(b) + Math.Sin(a) * Math.Sin(a);
                    foreach (var f in ac.LastForces)
                    {
                        double my = Vec3.Cross(f.PosBody - cg, f.ForceBody).Y + f.MomentBody.Y;
                        if (f.Kind is "lift" or "drag" or "fuselage" or "moment" or "thrust" or "gyro") mTot += my;
                        if (f.Kind == "fuselage")
                        {
                            if (Math.Abs(f.PosBody.X - cf.SectionCamberCenterX) < 1e-6) mDeck += my;
                            if (Math.Abs(f.PosBody.X - cf.CowlCamberCenterX) < 1e-6) mCowl += my;
                        }
                    }
                    n++;
                }
                ac.CaptureForces = false;
                _out.WriteLine($"  {(inverted ? "INVERTED" : "UPRIGHT ")} flat: alpha {st.Alpha,6:F1} ({st.AlphaMin:F0}..{st.AlphaMax:F0})  pitch {st.Pitch,6:F1}  beta {bet / n,6:F1}  {st.SecPerTurn:F2} s/turn  {st.Tas:F0} m/s TAS");
                _out.WriteLine($"           camber station local v {vDeck / n,7:F2} m/s  (of which body sideslip v_y {vDeckSlip / n,7:F2}, rotation r*arm {(vDeck - vDeckSlip) / n,7:F2})");
                _out.WriteLine($"           turtledeck camber pitching moment {mDeck / n,9:F0} N.m   cowl {mCowl / n,9:F0} N.m   TOTAL aero pitch {mTot / n,9:F0} N.m   camber share {(mTot != 0 ? 100 * (mDeck + mCowl) / mTot : 0),5:F1} %");
                _out.WriteLine($"           crossflow q factor: model cos^2a sin^2b = {qModel / n,7:F4}   published (+ sin^2 a) = {qPub / n,7:F4}   ratio {(qModel != 0 ? qPub / qModel : 0),7:F1}x");
            }
        }
    }
}
