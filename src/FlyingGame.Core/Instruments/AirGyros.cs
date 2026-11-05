using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Instruments;

/// <summary>
/// The vacuum attitude indicator: a vertical gyro in two gimbals, outer on the case's fore-and-aft axis
/// (bank), inner across it (pitch). The horizon card is the gimbals: what the pilot reads is the angle between
/// the case and a spin axis that holds still in space.
///
/// WHAT MOVES THE SPIN AXIS, and nothing else does:
///  1. **Erection** — four pendulous vanes on the rotor housing's exhaust ports. When the axis leans from the
///     apparent vertical a vane uncovers its port and the escaping air torques the housing; the gyro precesses
///     back toward the apparent vertical at a rate set by the air (the same jet that drives the rotor) over the
///     angular momentum. It erects to the APPARENT vertical — the direction of the specific force — so a long
///     turn or an acceleration tilts what it erects toward, and the indication is wrong by that much
///     afterwards. That is the real turn and acceleration error, not something added to it.
///  2. **Gimbal friction** — while the case rotates about a gimbal axis the races drag the gimbal along, and
///     the rotor answers at right angles (<see cref="GimbalPhysics.Friction"/>). Small at full speed, large on a
///     dying rotor.
///  3. **The Earth turning underneath** — a gyro holds still in space, not on the ground, so it drifts at the
///     Earth's rate (15 °/h at most), which the erection cancels.
///  4. **The stops** — the inner gimbal stops at ±70° of pitch, the outer at ±110° of bank (FAA PHAK: "older
///     instruments ... 100–110° of bank and 60–70° of pitch"). Past a stop the case drives the gimbal; the
///     torque it applies precesses the rotor about the OTHER gimbal at right angles, which is a tumble: the
///     horizon whirls round and then takes minutes to erect again.
///
/// A dead pump takes away the erection at once and the rotor's speed over a minute or two: the horizon stays
/// believable for a while and then wanders off, which is exactly what makes a vacuum failure dangerous.
///
/// (E) rotor 0.43 kg brass, 27 mm radius, 21,000 rpm at 5.0 inHg; erection 6 °/min at rated speed, saturating
/// beyond 3° of error; gimbal friction worth 3 °/min of precession at rated speed. All scale physically with
/// the air and the angular momentum away from the rated point.
/// </summary>
public sealed class AttitudeIndicator
{
    public const double PitchStopDeg = 70.0, BankStopDeg = 110.0;
    public const double ErectionDegPerMin = 6.0, ErectionSaturationDeg = 3.0, FrictionDegPerMin = 3.0;
    public const double RotorMassKg = 0.43, RotorRadiusM = 0.027, RatedRpm = 21000.0, BearingNm = 5e-5;

    public readonly GyroRotor Rotor;
    private readonly double _frictionNm;

    /// <summary>Spin axis, world, unit. Erect = straight down.</summary>
    public Vec3 SpinAxis = new(0, 0, 1);

    public double PitchDeg { get; private set; }
    public double BankDeg { get; private set; }
    /// <summary>True on any step a gimbal was against its stop.</summary>
    public bool OnStop { get; private set; }
    /// <summary>Sub-steps spent against a stop since the instrument was built — a tumble leaves a count.</summary>
    public int StopHits { get; private set; }

    public AttitudeIndicator()
    {
        double pm = Pneumatics.StdPressurePa - VacuumSystem.RatedInHg * Pneumatics.PaPerInHg;
        double flow = Pneumatics.MassFlow(VacuumSystem.AiAreaM2, Pneumatics.StdPressurePa, Pneumatics.StdTempK, pm);
        double jet = Pneumatics.JetVelocity(Pneumatics.StdPressurePa, Pneumatics.StdTempK, pm);
        Rotor = new GyroRotor(RotorMassKg, RotorRadiusM, RatedRpm, flow, jet, BearingNm);
        _frictionNm = FrictionDegPerMin * V.Deg / 60.0 * Rotor.RatedH;
    }

    /// <summary>Running and erect — the state after the five-minute wait. Sets the rotor speed to <paramref name="speedFraction"/>.</summary>
    public void Erect(in PanelInputs x, double speedFraction = 1.0)
    {
        SpinAxis = new Vec3(0, 0, 1);
        Rotor.OmegaRadS = Rotor.RatedRadS * speedFraction;
        Read(x.Attitude);
    }

    public void Step(double dt, in PanelInputs x, double flowKgS, double jetMs, Vec3 earthRateNed)
    {
        Rotor.Step(dt, flowKgS, jetMs);
        double h = Rotor.H;
        Quat q = x.Attitude;
        Vec3 s = SpinAxis;

        // 3. The Earth turns under a gyro that does not.
        s = (s - earthRateNed.Cross(s) * dt).Normalized();

        // 2. Gimbal friction, outer (fore-and-aft) then inner.
        Vec3 wCase = q.Rotate(x.RatesBody);
        Vec3 ao = q.Rotate(new Vec3(1, 0, 0));
        Vec3 ai = V.Unit(ao.Cross(s), q.Rotate(new Vec3(0, -1, 0)));
        s = GimbalPhysics.Friction(s, ao, wCase.Dot(ao), _frictionNm, h, dt);
        s = GimbalPhysics.Friction(s, ai, wCase.Dot(ai), _frictionNm, h, dt);

        // 1. Erection toward the apparent vertical.
        Vec3 down = V.Unit(-q.Rotate(x.SpecificForceBody), new Vec3(0, 0, 1));
        double err = V.Angle(s, down);
        double rate = ErectionDegPerMin * V.Deg / 60.0 * Rotor.JetFraction(flowKgS, jetMs)
                      * Rotor.RatedH / Math.Max(h, 0.01 * Rotor.RatedH);
        s = V.Toward(s, down, rate * dt * Math.Min(1.0, err / (ErectionSaturationDeg * V.Deg)));

        // 4. The stops.
        OnStop = false;
        Vec3 sb = q.Conjugate().Rotate(s);
        double bank = Math.Atan2(sb.Y, sb.Z), pitch = Math.Atan2(-sb.X, Math.Sqrt(sb.Y * sb.Y + sb.Z * sb.Z));
        if (Math.Abs(pitch) > PitchStopDeg * V.Deg)
        {
            OnStop = true; StopHits++;
            double over = Math.Abs(pitch) - PitchStopDeg * V.Deg;
            pitch = Math.Sign(pitch) * PitchStopDeg * V.Deg;
            sb = FromAngles(pitch, bank);
            // The case drives the inner gimbal about its axis; the rotor precesses toward that torque, i.e.
            // round the outer gimbal: the horizon rolls.
            Vec3 aib = V.Unit(new Vec3(1, 0, 0).Cross(sb), new Vec3(0, -1, 0));
            double push = Math.Sign(x.RatesBody.Dot(aib)); if (push == 0) push = Math.Sign(pitch);
            sb = V.Toward(sb, aib * push, over);
        }
        bank = Math.Atan2(sb.Y, sb.Z); pitch = Math.Atan2(-sb.X, Math.Sqrt(sb.Y * sb.Y + sb.Z * sb.Z));
        if (Math.Abs(bank) > BankStopDeg * V.Deg)
        {
            OnStop = true; StopHits++;
            double over = Math.Abs(bank) - BankStopDeg * V.Deg;
            bank = Math.Sign(bank) * BankStopDeg * V.Deg;
            sb = FromAngles(pitch, bank);
            // Driven round the outer gimbal: the rotor precesses toward the case's fore-and-aft axis — pitch.
            double push = Math.Sign(x.RatesBody.X); if (push == 0) push = Math.Sign(bank);
            sb = V.Toward(sb, new Vec3(push, 0, 0), over);
        }
        SpinAxis = q.Rotate(sb).Normalized();
        Read(q);
    }

    /// <summary>The spin axis in case axes for an indicated pitch and bank (rad).</summary>
    private static Vec3 FromAngles(double pitch, double bank) =>
        new(-Math.Sin(pitch), Math.Sin(bank) * Math.Cos(pitch), Math.Cos(bank) * Math.Cos(pitch));

    private void Read(Quat q)
    {
        Vec3 sb = q.Conjugate().Rotate(SpinAxis);
        BankDeg = Math.Atan2(sb.Y, sb.Z) / V.Deg;
        PitchDeg = Math.Atan2(-sb.X, Math.Sqrt(sb.Y * sb.Y + sb.Z * sb.Z)) / V.Deg;
    }

    /// <summary>How far the spin axis is from the true vertical, degrees — the instrument's total error, for checks.</summary>
    public double ErrorFromVerticalDeg => V.Angle(SpinAxis, new Vec3(0, 0, 1)) / V.Deg;
}

/// <summary>
/// The directional gyro: NOT slaved. A horizontal spin axis in two gimbals — outer about the case's vertical
/// (the card is fixed to it), inner across the spin axis — that the pilot sets to the magnetic compass with
/// the knob and then trusts for fifteen minutes at a time.
///
/// WHY IT DRIFTS, from the mechanism:
///  1. **Apparent drift.** It holds its direction in space; the ground turns under it at Ω·sin(latitude) about
///     the local vertical — 10.6 °/h at 45° N, and in the northern hemisphere the reading DECREASES. A 1970s
///     panel DG has no latitude nut; this is the bulk of what a pilot corrects every quarter hour.
///  2. **Residual unbalance.** No gimbal is perfectly balanced. A mass offset along the spin axis, pulled by
///     the apparent gravity, is a torque across the axis and a steady precession in azimuth: 4 °/h at rated
///     speed and 1 g (E — the FAA's acceptance figure is 3° in 15 minutes all-in; this plus the Earth's 10.6
///     comes to 3.7° per 15 minutes at 45° N). It grows as the rotor slows and in a steep turn.
///  3. **Gimbal friction.** While the aeroplane rolls and pitches the inner gimbal moves in its races, and the
///     friction precesses the card — so manoeuvring drifts it more than cruising does.
///  4. **The leveling jet.** The drive jet is fixed to the outer gimbal; when the spin axis tilts out of the
///     gimbal's plane the jet strikes the buckets off-centre and precesses it back. Like the AI's vanes it
///     dies with the air.
///  5. **Gimbal error** comes free from the geometry: in a bank the card reads the spin axis's projection on
///     the CASE's horizontal plane, which is not the aeroplane's heading.
///  6. **Stops** at ±55° of tilt (PHAK: "the bank and pitch limits of the heading indicator ... about 55°"),
///     past which it tumbles and the card spins.
///
/// THE KNOB. Push it in and the inner gimbal is caged level; turn it and the outer gimbal (card and rotor)
/// turns with it. <see cref="TurnKnob"/> and <see cref="SetTo"/> do exactly that to the spin axis.
///
/// (E) rotor 0.31 kg, 24 mm radius, 18,000 rpm at 5.0 inHg; leveling 6 °/min; friction 3 °/min at rated speed.
/// </summary>
public sealed class DirectionalGyro
{
    public const double TiltStopDeg = 55.0;
    public const double LevelingDegPerMin = 6.0, LevelingSaturationDeg = 3.0, FrictionDegPerMin = 3.0;
    public const double UnbalanceDriftDegPerHour = 4.0;
    public const double RotorMassKg = 0.31, RotorRadiusM = 0.024, RatedRpm = 18000.0, BearingNm = 4e-5;

    public readonly GyroRotor Rotor;
    private readonly double _frictionNm, _unbalanceNm;

    /// <summary>Spin axis, world, unit.</summary>
    public Vec3 SpinAxis = new(1, 0, 0);
    /// <summary>The reading under the lubber line, degrees 0–360.</summary>
    public double CardDeg { get; private set; }
    /// <summary>Tilt of the spin axis out of the case's horizontal plane, degrees.</summary>
    public double TiltDeg { get; private set; }
    /// <summary>Total turns of the knob, degrees — for the rig's knob node.</summary>
    public double KnobDeg { get; private set; }
    public bool OnStop { get; private set; }
    public int StopHits { get; private set; }
    /// <summary>Scales the unbalance term (1 = the instrument as built). A check zeroes it to see the Earth alone.</summary>
    public double UnbalanceScale = 1.0;

    public DirectionalGyro()
    {
        double pm = Pneumatics.StdPressurePa - VacuumSystem.RatedInHg * Pneumatics.PaPerInHg;
        double flow = Pneumatics.MassFlow(VacuumSystem.DgAreaM2, Pneumatics.StdPressurePa, Pneumatics.StdTempK, pm);
        double jet = Pneumatics.JetVelocity(Pneumatics.StdPressurePa, Pneumatics.StdTempK, pm);
        Rotor = new GyroRotor(RotorMassKg, RotorRadiusM, RatedRpm, flow, jet, BearingNm);
        _frictionNm = FrictionDegPerMin * V.Deg / 60.0 * Rotor.RatedH;
        _unbalanceNm = UnbalanceDriftDegPerHour * V.Deg / 3600.0 * Rotor.RatedH;
    }

    /// <summary>Running, level, and set to <paramref name="cardDeg"/>.</summary>
    public void Erect(in PanelInputs x, double cardDeg, double speedFraction = 1.0)
    {
        Rotor.OmegaRadS = Rotor.RatedRadS * speedFraction;
        SpinAxis = x.Attitude.Rotate(new Vec3(1, 0, 0));
        SetTo(x.Attitude, cardDeg);
    }

    /// <summary>Push and turn the knob until the card reads <paramref name="cardDeg"/>.</summary>
    public void SetTo(Quat attitude, double cardDeg)
    {
        double turn = Nav.NavReception.AngleDiff(CardDeg, cardDeg);
        Cage(attitude, cardDeg);
        KnobDeg += turn;
    }

    /// <summary>Push the knob in and turn it <paramref name="deg"/> clockwise: the card reads that much more.</summary>
    public void TurnKnob(Quat attitude, double deg)
    {
        Cage(attitude, CardDeg + deg);
        KnobDeg += deg;
    }

    private void Cage(Quat q, double cardDeg)
    {
        // Caged: the inner gimbal locked square to the outer, i.e. the spin axis in the case's plane, at the
        // card angle that puts cardDeg under the lubber line.
        double a = -cardDeg * V.Deg;
        SpinAxis = q.Rotate(new Vec3(Math.Cos(a), Math.Sin(a), 0)).Normalized();
        Read(q);
    }

    public void Step(double dt, in PanelInputs x, double flowKgS, double jetMs, Vec3 earthRateNed)
    {
        Rotor.Step(dt, flowKgS, jetMs);
        double h = Rotor.H;
        Quat q = x.Attitude;
        Vec3 s = SpinAxis;

        // 1. The Earth.
        s = (s - earthRateNed.Cross(s) * dt).Normalized();

        // 2. Unbalance: offset along the spin axis, pulled by the apparent gravity.
        Vec3 gApp = -q.Rotate(x.SpecificForceBody) / Earth.G;   // in g, pointing "down"
        s = GimbalPhysics.Precess(s, gApp.Cross(s) * (_unbalanceNm * UnbalanceScale), Math.Max(h, 0.01 * Rotor.RatedH), dt);

        // 3. Friction, outer (case vertical) then inner.
        Vec3 wCase = q.Rotate(x.RatesBody);
        Vec3 ao = q.Rotate(new Vec3(0, 0, 1));
        Vec3 ai = V.Unit(ao.Cross(s), q.Rotate(new Vec3(0, 1, 0)));
        s = GimbalPhysics.Friction(s, ao, wCase.Dot(ao), _frictionNm, h, dt);
        s = GimbalPhysics.Friction(s, ai, wCase.Dot(ai), _frictionNm, h, dt);

        // 4. Leveling toward the outer gimbal's plane.
        Vec3 inPlane = V.Unit(s - ao * s.Dot(ao), s);
        double err = V.Angle(s, inPlane);
        double rate = LevelingDegPerMin * V.Deg / 60.0 * Rotor.JetFraction(flowKgS, jetMs)
                      * Rotor.RatedH / Math.Max(h, 0.01 * Rotor.RatedH);
        s = V.Toward(s, inPlane, rate * dt * Math.Min(1.0, err / (LevelingSaturationDeg * V.Deg)));

        // 6. The stop: the case drives the inner gimbal and the rotor precesses round the outer one — the card spins.
        OnStop = false;
        Vec3 sb = q.Conjugate().Rotate(s);
        double tilt = Math.Atan2(sb.Z, Math.Sqrt(sb.X * sb.X + sb.Y * sb.Y));
        if (Math.Abs(tilt) > TiltStopDeg * V.Deg)
        {
            OnStop = true; StopHits++;
            double over = Math.Abs(tilt) - TiltStopDeg * V.Deg;
            double az = Math.Atan2(sb.Y, sb.X), t = Math.Sign(tilt) * TiltStopDeg * V.Deg;
            sb = new Vec3(Math.Cos(t) * Math.Cos(az), Math.Cos(t) * Math.Sin(az), Math.Sin(t));
            Vec3 aib = V.Unit(new Vec3(0, 0, 1).Cross(sb), new Vec3(0, 1, 0));
            double push = Math.Sign(x.RatesBody.Dot(aib)); if (push == 0) push = 1;
            sb = V.Toward(sb, aib * push, over);
        }
        SpinAxis = q.Rotate(sb).Normalized();
        Read(q);
    }

    private void Read(Quat q)
    {
        Vec3 sb = q.Conjugate().Rotate(SpinAxis);
        CardDeg = V.Wrap360(-Math.Atan2(sb.Y, sb.X) / V.Deg);
        TiltDeg = Math.Atan2(sb.Z, Math.Sqrt(sb.X * sb.X + sb.Y * sb.Y)) / V.Deg;
    }
}
