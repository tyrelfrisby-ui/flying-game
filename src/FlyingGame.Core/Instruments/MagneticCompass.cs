using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Instruments;

/// <summary>
/// The whisky compass: two bar magnets on a float, hung from a jewelled pivot in a bowl of fluid, read against a
/// lubber line. Every error a student learns by acronym comes out of that sentence, so that is what is modelled
/// and the errors are not added anywhere:
///
///  - **The float is a pendulum.** It hangs along the APPARENT vertical (the specific force), lagging it a
///    little in the fluid, and it can tilt only about 18° in the bowl before the card touches. So in a
///    coordinated turn the card banks with the aeroplane, and in an acceleration it swings aft.
///  - **The magnets can only turn in the card's own plane.** They line up with whatever part of the field lies
///    in that plane. Level, that is the horizontal field and the card reads true. Tilted, the field's large
///    vertical part (dip — 63° at 45° N) leaks into the plane and drags the north end toward the low side.
///    That one fact IS northerly turning error (lags through north, leads through south, "UNOS") and
///    acceleration error (accelerate east or west and it shows a turn to the north, "ANDS").
///  - **The card is a damped oscillator.** Magnetic stiffness m·B⊥ against the float's inertia, damped by the
///    fluid — and the fluid turns with the bowl, so in a turn it drags the card along before the magnets win.
///    The stiffness is the field IN THE CARD PLANE, so the card is lazier where the dip is steep.
///  - **Deviation** is the aeroplane's own magnetism: a small fixed (hard-iron) field in airframe axes added to
///    the Earth's. That gives the familiar one-cycle deviation card, a degree or two, without a table.
///
/// (E) undamped period 2.0 s and damping ratio 0.35 in the horizontal field at 45° N; float lag 0.3 s; card
/// tilt limit 18° (the figure the FAA quotes for the card to remain free); airframe field 3 % of the local
/// horizontal field fore-and-aft, 2 % athwartships.
/// </summary>
public sealed class MagneticCompass
{
    public const double PeriodS = 2.0, DampingRatio = 0.35, FloatLagS = 0.3, TiltLimitDeg = 18.0;
    public const double ReferenceLatitudeDeg = 45.0;
    /// <summary>The airframe's own field, as fractions of the reference horizontal field, body axes (hard iron).</summary>
    public const double HardIronX = 0.03, HardIronY = 0.02;

    private readonly double _bRef;

    /// <summary>Float "down", world unit: the axis the card turns about.</summary>
    public Vec3 FloatDown = new(0, 0, 1);
    /// <summary>The card's north mark, world unit, perpendicular to <see cref="FloatDown"/>.</summary>
    public Vec3 CardNorth = new(1, 0, 0);
    /// <summary>The card's rate about <see cref="FloatDown"/>, rad/s.</summary>
    public double CardRate;

    public double ReadingDeg { get; private set; }
    /// <summary>The card's tilt in the bowl, degrees: nose-up and right-wing-down positive.</summary>
    public double CardPitchDeg { get; private set; }
    public double CardRollDeg { get; private set; }

    /// <summary>Scales the hard-iron field (1 = as installed; 0 = a perfectly swung compass, for checks).</summary>
    public double DeviationScale = 1.0;

    public MagneticCompass()
    {
        Vec3 b = Earth.MagneticFieldNed(ReferenceLatitudeDeg);
        _bRef = Math.Sqrt(b.X * b.X + b.Y * b.Y);
    }

    /// <summary>The field the magnets feel, world: the Earth's plus the airframe's.</summary>
    public Vec3 FieldWorld(Quat attitude, Vec3 earthField) =>
        earthField + attitude.Rotate(new Vec3(HardIronX, HardIronY, 0) * (_bRef * DeviationScale));

    /// <summary>Settled at rest in the field, hanging plumb in the case's current attitude.</summary>
    public void Settle(in PanelInputs x, Vec3 earthField)
    {
        Quat q = x.Attitude;
        FloatDown = Clamp(V.Unit(-q.Rotate(x.SpecificForceBody), new Vec3(0, 0, 1)), q);
        Vec3 b = FieldWorld(q, earthField);
        CardNorth = V.Unit(b - FloatDown * b.Dot(FloatDown), new Vec3(1, 0, 0));
        CardRate = 0;
        Read(q);
    }

    public void Step(double dt, in PanelInputs x, Vec3 earthField)
    {
        Quat q = x.Attitude;
        // The float hangs along the apparent vertical, lagging in the fluid, and stops against the bowl.
        Vec3 target = V.Unit(-q.Rotate(x.SpecificForceBody), FloatDown);
        Vec3 p = (FloatDown + (target - FloatDown) * (1.0 - Math.Exp(-dt / FloatLagS))).Normalized();
        p = Clamp(p, q);
        FloatDown = p;

        // The card turns about the float axis: magnetic torque from the field in the card's plane, fluid
        // damping against the BOWL's rotation about the same axis.
        Vec3 b = FieldWorld(q, earthField);
        Vec3 bPlane = b - p * b.Dot(p);
        double w0Ref = 2.0 * Math.PI / PeriodS;
        double k = w0Ref * w0Ref * bPlane.Length / _bRef;
        double c = 2.0 * DampingRatio * w0Ref;
        Vec3 n = V.Unit(CardNorth - p * CardNorth.Dot(p), CardNorth);
        double sinErr = n.Cross(V.Unit(bPlane, n)).Dot(p);
        double bowlRate = q.Rotate(x.RatesBody).Dot(p);
        CardRate += (k * sinErr - c * (CardRate - bowlRate)) * dt;   // semi-implicit Euler
        CardNorth = V.Rotate(n, p, CardRate * dt).Normalized();
        Read(q);
    }

    /// <summary>Keep the float within <see cref="TiltLimitDeg"/> of the case's vertical.</summary>
    private static Vec3 Clamp(Vec3 p, Quat q)
    {
        Vec3 zc = q.Rotate(new Vec3(0, 0, 1));
        double ang = V.Angle(zc, p);
        double lim = TiltLimitDeg * V.Deg;
        return ang <= lim ? p : V.Toward(zc, p, lim);
    }

    private void Read(Quat q)
    {
        Vec3 p = FloatDown;
        Vec3 xc = q.Rotate(new Vec3(1, 0, 0));
        Vec3 xp = V.Unit(xc - p * xc.Dot(p), xc);
        ReadingDeg = V.Wrap360(Math.Atan2(CardNorth.Cross(xp).Dot(p), CardNorth.Dot(xp)) / V.Deg);
        Vec3 pb = q.Conjugate().Rotate(p);
        CardRollDeg = Math.Atan2(pb.Y, pb.Z) / V.Deg;
        CardPitchDeg = Math.Atan2(-pb.X, Math.Sqrt(pb.Y * pb.Y + pb.Z * pb.Z)) / V.Deg;
    }
}
