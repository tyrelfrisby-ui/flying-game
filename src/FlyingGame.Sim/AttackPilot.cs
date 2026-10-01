using FlyingGame.Core;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim;

/// <summary>
/// A target P-51 that fights back (owner 2026-10-01): MEDIUM skill, not an ace. Lead pursuit with an imperfect lead
/// (<see cref="LeadFactor"/>) and a slowly wandering aim error, a human reaction lag on the stick, a moderate pull limit
/// (no 6 g knife fights), short bursts only when roughly on target and in range, a break-off when it gets too close,
/// and a hard floor. Returns the stick/throttle plus whether the trigger is pressed this step.
/// </summary>
public sealed class AttackPilot
{
    public double LeadFactor = 0.75;          // fraction of the true lead it pulls (an ace: ~1.0)
    public double AimWanderRad = 0.007;       // ~7 mrad wandering aim error, ~2 m at 300 m (an ace: ~3)
    public double ReactionSec = 0.35;         // stick lag
    public double MaxPull = 0.55;             // of full aft elevator (an ace: 0.8+)
    public double FireRangeM = 600, FireConeRad = 0.025;
    public double FloorAglM = 200, BreakOffM = 110;
    public double BulletSpeedMs = 850;

    public bool Firing { get; private set; }
    /// <summary>Diagnostics: range and the angle between the nose and the (perceived) aim point, last update.</summary>
    public double LastRangeM { get; private set; }
    public double LastOffNoseRad { get; private set; }
    public string LastMode { get; private set; } = "";
    public int Bursts { get; private set; }

    private readonly Random _rng;
    private double _iPitch, _iLat;   // tracking trim (integral), like a pilot easing the stick to hold the pipper
    private double _ail, _ele, _t, _burstT, _coolT, _breakT, _breakDir = 1;
    private double _wanderA, _wanderB, _wanderTa, _wanderTb;

    public AttackPilot(int seed) { _rng = new Random(seed); _coolT = 1.0 + _rng.NextDouble(); }

    public ControlInputs Update(Aircraft self, RigidBodyState target, double dt)
    {
        RigidBodyState s = self.State;
        Quat q = s.Attitude;
        (double roll, double pitch, _) = FormationPilot.Euler(q);
        _t += dt;
        Vec3 vw = q.Rotate(s.Velocity);
        Vec3 tv = target.Attitude.Rotate(target.Velocity);
        Vec3 d = target.Position - s.Position;
        double dist = Math.Max(1, d.Length);
        double agl = -s.Position.Z - WorldTerrain.GroundHeightAt(s.Position.X, s.Position.Y);

        // Wandering aim error: two slow random walks (perpendicular directions), re-targeted every ~1.5 s.
        if (_t > _wanderTa) { _wanderTa = _t + 1 + _rng.NextDouble(); _wanderB = (_rng.NextDouble() * 2 - 1) * AimWanderRad; }
        if (_t > _wanderTb) { _wanderTb = _t + 1 + _rng.NextDouble(); _wanderA += ((_rng.NextDouble() * 2 - 1) * AimWanderRad - _wanderA) * 0.7; }
        _wanderA += (_wanderB - _wanderA) * Math.Min(1, dt / 1.2);

        double tof = dist / Math.Max(200, BulletSpeedMs + (vw.X * d.X + vw.Y * d.Y + vw.Z * d.Z) / dist);
        Vec3 aim = target.Position + tv * (tof * LeadFactor);
        Vec3 to = aim - s.Position;
        Vec3 tb = q.Conjugate().Rotate(to);          // body axes: x ahead, y right, z down
        tb = new Vec3(tb.X, tb.Y + _wanderA * to.Length, tb.Z + _wanderB * to.Length);
        double tl = Math.Max(1e-6, tb.Length);
        double offNose = Math.Atan2(Math.Sqrt(tb.Y * tb.Y + tb.Z * tb.Z), tb.X);

        LastRangeM = dist; LastOffNoseRad = offNose;
        double closure = -((tv.X - vw.X) * d.X + (tv.Y - vw.Y) * d.Y + (tv.Z - vw.Z) * d.Z) / dist;   // m/s, + = closing
        double ailCmd, eleCmd;
        if (agl < FloorAglM || (agl < FloorAglM * 2 && vw.Z > 25))
        {
            // Too low: wings level and climb, whatever the target is doing.
            ailCmd = Math.Clamp(-roll * 1.5 - s.Rates.X * 0.3, -1, 1);
            eleCmd = Math.Abs(roll) < 0.6 ? -MaxPull : 0.0;
            _breakT = 0; LastMode = "floor";
        }
        else if (_breakT > 0 || dist < BreakOffM || (closure > 5 && dist / closure < 2.0 && dist < 400))
        {
            // Overshooting: break away hard for a few seconds, then come round again.
            if (_breakT <= 0) { _breakT = 2.5 + _rng.NextDouble() * 1.5; _breakDir = tb.Y > 0 ? -1 : 1; }
            _breakT -= dt;
            ailCmd = Math.Clamp((1.1 * _breakDir - roll) * 1.4, -1, 1);
            eleCmd = -MaxPull; LastMode = "break";
        }
        else
        {
            double pullErr = Math.Atan2(-tb.Z, tb.X);
            if (offNose > 0.25)
            {
                // Far off the nose: roll the aim point into the lift plane, then pull it round.
                double rollErr = Math.Atan2(tb.Y, -tb.Z);
                ailCmd = Math.Clamp(rollErr * 1.2 - s.Rates.X * 0.2, -1, 1);
                eleCmd = offNose > 1.5 ? -MaxPull : Math.Clamp(-pullErr * 2.5 + s.Rates.Y * 0.3, -MaxPull, 0.4);
            }
            else
            {
                // Tracking: a bank toward the aim point (up to 45°) and pitch onto it — no rolling back and forth.
                double lat = Math.Atan2(tb.Y, tb.X);
                _iPitch = Math.Clamp(_iPitch + pullErr * dt * 2.0, -0.3, 0.3);
                _iLat = Math.Clamp(_iLat + lat * dt * 1.0, -0.3, 0.3);
                double bankCmd = Math.Clamp(lat * 4.0 + _iLat, -0.8, 0.8);
                ailCmd = Math.Clamp((bankCmd - roll) * 1.5 - s.Rates.X * 0.25, -1, 1);
                eleCmd = Math.Clamp(-pullErr * 4.0 - _iPitch - Math.Abs(roll) * 0.35 + s.Rates.Y * 0.4, -MaxPull, 0.4);
            }
            LastMode = "pursue";
        }
        double k = Math.Min(1, dt / ReactionSec);
        _ail += (ailCmd - _ail) * k;
        _ele += (eleCmd - _ele) * k;

        // Trigger: short bursts when in range and close to the aim point; a pause between them.
        // He fires when HE thinks he is on (his own wandering aim), so his bursts miss by that error.
        bool solution = dist < FireRangeM && offNose < FireConeRad && _breakT <= 0;
        if (_burstT > 0) { _burstT -= dt; if (_burstT <= 0) _coolT = 1.5 + 1.5 * _rng.NextDouble(); }
        else if (_coolT > 0) _coolT -= dt;
        else if (solution) { _burstT = 0.8 + 0.8 * _rng.NextDouble(); Bursts++; }
        Firing = _burstT > 0;

        // Power: once settled behind the target (nose within ~15°), close at a steady overtake that shrinks with range
        // (~55 m/s at 2 km, ~15 m/s at 300 m) instead of ramming; anywhere else — turning, re-attacking — full power.
        double thr = 1.0;
        if (offNose < 0.25 && dist < 2000) thr = Math.Clamp(0.55 + 0.04 * ((8 + 0.025 * dist) - closure), 0.0, 1.0);
        double rud = Math.Clamp(-s.Velocity.Y * 0.02, -0.4, 0.4);
        return new ControlInputs(Math.Clamp(_ail, -1, 1), Math.Clamp(_ele, -0.8, 0.5), rud, 1.0 - 2.0 * thr);
    }
}
