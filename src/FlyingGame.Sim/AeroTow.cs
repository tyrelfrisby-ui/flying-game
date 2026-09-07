using FlyingGame.Core;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim;

/// <summary>
/// Aerotow coupling between a tug and a glider via an elastic rope with weak links (real ops:
/// 200 ft / 61 m polypropylene rope, weak link ~300 daN at the glider end, ~400 daN at the tug end,
/// tow ~60 kt). The rope only PULLS: when the attach-point separation exceeds the rope's natural
/// length it acts as a stiff spring-damper along the rope line, tension shared equally (Newton's
/// third law) onto both aircraft at their hooks. Slack rope = no force. Tension over either weak
/// link, or a release from either end, severs the connection.
///
/// The manager sets each aircraft's ExternalForceWorld/Point each step; the caller steps both
/// aircraft. Engine-agnostic — no Unity.
/// </summary>
public sealed class AeroTow
{
    public Aircraft Tug { get; }
    public Aircraft Glider { get; }
    public double RopeLengthM { get; }
    public double SpringNPerM { get; }        // rope elasticity (stiff — polypropylene stretches little)
    public double DampingNsPerM { get; }
    public double WeakLinkGliderN { get; }    // 300 daN
    public double WeakLinkTugN { get; }        // 400 daN

    /// <summary>Tug hook is at the tail; glider hook is at the nose. Body-frame offsets from each CG.</summary>
    public Vec3 TugHookBody { get; }
    public Vec3 GliderHookBody { get; }

    public bool Connected { get; private set; } = true;
    public double Tension { get; private set; }
    private double _filteredTension;  // ~0.12s low-pass: weak links break on SUSTAINED overload, not
                                      // single-step spikes (brief peaks are absorbed by rope stretch).
    public string? SeverReason { get; private set; }   // "released" | "weaklink-glider" | "weaklink-tug"

    public AeroTow(Aircraft tug, Aircraft glider,
        double ropeLengthM = 61.0, double springNPerM = 2000.0, double dampingNsPerM = 1200.0,
        double weakLinkGliderN = 2940.0, double weakLinkTugN = 3920.0,
        Vec3 tugHookBody = default, Vec3 gliderHookBody = default)
    {
        Tug = tug;
        Glider = glider;
        RopeLengthM = ropeLengthM;
        SpringNPerM = springNPerM;
        DampingNsPerM = dampingNsPerM;
        WeakLinkGliderN = weakLinkGliderN;
        WeakLinkTugN = weakLinkTugN;
        TugHookBody = tugHookBody.LengthSquared > 0 ? tugHookBody : new Vec3(-3.4, 0, 0);      // tug tail
        GliderHookBody = gliderHookBody.LengthSquared > 0 ? gliderHookBody : new Vec3(2.0, 0, 0); // glider nose
    }

    public Vec3 TugHookWorld => Tug.State.Position + Tug.State.Attitude.Rotate(TugHookBody);
    public Vec3 GliderHookWorld => Glider.State.Position + Glider.State.Attitude.Rotate(GliderHookBody);

    /// <summary>Release from the glider (or tug) end — the standard "pull the yellow knob" action.</summary>
    public void Release(string reason = "released")
    {
        Sever(reason);
    }

    /// <summary>Compute rope tension and apply it to both aircraft. Call before stepping them.</summary>
    public void Apply(double dt)
    {
        if (!Connected)
        {
            Tug.ExternalForceWorld = Vec3.Zero;
            Glider.ExternalForceWorld = Vec3.Zero;
            return;
        }

        Vec3 tugHook = TugHookWorld;
        Vec3 gliderHook = GliderHookWorld;
        Vec3 rope = tugHook - gliderHook;   // from glider hook toward tug hook
        double dist = rope.Length;
        if (dist < 1e-6)
        {
            return;
        }

        Vec3 dir = rope / dist;
        double stretch = dist - RopeLengthM;
        if (stretch <= 0.0)
        {
            Tension = 0.0;                   // slack rope pulls nothing
            Tug.ExternalForceWorld = Vec3.Zero;
            Glider.ExternalForceWorld = Vec3.Zero;
            return;
        }

        // Closing/opening rate along the rope for damping (relative velocity of the two hooks).
        Vec3 tugHookVel = Tug.State.Attitude.Rotate(Tug.State.Velocity)
            + Vec3.Cross(Tug.State.Attitude.Rotate(Tug.State.Rates), Tug.State.Attitude.Rotate(TugHookBody));
        Vec3 gliderHookVel = Glider.State.Attitude.Rotate(Glider.State.Velocity)
            + Vec3.Cross(Glider.State.Attitude.Rotate(Glider.State.Rates), Glider.State.Attitude.Rotate(GliderHookBody));
        double closingRate = Vec3.Dot(gliderHookVel - tugHookVel, dir); // + = separating (rope loading)

        double tension = SpringNPerM * stretch + DampingNsPerM * closingRate;
        tension = System.Math.Max(0.0, tension); // rope can't push
        Tension = tension;
        double k = 1.0 - System.Math.Exp(-dt / 0.12);
        _filteredTension += (tension - _filteredTension) * k;

        // Weak-link check on the SUSTAINED (filtered) tension — the weaker link governs.
        if (_filteredTension > WeakLinkGliderN && WeakLinkGliderN <= WeakLinkTugN)
        {
            Sever("weaklink-glider");
            return;
        }
        if (_filteredTension > WeakLinkTugN)
        {
            Sever("weaklink-tug");
            return;
        }

        // Glider is pulled TOWARD the tug (+dir); tug is pulled back toward the glider (-dir).
        Glider.ExternalForceWorld = dir * tension;
        Glider.ExternalForcePointBody = GliderHookBody;
        Tug.ExternalForceWorld = dir * (-tension);
        Tug.ExternalForcePointBody = TugHookBody;
    }

    private void Sever(string reason)
    {
        Connected = false;
        SeverReason = reason;
        Tension = 0.0;
        Tug.ExternalForceWorld = Vec3.Zero;
        Glider.ExternalForceWorld = Vec3.Zero;
    }
}
