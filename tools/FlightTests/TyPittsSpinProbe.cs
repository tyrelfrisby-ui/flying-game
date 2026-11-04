using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;
namespace FlyingGame.FlightTests;
public class TyPittsSpinProbe
{
    private readonly ITestOutputHelper _out; public TyPittsSpinProbe(ITestOutputHelper o) { _out = o; }
    static double AlphaDeg(RigidBodyState s){ Vec3 air = s.Velocity - s.Attitude.Conjugate().Rotate(Atmosphere.WindAtPosition(s.Position)); return Math.Atan2(air.Z, air.X)*57.3; }
    static double OmVert(RigidBodyState s){ Vec3 zB = s.Attitude.Conjugate().Rotate(new Vec3(0,0,1)); return s.Rates.X*zB.X+s.Rates.Y*zB.Y+s.Rates.Z*zB.Z; }
    [Fact]
    public void Run()
    {
        if (Environment.GetEnvironmentVariable("TYP") == null) return;
        var d = new DirectoryInfo(AppContext.BaseDirectory); while (!Directory.Exists(Path.Combine(d!.FullName,"configs","aircraft"))) d = d.Parent;
        string file = Path.Combine(d.FullName,"configs","aircraft","pitts-s2b-like.json");
        foreach (var (lever, label) in new[]{(1.0,"idle"),(0.2,"~40%pwr"),(-1.0,"fullpwr")})
        foreach (string rud in (Environment.GetEnvironmentVariable("RUD") ?? "-1").Split(','))
        foreach (string inert in (Environment.GetEnvironmentVariable("TYP_CASES") ?? "baseline,noGyro").Split(','))
        {
            AircraftConfig cfg = AircraftConfigLoader.LoadFromFile(file);
            Aircraft.SwirlFactor=0.2; if (inert=="noGyro") cfg.Propulsion!.PropInertia=0; if (inert=="ip5") cfg.Propulsion!.PropInertia=5.0; if (inert=="inertiaLane") { cfg.Propulsion!.PropInertia=5.0; cfg.Mass.Inertia.Ixx=401; cfg.Mass.Inertia.Iyy=1048; cfg.Mass.Inertia.Izz=1272; cfg.Mass.Inertia.Ixz=12; } if (inert=="noSlipstream") cfg.Propulsion!.SlipstreamK=0; if (inert=="SwirlOff") Aircraft.SwirlFactor=0;
            double stallDeg = StallAuthorityTests.StallDeg(cfg);
            double v = ThermalSpawn.MinSinkSpeed(cfg, 2500);
            var t0 = TrimSolver.SolveGliderTrim(cfg, v, 2500, idleProp: true);
            var att = new Quat(0, Math.Sin(t0.ThetaRad/2), 0, Math.Cos(t0.ThetaRad/2));
            var ac = new Aircraft(cfg, new RigidBodyState(new Vec3(0,0,-2500), att, new Vec3(v*Math.Cos(t0.AlphaRad),0,v*Math.Sin(t0.AlphaRad)), Vec3.Zero), new FlyingGame.Core.Aero.ControlDeflections(0,t0.ElevatorRad,0,0));
            double h = SimLoop.DefaultFixedDtSec;
            for (double t=0;t<25;t+=h){ ac.Step(new ControlInputs(0,-1,0,1.0),h); if (AlphaDeg(ac.State)>stallDeg+2) break; }
            var pro = new ControlInputs(0,-1,double.Parse(rud),lever);
            double net=0,lost=0,sa=0,sp=0,sr=0,amin=999,amax=-999,sv=0; int n=0;
            for (double t=0;t<45;t+=h)
            {
                double z0=ac.State.Position.Z; ac.Step(pro,h); var s=ac.State;
                if (t>=25){ double a=AlphaDeg(s); sa+=a; amin=Math.Min(amin,a); amax=Math.Max(amax,a); sp+=FormationPilot.Euler(s.Attitude).Item2*57.3; net+=OmVert(s)*h; lost+=s.Position.Z-z0; n++; }
                if (-s.Position.Z<600) ac.State=new RigidBodyState(new Vec3(s.Position.X,s.Position.Y,s.Position.Z-2500),s.Attitude,s.Velocity,s.Rates);
            }
            double turns=Math.Abs(net)/(2*Math.PI);
            _out.WriteLine($"{label,-9} rud {rud,2} {inert,-9} alpha {sa/n,5:F1} ({amin:F0}..{amax:F0})  pitch {sp/n,6:F1}  rate {Math.Abs(net)/20*57.3,5:F0} deg/s  {20/turns,4:F2} s/turn  sink {lost/20*196.85,6:F0} fpm  {lost*3.2808/turns,5:F0} ft/turn");
        }
    }
}
