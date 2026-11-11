namespace FlyingGame.Core;

/// <summary>
/// The pilot's view under water (Ty t922u, 2026-11-06: the J-3 on floats upside down in the lake, and "the instrument
/// panel and top of the cockpit don't look different from dry areas even though underwater").
///
/// Why it looked dry: the water is one flat transparent surface (FlyingGame/Water). From ABOVE it tints whatever is
/// under it by the length of ray it covers; from BELOW nothing at all is drawn between the eye and the panel, because
/// no surface lies between them. A head hanging in the water saw the cockpit as if the lake were not there.
///
/// What this decides (pure, no Unity; the PilotUnderwater shader runs the same arithmetic per pixel):
///  - <see cref="SubmergedLength"/>: of a straight ray from the eye, how much lies under the plane y = water. That is
///    what murks a pixel, so the waterline on screen is exactly where the real surface crosses each part.
///  - <see cref="ModeFor"/>: when the treatment runs at all. ONLY FROM THE PILOT'S OWN EYE (the aerial and chase views
///    are never touched, Ty t923u: "the water itself looks great"), only over water, and only once the eye is in the water or
///    within <see cref="NearAboveM"/> of it. A floatplane taxiing upright has its eye 1.5–2 m above the lake and gets
///    nothing, so the approved view from the cockpit on the water does not change either.
///  - With the eye above the surface (<see cref="Mode.NearField"/>) the water's own pass already tints everything
///    under it; the extra murk is held to the aeroplane's own scale (<see cref="NearFieldFullM"/> →
///    <see cref="NearFieldZeroM"/>) so a nosed-in panel reads wet while the lake beyond looks as it always did.
/// </summary>
public static class UnderwaterView
{
    public enum Mode { Off = 0, NearField = 1, Full = 2 }

    /// <summary>The eye this far above the water (or less) and the cockpit is treated. Upright on floats the J-3's eye
    /// sits ~1.6 m up, so 1.0 m keeps the treatment off for every normal float or hull attitude.</summary>
    public const double NearAboveM = 1.0;

    /// <summary>Eye above the water: full extra murk on geometry within this distance of the eye, fading to none at
    /// <see cref="NearFieldZeroM"/> — the cockpit and the airframe, not the lake bed.</summary>
    public const double NearFieldFullM = 4.0, NearFieldZeroM = 8.0;

    /// <summary>Murk per metre of submerged path (1/m): lake water seen from inside it. 0.55/m gives a third of the
    /// way to murk across the 0.7 m to the panel and leaves a few metres of visibility — a flooded cabin in a lake,
    /// not a swimming pool. Chosen, not derived; the colour is the shader's.</summary>
    public const double ExtinctionPerM = 0.55;

    /// <summary>Submerged length (m) of the segment from an eye at height <paramref name="eyeAboveWater"/> (m, negative
    /// when the eye is under water), along a unit direction whose upward component is <paramref name="dirUp"/>, out to
    /// <paramref name="length"/> metres.</summary>
    public static double SubmergedLength(double eyeAboveWater, double dirUp, double length)
    {
        if (length <= 0) return 0;
        if (eyeAboveWater < 0)
            return dirUp <= 0 ? length : Math.Min(length, -eyeAboveWater / dirUp);
        if (dirUp >= 0) return 0;
        return Math.Max(0, length - eyeAboveWater / -dirUp);
    }

    /// <summary>
    /// When the treatment runs: the pilot's own eye, water under it, the eye in the water or close above it.
    ///
    /// <paramref name="pilotsOwnEye"/> WAS "in the cockpit" until 2026-11-10 and is now wider than that, because the
    /// pilot can leave the aeroplane and swim (lane/claude-vr-cockpit's egress, Ty's "you can always get out"). A
    /// seated pilot in a flooded cockpit got murk and one treading water beside it got none, which is the same bug
    /// t922u reported one step further out. Asked which of the two underwater implementations should win, Ty chose
    /// this one, extended: *"teach PilotUnderwater about an eye that is OUTSIDE the cockpit … one source of truth,
    /// extended — not two layers."* So the Bridge passes true for the seat view OR a pilot who is out of the
    /// aeroplane (PilotUnderwater), and still false for the chase and aerial cameras, which is the part t923u cares
    /// about.
    /// </summary>
    public static Mode ModeFor(bool pilotsOwnEye, double? waterY, double eyeY)
    {
        if (!pilotsOwnEye || !waterY.HasValue) return Mode.Off;
        double above = eyeY - waterY.Value;
        if (above < 0) return Mode.Full;
        return above <= NearAboveM ? Mode.NearField : Mode.Off;
    }

    /// <summary>How much of the water's colour a pixel takes (0 dry … 1 opaque murk), as the shader computes it.</summary>
    public static double Murk(Mode mode, double eyeAboveWater, double dirUp, double distance)
    {
        if (mode == Mode.Off) return 0;
        double s = SubmergedLength(eyeAboveWater, dirUp, distance);
        double m = 1 - Math.Exp(-s * ExtinctionPerM);
        if (mode == Mode.NearField)
        {
            double f = Math.Clamp((distance - NearFieldFullM) / (NearFieldZeroM - NearFieldFullM), 0, 1);
            m *= 1 - f * f * (3 - 2 * f);
        }
        return m;
    }
}
