using FlyingGame.Core;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim;

/// <summary>
/// Formation wingman (owner 2026-10-01: 4-ships of P-51s, 3-ships of DC-3s in the combat zone). Holds a slot defined in the
/// LEADER's track frame — <see cref="Slot"/> = (metres ahead, metres right, metres below) — by flying a speed for the
/// along-track error, a heading for the cross-track error (leader's bank fed forward) and a flight-path angle for the
/// height error (leader's climb angle fed forward). Works through the same stick/throttle the drones use.
/// </summary>
public sealed class FormationPilot
{
    public Vec3 Slot;
    public double MaxBankDeg = 55;

    public FormationPilot(Vec3 slot) { Slot = slot; }

    private static double Wrap(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; }

    public static (double roll, double pitch, double psi) Euler(Quat q) => (
        Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)),
        Math.Asin(Math.Clamp(2 * (q.W * q.Y - q.Z * q.X), -1, 1)),
        Math.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)));

    /// <summary>World position of this wingman's slot behind <paramref name="leader"/>.</summary>
    public Vec3 SlotPosition(RigidBodyState leader)
    {
        Vec3 vl = leader.Attitude.Rotate(leader.Velocity);
        double psiL = Math.Atan2(vl.Y, vl.X);
        var fwd = new Vec3(Math.Cos(psiL), Math.Sin(psiL), 0);
        var right = new Vec3(-Math.Sin(psiL), Math.Cos(psiL), 0);
        return leader.Position + fwd * Slot.X + right * Slot.Y + new Vec3(0, 0, Slot.Z);
    }

    public ControlInputs Update(Aircraft self, Aircraft leader, double dt)
    {
        RigidBodyState s = self.State, L = leader.State;
        (double roll, double pitch, double psi) = Euler(s.Attitude);
        (double rollL, _, _) = Euler(L.Attitude);
        Vec3 vl = L.Attitude.Rotate(L.Velocity), vw = s.Attitude.Rotate(s.Velocity);
        double vlH = Math.Sqrt(vl.X * vl.X + vl.Y * vl.Y), vH = Math.Sqrt(vw.X * vw.X + vw.Y * vw.Y);
        double psiL = Math.Atan2(vl.Y, vl.X);
        var fwd = new Vec3(Math.Cos(psiL), Math.Sin(psiL), 0);
        var right = new Vec3(-Math.Sin(psiL), Math.Cos(psiL), 0);
        Vec3 e = SlotPosition(L) - s.Position;
        double ea = e.X * fwd.X + e.Y * fwd.Y, ec = e.X * right.X + e.Y * right.Y, eh = -e.Z;

        // The SLOT's own velocity: the leader's plus his turn rate × the slot offset — in a turn the outside slot moves
        // faster than the leader and the inside one slower (without this every wingman settles off his slot).
        double wz = L.Attitude.Rotate(L.Rates).Z;   // turn rate about world down (+ = turning right)
        double rx = fwd.X * Slot.X + right.X * Slot.Y, ry = fwd.Y * Slot.X + right.Y * Slot.Y;
        double svx = vl.X - wz * ry, svy = vl.Y + wz * rx;
        double slotSpeed = Math.Sqrt(svx * svx + svy * svy), slotTrack = Math.Atan2(svy, svx);

        // Speed: the slot's, plus a closure proportional to the along-track error (capped so the join-up is calm).
        double along = vw.X * fwd.X + vw.Y * fwd.Y;
        double vCmd = slotSpeed + Math.Clamp(0.10 * ea, -10, 12);
        double thr = Math.Clamp(leader.Throttle01 + 0.06 * (vCmd - along) + 0.02 * (slotSpeed - vlH), 0.05, 1.0);

        // Heading: the slot's track, turned toward the slot line; the leader's bank fed forward.
        double psiCmd = slotTrack + Math.Clamp(Math.Atan2(ec, 150.0), -0.6, 0.6);
        double maxBank = MaxBankDeg * Math.PI / 180;
        double bankCmd = Math.Clamp(2.0 * Wrap(psiCmd - psi) + 0.8 * rollL, -maxBank, maxBank);
        double ail = Math.Clamp((bankCmd - roll) * 1.5 - s.Rates.X * 0.25, -1, 1);

        // Height: the leader's flight-path angle plus a correction; extra pull in a bank.
        double gammaL = Math.Atan2(-vl.Z, Math.Max(1, vlH)), gamma = Math.Atan2(-vw.Z, Math.Max(1, vH));
        double gammaCmd = gammaL + Math.Clamp(eh * 0.012, -0.15, 0.15);
        double ele = Math.Clamp(-(gammaCmd - gamma) * 3.0 - Math.Abs(roll) * 0.45 + s.Rates.Y * 0.5, -0.7, 0.5);

        double rud = Math.Clamp(-s.Velocity.Y * 0.02, -0.3, 0.3);
        return new ControlInputs(ail, ele, rud, 1.0 - 2.0 * thr);
    }
}
