using System;
using FlyingGame.Core.DataContracts;

namespace FlyingGame.Core.Instruments;

/// <summary>
/// WHICH UNIT AN AEROPLANE'S AIRSPEED INDICATOR READS IN, and the whole face that follows from it.
///
/// Ty, 2026-11-10, asked what rule the fleet follows after settling five types by hand ("mph on the J-3, the 2-33, the
/// Great Lakes and the Pitts; knots on the Extra"), chose **"By era and category"**:
///
///     "Vintage, fabric and experimental types read mph; anything certified modern or turbine reads knots."
///
/// That is a FLEET rule, so it lives in the data, not in the drawing code: every <c>configs/aircraft/&lt;id&gt;.json</c>
/// declares <c>instruments.airspeed.units</c> ("mph" or "kt") and every surface that shows an airspeed to the pilot
/// reads it from here. One key, one helper. Before this, the flat dials (Bridge AnalogGauges), the HUD (HudOverlay),
/// the VR world HUD and the split-screen HUD each hard-coded <c>* 1.9438</c> and the caption "KT", while the MODELLED
/// cockpit panels drew whatever their own builder drew — so the Pitts' panel ASI read mph (TCDS A8SO, 212 mph CAS) and
/// the same aeroplane's exterior dial read knots in the same session. A dial that reads mph with knots arcs on it is
/// worse than either, which is the whole reason this is one place.
///
/// TWO TYPES ARE THE OWNER'S WORD AGAINST THE RULE, and his word wins — do not "correct" either:
///  - the GREAT LAKES is mph, though a 2T-1A-2 off the modern Ohio line could be either (TCDS A18EA rev 11 is in mph
///    CAS, so the rule and his instruction agree anyway);
///  - the EXTRA 300 is knots, though an aerobatic type is exactly what "experimental" in the rule would catch. Here it
///    is the RULE that is wrong and he is right: the Extra 300's POH section 2 is published in KIAS (VS0 60, VNO 158,
///    VNE 220 KIAS), so knots is also what the aeroplane itself reads.
///
/// THREE TYPES DEPART FROM THE SHAPE HE WAS SHOWN, by applying the rule he actually chose: the P-51D, the DC-3 and the
/// H-4. The list he was shown had them in knots; the rule says vintage and not turbine, and the real aeroplanes agree
/// (a P-51D's indicator is marked in mph; so is a DC-3's). Flagged for him in the hand-back: one line flips any of them.
///
/// See <c>docs/INSTRUMENT-LIBRARY.md</c> for the face spec the modelled panels draw from the same block.
/// </summary>
public enum AsiUnit
{
    /// <summary>Knots indicated. The default when a config says nothing, so a new type reads knots until someone decides.</summary>
    Knots,
    /// <summary>Statute miles per hour indicated.</summary>
    Mph,
}

/// <summary>The unit, its conversion and its caption. <see cref="AsiUnit"/> carries the owner's rule.</summary>
public static class AsiUnits
{
    /// <summary>1 m/s in knots and in mph. These two constants are the ONLY place the conversions belong: the
    /// drawing code used to carry four copies of 1.9438 at four different precisions.</summary>
    public const double MsToKnots = 1.943844, MsToMph = 2.2369363;

    /// <summary>The config's <c>instruments.airspeed.units</c> string. Anything but "mph" is knots: the fallback has to
    /// be the unit most of aviation reads, so a config nobody has decided yet is merely conservative, not wrong.
    /// <c>AsiUnitsTests</c> refuses to let a config in <c>configs/aircraft/</c> leave the key out at all.</summary>
    public static AsiUnit Parse(string? units) =>
        units != null && units.Trim().Equals("mph", StringComparison.OrdinalIgnoreCase) ? AsiUnit.Mph : AsiUnit.Knots;

    /// <summary>The unit this aeroplane's ASI reads in.</summary>
    public static AsiUnit For(AircraftConfig? config) =>
        Parse(config?.Instruments?.Airspeed?.Units);

    /// <summary>Display units per m/s.</summary>
    public static double PerMs(AsiUnit unit) => unit == AsiUnit.Mph ? MsToMph : MsToKnots;

    /// <summary>An airspeed in m/s as the pilot's dial reads it.</summary>
    public static double FromMs(AsiUnit unit, double ms) => ms * PerMs(unit);

    /// <summary>A dial reading back to m/s.</summary>
    public static double ToMs(AsiUnit unit, double value) => value / PerMs(unit);

    /// <summary>The caption on the dial and beside the readout: "MPH" or "KT".</summary>
    public static string Caption(AsiUnit unit) => unit == AsiUnit.Mph ? "MPH" : "KT";

    /// <summary>The word a lesson or a table uses in running text ("mph" / "kt").</summary>
    public static string Word(AsiUnit unit) => unit == AsiUnit.Mph ? "mph" : "kt";
}

/// <summary>
/// EVERYTHING ON THE AIRSPEED FACE, in the unit the type reads — the scale top, the tick and numeral steps, the
/// certificated range arcs and the red line. Built by <see cref="AsiDial.For"/> so the flat dials, the HUD and any
/// future tape all mark the same speeds in the same unit.
///
/// Where the config declares a full face (<c>scaleMin</c>/<c>scaleMax</c>/<c>arcs</c>/<c>redline</c> — the four types
/// with modelled cockpits, whose faces <c>tools/own_models/instruments/build.py</c> draws from the same block) the flat
/// dial uses it verbatim, so the exterior dial and the panel dial in the cockpit are the same instrument. Where the
/// config declares only a unit, the scale is derived and the red line comes from <c>limits.vneMs</c>: a derived scale
/// must still reach Vne or there is nowhere to put the red line.
/// </summary>
public sealed class AsiDial
{
    public readonly AsiUnit Unit;
    /// <summary>Top of the scale, display units. 0 on the dial is the start of the 300° sweep.</summary>
    public readonly double Top;
    /// <summary>Minor tick step and the step between printed numerals (the numerals are every fourth tick).</summary>
    public readonly double TickStep, LabelStep;
    /// <summary>Top of the low-speed red arc (0 = none). Not a certificated marking: a game affordance that has been on
    /// these dials since they were drawn, so it stays — but where the config gives a green arc it ends at the green
    /// floor (the published stall) instead of at a guess off the spawn speed.</summary>
    public readonly double StallTop;
    /// <summary>Vne, display units. 0 when it does not fit on the scale (it always should).</summary>
    public readonly double Redline;
    /// <summary>White (flap operating), green (normal operating) and yellow (caution) arcs, display units.
    /// Lo == Hi means the type has not had its arcs authored; only the four with modelled cockpits have.</summary>
    public readonly double WhiteLo, WhiteHi, GreenLo, GreenHi, YellowLo, YellowHi;

    private AsiDial(AsiUnit unit, double top, double tickStep, double labelStep, double stallTop, double redline,
        double whiteLo, double whiteHi, double greenLo, double greenHi, double yellowLo, double yellowHi)
    {
        Unit = unit; Top = top; TickStep = tickStep; LabelStep = labelStep; StallTop = stallTop; Redline = redline;
        WhiteLo = whiteLo; WhiteHi = whiteHi; GreenLo = greenLo; GreenHi = greenHi; YellowLo = yellowLo; YellowHi = yellowHi;
    }

    public bool HasWhite => WhiteHi > WhiteLo;
    public bool HasGreen => GreenHi > GreenLo;
    public bool HasYellow => YellowHi > YellowLo;

    /// <summary>The smallest scale top, in knots, that any dial gets. Was <c>Mathf.Max(120f, ...)</c> in
    /// AnalogGauges: the same floor, now expressed as a speed rather than as a number of knots, so an mph dial gets
    /// the same airspeed (138 mph) rather than 120 mph.</summary>
    public const double MinTopKnots = 120.0;

    /// <summary>
    /// The dial for this aeroplane. <paramref name="spawnIasMs"/> is the type's spawn IAS, which is what the old
    /// derivation used (2.2 × spawn, rounded up to 20) and still does — but the result is now also forced to clear
    /// Vne by 5 %, because a scale that stops short of Vne cannot show a red line and the red line is the one marking
    /// on an ASI that is about the airframe rather than about the wing.
    /// </summary>
    public static AsiDial For(AircraftConfig? config, double spawnIasMs)
    {
        AsiUnit unit = AsiUnits.For(config);
        double perMs = AsiUnits.PerMs(unit);
        AirspeedInstrumentConfig? spec = config?.Instruments?.Airspeed;
        double vne = config != null ? config.Limits.VneMs * perMs : 0.0;

        // The declared red line wins over the converted Vne: a TCDS publishes Vne in CAS at one number, and that
        // number is what is painted on the glass. Where they disagree the painted one is what the pilot reads.
        double redline = spec is { Redline: > 0 } ? spec.Redline : vne;

        double top;
        if (spec is { ScaleMax: > 0 })
        {
            top = spec.ScaleMax;
        }
        else
        {
            double spawn = spawnIasMs * perMs;
            double want = Math.Max(Math.Max(MinTopKnots * perMs / AsiUnits.MsToKnots, spawn * 2.2), redline * 1.05);
            top = Math.Ceiling(want / 20.0) * 20.0;
        }
        if (top <= 0.0) top = MinTopKnots * perMs / AsiUnits.MsToKnots;

        double tick = top <= 200.0 ? 10.0 : top <= 400.0 ? 20.0 : 50.0;
        // A derived scale is rounded up to a whole number of ticks so the last tick lands ON the top of the scale.
        // A DECLARED scaleMax is left exactly as published: it is the number printed on a real face.
        if (spec is not { ScaleMax: > 0 }) top = Math.Ceiling(top / tick) * tick;

        double whiteLo = 0, whiteHi = 0, greenLo = 0, greenHi = 0, yellowLo = 0, yellowHi = 0;
        AsiArcsConfig? arcs = spec?.Arcs;
        if (arcs != null)
        {
            (whiteLo, whiteHi) = Pair(arcs.White);
            (greenLo, greenHi) = Pair(arcs.Green);
            (yellowLo, yellowHi) = Pair(arcs.Yellow);
        }

        // The low-speed red arc: the published stall where there is one (the green floor), else the old guess.
        double stallTop = greenHi > greenLo ? greenLo : spawnIasMs * 0.62 * perMs;

        return new AsiDial(unit, top, tick, tick * 4.0, Math.Min(stallTop, top),
            redline > 0 && redline <= top ? redline : 0.0,
            whiteLo, whiteHi, greenLo, greenHi, yellowLo, yellowHi);
    }

    private static (double, double) Pair(double[]? a) => a is { Length: >= 2 } ? (a[0], a[1]) : (0.0, 0.0);

    /// <summary>This aeroplane's indicated airspeed as its own dial reads it.</summary>
    public double Read(double iasMs) => iasMs * AsiUnits.PerMs(Unit);

    /// <summary>"KT" / "MPH".</summary>
    public string Caption => AsiUnits.Caption(Unit);
}
