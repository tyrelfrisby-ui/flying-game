namespace FlyingGame.Core.Nav;

/// <summary>What the pilot HEARS. A four-course range has no needle; the instrument is your ear.</summary>
public enum RangeTone
{
    /// <summary>Out of range, or in the cone of silence overhead the station.</summary>
    Silent,
    /// <summary>The A and the N interlocking into one unbroken note: you are on the course.</summary>
    OnCourse,
    /// <summary>Both letters audible, one louder — you are off the course and this says which way.</summary>
    BiSignalA,
    BiSignalN,
    /// <summary>A clean dot-dash: you are well inside the A quadrant.</summary>
    A,
    /// <summary>A clean dash-dot: you are well inside the N quadrant.</summary>
    N,
}

/// <summary>
/// A FOUR-COURSE RADIO RANGE — the 1930s one, the thing that made airline flying possible before the war and
/// the reason a generation of pilots could fly weather at all. Owner, 2026-10-18: *"build an an range and an
/// an range based airway and approach … the game will have navigation go from pilotage to ded reconing to
/// fire pots to an ranged to ndb to vor to ils but never to gps."*
///
/// HOW IT WORKS, because the model is only honest if the mechanism is. Two crossed loop aerials radiate
/// overlapping figure-of-eight patterns. One keys Morse **A** (dot-dash), the other keys **N** (dash-dot),
/// interlocked so that the N falls into the A's gaps. Stand where the two patterns are equally strong and
/// the letters dovetail into a single unbroken note — that line is a **course**, and there are four of them.
/// Drift off and one letter starts to win: you hear both with one louder (the **bi-signal zone**), then a
/// clean A or a clean N. Which letter tells you which side you are on, and that is the whole instrument.
/// There is no needle. You fly it by ear, and you hold a course by keeping the note from breaking up.
///
/// Directly over the station the two patterns both go to nothing and you hear **silence** — the cone of
/// silence. That is not a fault; it is the only positive fix the system offers, and a range approach is
/// built around it.
///
/// It is a low-frequency transmitter (190–535 kHz, like the beacons), so the signal follows the ground and a
/// ridge does not shadow it — see <see cref="NavReception.NdbSignal"/>. It is also why it was replaced: the
/// courses bend over terrain, the night sky plays with them, and a thunderstorm drowns the lot.
///
/// What is NOT modelled: course bending by the terrain (the real ones were surveyed and bent, sometimes
/// deliberately, to follow a valley), night effect, and the station identifier keyed between the letters.
/// </summary>
public readonly struct AnRange
{
    public readonly string Id;
    public readonly string Name;
    public readonly double X, Y, GroundM;
    public readonly double FreqKhz;
    /// <summary>
    /// The bearing of the FIRST course. The other three follow at 90° intervals, because the aerials are
    /// crossed at right angles. Quadrants alternate A and N.
    /// </summary>
    public readonly double Course0Deg;
    public readonly double RangeNm;
    public readonly string Why;

    public AnRange(string id, string name, double x, double y, double groundM, double freqKhz,
                   double course0Deg, double rangeNm, string why)
    { Id = id; Name = name; X = x; Y = y; GroundM = groundM; FreqKhz = freqKhz; Course0Deg = course0Deg; RangeNm = rangeNm; Why = why; }

    public double AntennaM => GroundM + 2.0;

    /// <summary>
    /// The four courses, in degrees, in order from <see cref="Course0Deg"/>. Normalised to 1…360 rather
    /// than 0…359, because that is how a course is printed on a chart and spoken on a radio: north is 360.
    /// </summary>
    public double[] Courses()
    {
        static double N(double d) { d %= 360; if (d <= 0) d += 360; return d; }
        return new[] { N(Course0Deg), N(Course0Deg + 90), N(Course0Deg + 180), N(Course0Deg + 270) };
    }
}

/// <summary>Reading a four-course range: what you hear, and how far off the course you are.</summary>
public static class AnRangeSignal
{
    /// <summary>Half the width of the steady-note band. The real ones were about three degrees across.</summary>
    public const double OnCourseHalfWidthDeg = 1.5;

    /// <summary>Out to here you still hear both letters, one louder. Beyond it the quadrant signal is clean.</summary>
    public const double BiSignalHalfWidthDeg = 12.0;

    /// <summary>The cone of silence overhead, half-angle from the vertical.</summary>
    public const double ConeHalfAngleDeg = 25.0;

    /// <summary>Morse A: dot, dash. Unit lengths, 1 = a dot.</summary>
    public static readonly int[] MorseA = { 1, 3 };
    /// <summary>Morse N: dash, dot.</summary>
    public static readonly int[] MorseN = { 3, 1 };

    /// <summary>
    /// What the receiver hears at (x, y, altM), and how far off the nearest course it is.
    /// <paramref name="offCourseDeg"/> is signed: positive means the course lies clockwise of you, so a
    /// positive value means turn RIGHT to regain it — the same convention as a CDI needle.
    /// </summary>
    public static RangeTone Read(WorldTerrain t, AnRange r, double x, double y, double altM,
                                 out double offCourseDeg, out double nearestCourseDeg, out double signal)
    {
        offCourseDeg = 0; nearestCourseDeg = r.Course0Deg;
        signal = NdbLikeSignal(r, x, y);
        if (signal < 0.2) return RangeTone.Silent;

        // Overhead: both patterns vanish. The one positive fix the system gives you.
        double dx = x - r.X, dy = y - r.Y;
        double horiz = System.Math.Sqrt(dx * dx + dy * dy);
        double above = altM - r.AntennaM;
        if (above > 0 && horiz < above * System.Math.Tan(ConeHalfAngleDeg * System.Math.PI / 180.0))
            return RangeTone.Silent;

        // Which course is nearest, and how far off it we are.
        double bearingFrom = NavReception.RadialDeg(r.X, r.Y, x, y);
        double best = double.MaxValue;
        foreach (double c in r.Courses())
        {
            double d = NavReception.AngleDiff(c, bearingFrom);   // + = we are clockwise of the course
            if (System.Math.Abs(d) < System.Math.Abs(best)) { best = d; nearestCourseDeg = c; }
        }
        // Flip the sign so it reads like a needle: + means the course is to our right.
        offCourseDeg = -best;

        if (System.Math.Abs(best) <= OnCourseHalfWidthDeg) return RangeTone.OnCourse;

        // Which quadrant. The quadrants alternate, starting with A clockwise of course 0 — so the sign of
        // the offset from the NEAREST course decides the letter, and it alternates with the course index.
        double rel = (bearingFrom - r.Course0Deg) % 360;
        if (rel < 0) rel += 360;
        int quadrant = (int)(rel / 90) % 4;
        bool aQuadrant = quadrant % 2 == 0;

        bool bi = System.Math.Abs(best) <= BiSignalHalfWidthDeg;
        return aQuadrant ? (bi ? RangeTone.BiSignalA : RangeTone.A)
                         : (bi ? RangeTone.BiSignalN : RangeTone.N);
    }

    /// <summary>A range is a low-frequency transmitter, so it fades with distance and not with terrain.</summary>
    public static double NdbLikeSignal(AnRange r, double x, double y)
    {
        double dx = x - r.X, dy = y - r.Y;
        double nm = System.Math.Sqrt(dx * dx + dy * dy) / NavReception.MetresPerNm;
        if (nm <= r.RangeNm) return 1.0;
        double over = (nm - r.RangeNm) / (r.RangeNm * 0.5);
        return over >= 1 ? 0.0 : 1.0 - over;
    }

    // ---------------------------------------------------------------- the keying

    /// <summary>One dot. Eight of these make a cycle, so a letter pair takes 1.2 seconds.</summary>
    public const double UnitSeconds = 0.15;

    /// <summary>The identification tone. 1,020 Hz is the standard one and it is what a range sounded like.</summary>
    public const double ToneHz = 1020.0;

    /// <summary>
    /// THE INTERLOCK, which is the whole trick and the reason the thing worked at all.
    ///
    /// Over an eight-unit cycle the N aerial keys a dash then a dot, and the A aerial keys a dot then a dash,
    /// timed so that **each letter falls exactly into the other's silences**:
    ///
    /// <code>
    ///   unit   0  1  2  3  4  5  6  7
    ///   N      ■  ■  ■  .  ■  .  .  .      dash(3) gap(1) dot(1) gap(3)  = N
    ///   A      .  .  .  ■  .  ■  ■  ■      gap(3) dot(1) gap(1) dash(3)  = A
    ///   both   ■  ■  ■  ■  ■  ■  ■  ■      one unbroken note
    /// </code>
    ///
    /// So where the two are equally strong you hear a continuous tone and nothing else; where one wins you
    /// hear that letter over a quiet background of the other; and far into a quadrant you hear a clean
    /// letter with silence between. The pilot's whole job is to keep the note from breaking up.
    /// </summary>
    public static bool NKeying(double tSeconds)
    {
        int u = Unit(tSeconds);
        return u == 0 || u == 1 || u == 2 || u == 4;
    }

    /// <inheritdoc cref="NKeying"/>
    public static bool AKeying(double tSeconds)
    {
        int u = Unit(tSeconds);
        return u == 3 || u == 5 || u == 6 || u == 7;
    }

    private static int Unit(double tSeconds)
    {
        double cycle = UnitSeconds * 8;
        double t = tSeconds % cycle;
        if (t < 0) t += cycle;
        return (int)(t / UnitSeconds) % 8;
    }

    /// <summary>
    /// How loud each letter is, given how far off the course you are. On the course they are equal, which is
    /// what makes the note continuous; well inside a quadrant one of them is all there is.
    /// </summary>
    public static (double a, double n) LetterStrengths(double offCourseDeg, bool aQuadrant)
    {
        double off = System.Math.Abs(offCourseDeg);
        // 0 on the course, 1 by the edge of the bi-signal zone.
        double win = System.Math.Clamp((off - OnCourseHalfWidthDeg) / (BiSignalHalfWidthDeg - OnCourseHalfWidthDeg), 0, 1);
        double loud = 0.5 + 0.5 * win, quiet = 0.5 - 0.5 * win;
        return aQuadrant ? (loud, quiet) : (quiet, loud);
    }

    /// <summary>
    /// The envelope of what the receiver puts in your headphones at time <paramref name="tSeconds"/>, 0…1 —
    /// multiply a 1,020 Hz tone by this and you have the sound of a four-course range.
    /// </summary>
    public static double Keying(double tSeconds, double offCourseDeg, bool aQuadrant)
    {
        var (a, n) = LetterStrengths(offCourseDeg, aQuadrant);
        return (AKeying(tSeconds) ? a : 0) + (NKeying(tSeconds) ? n : 0);
    }

    /// <summary>Is the tone sitting in an A quadrant? Convenience for the audio, which has no out-params.</summary>
    public static bool IsAQuadrant(RangeTone tone) => tone == RangeTone.A || tone == RangeTone.BiSignalA;

    /// <summary>What a tone sounds like, in words, for the panel and for a test.</summary>
    public static string Describe(RangeTone tone) => tone switch
    {
        RangeTone.OnCourse => "steady note — on course",
        RangeTone.BiSignalA => "A over a note — course to the right of the A",
        RangeTone.BiSignalN => "N over a note — course to the left of the N",
        RangeTone.A => "dot-dash, clean A",
        RangeTone.N => "dash-dot, clean N",
        _ => "silence",
    };
}
