using FlyingGame.Core.DataContracts;

namespace FlyingGame.Core.Aero;

/// <summary>
/// THE FUSELAGE'S OWN SHAPE: where its midline runs and how big its section is, station by station.
///
/// The crossflow model describes a fuselage as one constant ellipse on the x axis — half-width and half-height from
/// the plan and side areas over the length, and no camber at all, <c>CentreZ</c> documented as "zero for the fuselage,
/// which lies on the axis". That is enough to drag against a crossflow, which is what it was built for. It is not
/// enough to say where an ATTACHED propeller wash goes, because the thing it would attach to has no shape: a Cub's
/// tailcone rises most of a foot between the cockpit and the fin, and in this model it rises not at all.
///
/// So the shape is optional data on top of the same areas (Ty 2026-11-09: "determining a line that represents the
/// midline for each fuselage and then a series of elipses representing the aproximate shape"). Given none, every
/// answer below is exactly the constant section the crossflow model already used, so a type without measured
/// stations behaves as it did.
/// </summary>
public static class FuselageShape
{
    /// <summary>The body's midline height and section half-axes at a station, body axes (NED: negative z is up).</summary>
    public readonly struct Envelope
    {
        public readonly double MidZ, HalfY, HalfZ;
        public Envelope(double midZ, double halfY, double halfZ) { MidZ = midZ; HalfY = halfY; HalfZ = halfZ; }
        /// <summary>The equivalent circular radius of this section — the radius of a circle of the same area, which is
        /// what displaces an axisymmetric wash.</summary>
        public double EquivalentRadiusM => System.Math.Sqrt(System.Math.Max(0.0, HalfY * HalfZ));
    }

    /// <summary>Is there measured station data, or is this the constant section?</summary>
    public static bool HasStations(CrossflowConfig? cf)
        => cf is not null && cf.StationX.Length >= 2
           && cf.StationX.Length == cf.StationMidZ.Length
           && cf.StationX.Length == cf.StationHalfY.Length
           && cf.StationX.Length == cf.StationHalfZ.Length;

    /// <summary>The envelope at body station <paramref name="x"/>, interpolated between measured stations and held at
    /// the ends. Falls back to the constant section with a straight midline.</summary>
    public static Envelope At(CrossflowConfig? cf, double x)
    {
        if (cf is null || cf.LengthM <= 0.1) return new Envelope(0, 0, 0);
        if (!HasStations(cf))
        {
            double hy = cf.PlanArea > 0 ? 0.5 * cf.PlanArea / cf.LengthM : 0.0;
            double hz = cf.SideArea > 0 ? 0.5 * cf.SideArea / cf.LengthM : 0.0;
            return new Envelope(0.0, hy, hz);
        }
        double[] sx = cf.StationX;
        int n = sx.Length;
        // The stations are ordered nose-to-tail in x, which in body axes means DECREASING x (the nose is forward,
        // +x), so the search walks the array and does not assume a direction.
        if (n == 1) return new Envelope(cf.StationMidZ[0], cf.StationHalfY[0], cf.StationHalfZ[0]);
        bool ascending = sx[n - 1] > sx[0];
        for (int i = 0; i + 1 < n; i++)
        {
            double a = sx[i], b = sx[i + 1];
            bool inside = ascending ? (x >= a && x <= b) : (x <= a && x >= b);
            if (!inside) continue;
            double span = b - a;
            double f = System.Math.Abs(span) < 1e-9 ? 0.0 : (x - a) / span;
            return new Envelope(
                cf.StationMidZ[i] + f * (cf.StationMidZ[i + 1] - cf.StationMidZ[i]),
                cf.StationHalfY[i] + f * (cf.StationHalfY[i + 1] - cf.StationHalfY[i]),
                cf.StationHalfZ[i] + f * (cf.StationHalfZ[i + 1] - cf.StationHalfZ[i]));
        }
        // Outside the measured run: hold the nearer end rather than extrapolate a section to nonsense.
        int end = ascending ? (x < sx[0] ? 0 : n - 1) : (x > sx[0] ? 0 : n - 1);
        return new Envelope(cf.StationMidZ[end], cf.StationHalfY[end], cf.StationHalfZ[end]);
    }

    /// <summary>The midline's slope dz/dx at a station — the direction an attached flow is turned to.</summary>
    public static double MidlineSlope(CrossflowConfig? cf, double x)
    {
        if (!HasStations(cf)) return 0.0;
        const double h = 0.05;
        return (At(cf, x - h).MidZ - At(cf, x + h).MidZ) / (2.0 * h);
    }
}
