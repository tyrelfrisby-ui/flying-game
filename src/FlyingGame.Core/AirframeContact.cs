using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>The airframe parts a contact point belongs to — what breaks off when that point hits hard.</summary>
public enum AirframeComponent { Fuselage, Nose, WingLeft, WingRight, TailHorizontal, TailVertical, NacelleLeft, NacelleRight, Propeller }

/// <summary>A hard point on the airframe (body frame, same origin as the config) that must not pass through
/// the ground or a solid: wing tips, stab tips, fin top, nose, tail cone, cabin top, nacelles.</summary>
public sealed class ContactPoint
{
    public Vec3 Body;
    public AirframeComponent Component;
    public double BreakSpeedMs;           // normal impact speed that snaps the component off (0 = unbreakable)
    public bool Touching;                 // contact state last step (for impact detection)
    public string Name = "";
}

/// <summary>Solid boxes in the world (buildings, bridge decks, landmark legs) that the airframe collides with.
/// Axis-aligned in the sim frame: centre (x, y), half-sizes, bottom/top heights (m, up).</summary>
public static class WorldSolids
{
    public readonly struct Box
    {
        public readonly double Cx, Cy, Hx, Hy, Bottom, Top;
        public Box(double cx, double cy, double hx, double hy, double bottom, double top) { Cx = cx; Cy = cy; Hx = hx; Hy = hy; Bottom = bottom; Top = top; }
    }
    public static readonly System.Collections.Generic.List<Box> Boxes = new();

    /// <summary>If (x, y, up) is inside a box, the outward push (world, z DOWN) along the least-penetration axis and
    /// its depth; null outside every box.</summary>
    public static (Vec3 normalNed, double depth)? Penetration(double x, double y, double up)
    {
        foreach (Box b in Boxes)
        {
            double dx = x - b.Cx, dy = y - b.Cy;
            if (System.Math.Abs(dx) > b.Hx || System.Math.Abs(dy) > b.Hy || up < b.Bottom || up > b.Top) continue;
            double px = b.Hx - System.Math.Abs(dx), py = b.Hy - System.Math.Abs(dy), pTop = b.Top - up, pBot = up - b.Bottom;
            double m = System.Math.Min(System.Math.Min(px, py), System.Math.Min(pTop, pBot));
            if (m == pTop) return (new Vec3(0, 0, -1), pTop);
            if (m == px) return (new Vec3(System.Math.Sign(dx), 0, 0), px);
            if (m == py) return (new Vec3(0, System.Math.Sign(dy), 0), py);
            return (new Vec3(0, 0, 1), pBot);
        }
        return null;
    }
}

/// <summary>
/// Airframe-to-ground (and airframe-to-solid) contact: every hard point is a stiff spring-damper against the
/// surface with Coulomb friction, so an aircraft on its back rests on its fin and wing tips instead of
/// hanging from its tyres. A point that ARRIVES fast enough snaps its component off: the caller drops the
/// component's aero (no mass/inertia bookkeeping — owner's call) and removes its contact points.
/// </summary>
public static class AirframeContact
{
    public const double SpringNPerM = 150000.0, DampNsPerM = 9000.0, FrictionMu = 0.7;

    /// <summary>Derive the hard points from the config geometry.</summary>
    public static List<ContactPoint> BuildPoints(AircraftConfig c)
    {
        var pts = new List<ContactPoint>();
        double rBody = c.Fuselage.Crossflow?.BodyRadiusM > 0 ? c.Fuselage.Crossflow.BodyRadiusM : 0.5;
        double len = c.Fuselage.Crossflow?.LengthM > 0 ? c.Fuselage.Crossflow.LengthM : 7.0;
        double minTe = double.MaxValue, maxLe = double.MinValue;
        foreach (SurfaceConfig sf in c.Surfaces)
        {
            string id = sf.Id.ToLowerInvariant();
            bool vertical = id.Contains("vstab") || id.StartsWith("rudder") || id.Contains("fin");
            bool isWing = id.Contains("wing") && !id.Contains("aileron");
            bool isStab = id == "hstab";
            bool isFin = id == "vstab";
            foreach (StripConfig st in sf.Strips)
            {
                minTe = System.Math.Min(minTe, st.Pos[0] - 0.75 * st.Chord);
                maxLe = System.Math.Max(maxLe, st.Pos[0] + 0.25 * st.Chord);
            }
            if (isWing)
            {
                StripConfig l = sf.Strips[0], r = sf.Strips[0];
                foreach (StripConfig st in sf.Strips) { if (st.Pos[1] < l.Pos[1]) l = st; if (st.Pos[1] > r.Pos[1]) r = st; }
                // Wing root skin (the top of a high wing is what a flipped aircraft rests on; the bottom of a low
                // wing is what a belly landing rides on): one point each side of the centreline.
                StripConfig root = sf.Strips[0];
                foreach (StripConfig st in sf.Strips) if (System.Math.Abs(st.Pos[1]) < System.Math.Abs(root.Pos[1])) root = st;
                double skin = root.Pos[2] < 0 ? root.Pos[2] - 0.07 * root.Chord : root.Pos[2] + 0.07 * root.Chord;
                pts.Add(new ContactPoint { Body = new Vec3(root.Pos[0], -0.8, skin), Component = AirframeComponent.Fuselage, BreakSpeedMs = 0, Name = "wing-root-L" });
                pts.Add(new ContactPoint { Body = new Vec3(root.Pos[0], 0.8, skin), Component = AirframeComponent.Fuselage, BreakSpeedMs = 0, Name = "wing-root-R" });
                foreach ((StripConfig st, AirframeComponent comp, double sign) in new[] { (l, AirframeComponent.WingLeft, -1.0), (r, AirframeComponent.WingRight, 1.0) })
                {
                    double w = st.Chord > 1e-6 ? st.Area / st.Chord : 0.3;
                    double y = st.Pos[1] + sign * w * 0.5;
                    double z = st.Pos[2] - System.Math.Abs(y) * System.Math.Tan(st.DihedralRad);
                    pts.Add(new ContactPoint { Body = new Vec3(st.Pos[0], y, z), Component = comp, BreakSpeedMs = 4.5, Name = comp + ":" + sf.Id });
                }
            }
            if (isStab)
            {
                StripConfig l = sf.Strips[0], r = sf.Strips[0];
                foreach (StripConfig st in sf.Strips) { if (st.Pos[1] < l.Pos[1]) l = st; if (st.Pos[1] > r.Pos[1]) r = st; }
                pts.Add(new ContactPoint { Body = new Vec3(l.Pos[0], l.Pos[1] - 0.2, l.Pos[2]), Component = AirframeComponent.TailHorizontal, BreakSpeedMs = 5.0, Name = "stab-L" });
                pts.Add(new ContactPoint { Body = new Vec3(r.Pos[0], r.Pos[1] + 0.2, r.Pos[2]), Component = AirframeComponent.TailHorizontal, BreakSpeedMs = 5.0, Name = "stab-R" });
            }
            if (isFin && sf.Strips.Count > 0)
            {
                StripConfig top = sf.Strips[0];
                foreach (StripConfig st in sf.Strips) if (st.Pos[2] < top.Pos[2]) top = st;
                double w = top.Chord > 1e-6 ? top.Area / top.Chord : 0.3;
                double topZ = top.Pos[2] - w * 0.5;
                if (topZ > -0.2) topZ = -0.2; // a fin that sits below the axis (low rudder): still give it a top
                pts.Add(new ContactPoint { Body = new Vec3(top.Pos[0], 0, topZ), Component = AirframeComponent.TailVertical, BreakSpeedMs = 5.0, Name = "fin-top" });
            }
        }
        double tailX = minTe - 0.2, noseX = tailX + len;
        // Tail cone bottom sits ABOVE the tailwheel/skid (which hangs below it) — never lower than that.
        double tailZ = 0.0;
        foreach (GearConfig g in c.Gear) if (g.IsTailwheel || g.Pos[0] < tailX + 1.5) tailZ = System.Math.Min(tailZ, g.Pos[2] - 0.45);
        pts.Add(new ContactPoint { Body = new Vec3(noseX, 0, 0), Component = AirframeComponent.Nose, BreakSpeedMs = 6.0, Name = "nose" });
        // Propeller disc: the lowest blade tip. ANY ground contact is a prop strike (engine stops, blades bend).
        if (c.Propulsion is not null && c.Propulsion.PropDiameterM > 0)
        {
            double r = c.Propulsion.PropDiameterM * 0.5 * 0.85;
            if (c.Engines.Count == 0)
            {
                pts.Add(new ContactPoint { Body = new Vec3(noseX + 0.1, 0, r), Component = AirframeComponent.Propeller, BreakSpeedMs = 0.15, Name = "prop-tip" });
            }
            else
            {
                foreach (EngineMount e in c.Engines)
                    pts.Add(new ContactPoint { Body = new Vec3(e.Pos[0] + 1.0, e.Pos[1], e.Pos[2] + r), Component = AirframeComponent.Propeller, BreakSpeedMs = 0.15, Name = "prop-tip" });
            }
        }
        pts.Add(new ContactPoint { Body = new Vec3(tailX, 0, tailZ), Component = AirframeComponent.Fuselage, BreakSpeedMs = 0, Name = "tail-cone" });
        pts.Add(new ContactPoint { Body = new Vec3(0.3, 0, -rBody * 1.1), Component = AirframeComponent.Fuselage, BreakSpeedMs = 0, Name = "cabin-top" });
        pts.Add(new ContactPoint { Body = new Vec3(noseX * 0.6, 0, -rBody * 0.9), Component = AirframeComponent.Fuselage, BreakSpeedMs = 0, Name = "cowl-top" });
        pts.Add(new ContactPoint { Body = new Vec3(0.3, 0, rBody * 1.0), Component = AirframeComponent.Fuselage, BreakSpeedMs = 0, Name = "belly" });
        foreach (EngineMount e in c.Engines)
        {
            pts.Add(new ContactPoint { Body = new Vec3(e.Pos[0] + 0.8, e.Pos[1], e.Pos[2] + 0.4), Component = e.Pos[1] < 0 ? AirframeComponent.NacelleLeft : AirframeComponent.NacelleRight, BreakSpeedMs = 6.0, Name = "nacelle" });
        }
        return pts;
    }

    /// <summary>Contact forces (world) and the components whose points just hit hard enough to break.</summary>
    public static (Vec3 Force, Vec3 Moment) Compute(List<ContactPoint> points, Vec3 cg, RigidBodyState s, List<AirframeComponent>? broken, double surfaceOffsetZ = 0.0)
    {
        Vec3 totalF = Vec3.Zero, totalM = Vec3.Zero;
        foreach (ContactPoint p in points)
        {
            Vec3 rBody = p.Body - cg;
            Vec3 rWorld = s.Attitude.Rotate(rBody);
            Vec3 w = s.Position + rWorld;
            Vec3 vel = s.Attitude.Rotate(s.Velocity + Vec3.Cross(s.Rates, rBody));   // world velocity of the point

            // Ground (with the wheel-scale bumps) — normal is world up.
            double groundH = WorldTerrain.WheelGroundHeightAt(w.X, w.Y);
            double? waterH = FloatHydro.WaterSurfaceAt(w.X, w.Y);
            bool onWater = waterH.HasValue && waterH.Value >= groundH - 0.01;
            Vec3 normal = new(0, 0, -1);   // outward (NED up)
            double depth = onWater ? -1.0 : (w.Z + surfaceOffsetZ) - (-groundH);
            // Solids (buildings etc.): whichever is deeper.
            var solid = WorldSolids.Penetration(w.X, w.Y, -w.Z);
            if (solid.HasValue && solid.Value.depth > depth) { normal = solid.Value.normalNed; depth = solid.Value.depth; }

            if (depth <= 0.0) { p.Touching = false; continue; }

            double closing = -Vec3.Dot(vel, normal);          // + = moving INTO the surface
            if (!p.Touching)
            {
                p.Touching = true;
                if (broken is not null && p.BreakSpeedMs > 0 && closing > p.BreakSpeedMs && !broken.Contains(p.Component))
                {
                    broken.Add(p.Component);
                }
            }
            double n = SpringNPerM * depth + DampNsPerM * System.Math.Max(0.0, closing);
            Vec3 f = normal * n;
            // Coulomb friction against the tangential velocity (regularised at low speed).
            Vec3 vt = vel - normal * Vec3.Dot(vel, normal);
            double vtLen = vt.Length;
            if (vtLen > 1e-4)
            {
                double mu = FrictionMu * System.Math.Min(1.0, vtLen / 0.3);
                f -= vt / vtLen * (mu * n);
            }
            totalF += f;
            totalM += Vec3.Cross(rWorld, f);
            Aero.ForceDebug.Add(p.Body, s.Attitude.Conjugate().Rotate(f), Vec3.Zero, "contact");
        }
        return (totalF, totalM);
    }
}
