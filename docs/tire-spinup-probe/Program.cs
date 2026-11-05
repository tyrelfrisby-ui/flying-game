// Tire spin-up probe (analysis only; does not change the sim). Drops each aircraft's MAIN gear (real config
// spring/damper through the real LandingGear.Compute) onto flat ground at a given sink rate with 50 kt ground
// speed and wing lift = LiftFrac*W, then integrates a wheel that the sim does NOT have:
//   I dω/dt = μ·N(t)·r   until ω·r = V   (spin-up under kinetic friction; rolling/bearing drag neglected)
// μ = TireMu × LandingGear.SurfaceGrip(surface) (the code's own friction cap). I is ASSUMED (not in the code).
using System; using System.Collections.Generic; using System.IO; using System.Linq;
using FlyingGame.Core; using FlyingGame.Core.DataContracts; using FlyingGame.Core.MathTypes;
static class P {
  static void Main(string[] a) {
    string dir = a[0]; double liftFrac = a.Length > 1 ? double.Parse(a[1]) : 0.9;
    // id, wheel+tyre assembly mass per main wheel (kg, ASSUMED), wheels per main leg
    var fleet = new (string id, double mWheel, int perLeg)[] {
      ("j3-cub-like",7.5,1),("pa18-cub-like",7.5,1),("pitts-s2b-like",4.5,1),("extra-300-like",4.5,1),("c172-like",6.5,1),
      ("decathlon-8kcab-like",6.5,1),("great-lakes-like",6.0,1),("glider-2-33-like",4.5,1),("boeing-737-like",150,2) };
    double V = 50 * 0.514444; var sinks = new[]{1.0,3.0,6.0,10.0};
    var surfs = new[]{WorldTerrain.Surface.Paved, WorldTerrain.Surface.Gravel, WorldTerrain.Surface.Grass, WorldTerrain.Surface.Rough};
    Console.WriteLine($"liftFrac={liftFrac}  V=50kt  groundH(0,0)={WorldTerrain.WheelGroundHeightAt(0,0):F3}");
    Console.WriteLine("id,r_m,I_kgm2,TireMu,k_N/m,c_Ns/m,mass_kg,sink_fps,N_peak_per_wheel_N,N_at_spun_per_wheel,surface,mu,t_spinup_ms,dist_m,impulse_Ns_total,dV_ms,pitchMomPeak_kNm");
    foreach (var f in fleet) {
      AircraftConfig c = AircraftConfigLoader.LoadFromFile(Path.Combine(dir, f.id + ".json"));
      // mains only: not tailwheel, not skids, laterally off-centre OR the single centre main of the 2-33
      var mains = c.Gear.Where(g => !g.IsTailwheel && g.GearType != "nose-skid" && g.GearType != "tip-skid" && g.GearType != "float-keel"
                                    && !(g.IsSteerable) && g.Pos[2] > 0).ToList();
      if (f.id.StartsWith("c172") ) mains = c.Gear.Where(g => Math.Abs(g.Pos[1]) > 0.3).ToList();
      if (f.id.StartsWith("boeing")) mains = c.Gear.Where(g => Math.Abs(g.Pos[1]) > 0.3).ToList();
      c.Gear = mains;
      var g0 = mains[0]; double r = LandingGear.TireRadiusOf(c, g0);
      double I = 0.65 * f.mWheel * r * r;   // tyre mass mostly at the tread: ~0.6-0.7 m r^2
      double m = c.Mass.MassKg, W = m * 9.81; double zGear = (g0.PosVec() - c.Mass.CgVec()).Z; double cgHeight = zGear;
      foreach (double sinkFps in sinks) {
        double sink = sinkFps * 0.3048;
        // simulate the vertical drop with the real gear code; record per-wheel N(t)
        double dt = 1e-4; var Ns = new List<double>();
        var st = new RigidBodyState(new Vec3(0,0,-zGear - WorldTerrain.WheelGroundHeightAt(0,0) - 1e-6), Quat.Identity, new Vec3(V,0,sink), Vec3.Zero);
        double z = st.Position.Z, w = sink; 
        for (int i = 0; i < 20000; i++) {
          var s = new RigidBodyState(new Vec3(0,0,z), Quat.Identity, new Vec3(V,0,w), Vec3.Zero);
          var (F, _) = LandingGear.Compute(c, s, 0, 0);
          double Nwheel = -F.Z / mains.Count / f.perLeg;  // upward per wheel (inc. negligible roll drag z-comp)
          Ns.Add(Math.Max(0, Nwheel));
          double az = (W - liftFrac * W + F.Z) / m;  // NED: weight down, lift up, gear F.Z negative = up
          w += az * dt; z += w * dt;
        }
        foreach (var sf in surfs) {
          double mu = g0.TireMu * LandingGear.SurfaceGrip(sf);
          double om = 0, t = 0, dist = 0, Npk = 0, Nend = 0; int k = 0;
          for (; k < Ns.Count; k++) {
            double N = Ns[k]; Npk = Math.Max(Npk, N);
            om += mu * N * r / I * dt; t += dt; dist += V * dt;
            if (om * r >= V) { Nend = N; break; }
          }
          bool done = k < Ns.Count;
          double impulse = I * V / (r * r) * mains.Count * f.perLeg; // Σ μN dt per wheel = I V / r²
          double dV = impulse / m;
          double pitch = mu * Npk * mains.Count * f.perLeg * cgHeight / 1000;
          Console.WriteLine($"{f.id},{r:F3},{I:F3},{g0.TireMu},{g0.SpringN},{g0.DampNs},{m},{sinkFps},{Ns.Max():F0},{Nend:F0},{sf},{mu:F2},{(done? (t*1000).ToString("F1") : ">2000")},{dist:F2},{impulse:F0},{dV:F3},{pitch:F1}");
        }
      }
    }
  }
}
