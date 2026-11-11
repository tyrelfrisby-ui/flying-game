using System;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core.Vr
{
    /// <summary>What the pilot works, after the belt, to get out (docs/vr-cockpit.md "You can always get out").</summary>
    public enum ExitControl
    {
        /// <summary>No exit is modelled (no pilot, or a hang glider's / paraglider's harness: that is a different
        /// question, not a seat belt).</summary>
        None = 0,
        /// <summary>An open cockpit: the belt is all there is.</summary>
        BeltOnly = 1,
        /// <summary>A canopy: its latch or its jettison / emergency release (Pitts ball or jettison knob, Extra handles
        /// or CanopyRelease, a generic canopy-release lever on the rest).</summary>
        Canopy = 2,
        /// <summary>A cabin door: its handle.</summary>
        Door = 3,
    }

    /// <summary>Which edge an opening turns about, for the water load on it (DoorWaterLoad).</summary>
    public enum HingeEdge
    {
        /// <summary>A vertical hinge at the door's front edge (172, PA-28, Seminole, Beaver).</summary>
        Forward = 0,
        /// <summary>Hinged along its top edge (the Cub's upper half; the SR22's gull-wing doors).</summary>
        Top = 1,
        /// <summary>Hinged along its bottom edge (the Cub's lower half).</summary>
        Bottom = 2,
        /// <summary>A canopy: a near-horizontal shell hinged along one side (or slid and hinged).</summary>
        CanopySide = 3,
    }

    /// <summary>The opening the pilot leaves through, for the water load: its size, its height in the cockpit and its
    /// hinge. Door sizes are estimates from the type's cabin (E); canopy plans are Codex's models (CanopySpec).</summary>
    public readonly struct ExitOpening
    {
        public readonly double WidthM, HeightM;
        /// <summary>The opening's centre below the pilot's eye, m (positive = below).</summary>
        public readonly double CentreBelowEyeM;
        public readonly HingeEdge Hinge;
        /// <summary>Hinge to the handle, as a fraction of the dimension across the hinge (width for a forward hinge,
        /// height for top/bottom). For a canopy: hinge to the hand at the free edge over hinge to the plan centroid.</summary>
        public readonly double HandleArmFrac;
        /// <summary>Canopy only: hinge line to the plan centroid, m.</summary>
        public readonly double CanopyLoadArmM;

        public ExitOpening(double widthM, double heightM, double centreBelowEyeM, HingeEdge hinge, double handleArmFrac, double canopyLoadArmM = 0.0)
        {
            WidthM = widthM; HeightM = heightM; CentreBelowEyeM = centreBelowEyeM; Hinge = hinge;
            HandleArmFrac = handleArmFrac; CanopyLoadArmM = canopyLoadArmM;
        }

        public double AreaM2 => WidthM * HeightM;
    }

    /// <summary>
    /// ONE TYPE'S WAY OUT, per seat. Every number is an estimate (E) unless it names its source; the grab nodes are the
    /// model's own where they exist (CONTRACT.md), else an authored proxy relative to the seat's eye (cockpit frame: +x
    /// right, +y up, +z nose) and one `[VrCockpit] missing node` line.
    ///
    /// THE CLIMB-OUT ON THE GROUND (scripted: ClimbPath) runs eye → stand up in the seat → over the sill → onto the
    /// step or wing walk → down onto the ground, on <see cref="Side"/>. Heights: the step's height above the ground
    /// (<see cref="StepAboveGroundM"/>) — a low wing's walkway, a biplane's lower-wing root, a high wing's gear step.
    /// </summary>
    public sealed class ExitSpec
    {
        public readonly string AircraftId, SeatId;
        public readonly ExitControl Control;
        public readonly ExitSide Side;
        /// <summary>Door: the model nodes the handle can be bound to, first found wins (the Cub's DoorLower_R carries the
        /// latch). Empty: proxy only. Canopy types use their latch / jettison grabs instead.</summary>
        public readonly string[] HandleNodes;
        /// <summary>The handle where the model has no node: eye-relative, cockpit frame.</summary>
        public readonly Vec3 HandleProxy;
        public readonly ExitOpening Opening;
        public readonly double StepAboveGroundM;
        /// <summary>Climb-out, outboard of the eye on <see cref="Side"/>: the sill, the step, the ground point (m), and
        /// how far aft of the eye the ground point is (off a low wing's trailing edge root).</summary>
        public readonly double SillOutM, StepOutM, GroundOutM, GroundAftM;
        /// <summary>The pilot wears an emergency parachute in this type (aerobatic and gliders, E).</summary>
        public readonly bool WearsParachute;
        /// <summary>The table entry is a guess (shape of the fleet, not this type's document): flagged in the docs.</summary>
        public readonly bool Guessed;

        public ExitSpec(string aircraftId, string seatId, ExitControl control, ExitSide side, string[] handleNodes, Vec3 handleProxy,
                        ExitOpening opening, double stepAboveGroundM, double sillOutM, double stepOutM, double groundOutM, double groundAftM,
                        bool wearsParachute, bool guessed)
        {
            AircraftId = aircraftId; SeatId = seatId; Control = control; Side = side; HandleNodes = handleNodes ?? Array.Empty<string>();
            HandleProxy = handleProxy; Opening = opening; StepAboveGroundM = stepAboveGroundM;
            SillOutM = sillOutM; StepOutM = stepOutM; GroundOutM = groundOutM; GroundAftM = groundAftM;
            WearsParachute = wearsParachute; Guessed = guessed;
        }

        /// <summary>+1 to the right, −1 to the left: the outboard sign of <see cref="Side"/> in the cockpit frame.</summary>
        public double OutSign => Side == ExitSide.Right ? 1.0 : -1.0;
    }

    /// <summary>
    /// THE FLEET'S EXITS (docs/vr-cockpit.md "Exit table"). Pitts and Extra are Ty's and the 300S egress card's; the Cub's
    /// door is its model's (DoorUpper_R / DoorLower_R, right side only); the 172's two doors, and the single right-hand
    /// door of the PA-28 and the Seminole, are the types' well-known layouts; everything marked Guessed is the shape of the
    /// type, not a document — say so before trusting it.
    /// </summary>
    public static class ExitTable
    {
        // Climb geometry by family (E): (step above ground, sill out, step out, ground out, ground aft).
        private const double LowWingStep = 0.80, BiplaneStep = 0.65, HighWingStep = 0.35, GliderStep = 0.0;

        public static ExitSpec For(string aircraftId, string seatId = null)
        {
            string id = aircraftId ?? "";
            string seat = seatId ?? "";
            bool rightSeat = seat == "copilot" || seat == "right";

            // ---- canopies with a hands-on latch (P2 / P2c) ----
            if (id.StartsWith("pitts"))
                // Canopy hinges over to the RIGHT, so out to the LEFT onto the lower wing root (ClimbOut's rule). The
                // bubble's plan (Codex): 1.48 × 0.716 m; hinge 0.36 m right of the centre-line, free edge 0.718 m.
                return Canopy(id, seat, ExitSide.Left, 1.48, 0.716, 0.36, BiplaneStep, guessed: false);
            if (id.StartsWith("extra"))
                // Extra 300S egress card: canopy hinged right, out to the left onto the wing walk.
                return Canopy(id, seat, ExitSide.Left, 2.12, 0.78, 0.343, LowWingStep, guessed: false);

            // ---- doors ----
            if (id.StartsWith("j3-cub") || id.StartsWith("pa18"))
                // Right side only: the upper half folds up under the wing, the lower half down (its model). Lower-door
                // latch at its aft upper corner, 0.40 m right of / 0.45 below / 0.10 ahead of the eye (E). Opening
                // 0.70 × 0.95 m (E), its centre 0.25 m below the eye; for the water load the lower half (bottom hinge)
                // is the one that has to be pushed open, handle at its top.
                return new ExitSpec(id, seat, ExitControl.Door, ExitSide.Right, new[] { "Grip_Door_R", "DoorLower_R", "DoorUpper_R" },
                    new Vec3(0.40, -0.45, 0.10), new ExitOpening(0.70, 0.95, 0.25, HingeEdge.Bottom, 1.0),
                    HighWingStep, 0.45, 0.80, 1.20, 0.0, wearsParachute: false, guessed: false);
            if (id.StartsWith("c172") || id.StartsWith("dhc2") || id.StartsWith("decathlon") || id.StartsWith("pa25"))
            {
                // A door each side (172, Beaver front); Decathlon and Pawnee: one door, guessed side. Forward hinge, handle
                // near the aft edge (0.85 of the width, E). Opening 0.90 × 1.00 m (E, a 172 cabin door), centre 0.30 m
                // below the eye.
                ExitSide side = id.StartsWith("decathlon") ? ExitSide.Right : rightSeat ? ExitSide.Right : ExitSide.Left;
                double s = side == ExitSide.Right ? 1.0 : -1.0;
                bool guess = !id.StartsWith("c172");
                return new ExitSpec(id, seat, ExitControl.Door, side, new[] { side == ExitSide.Right ? "Grip_Door_R" : "Grip_Door_L" },
                    new Vec3(0.42 * s, -0.40, 0.05), new ExitOpening(0.90, 1.00, 0.30, HingeEdge.Forward, 0.85),
                    HighWingStep, 0.50, 0.85, 1.30, 0.0, wearsParachute: id.StartsWith("decathlon"), guessed: guess);
            }
            if (id.StartsWith("pa28") || id.StartsWith("seminole"))
                // One cabin door, on the RIGHT: the pilot slides across and steps out onto the right wing walk, then off
                // its trailing edge. Opening 0.85 × 0.90 m (E), forward hinge; handle at 0.85 of the width.
                return new ExitSpec(id, seat, ExitControl.Door, ExitSide.Right, new[] { "Grip_Door_R" },
                    new Vec3(0.55, -0.35, 0.10), new ExitOpening(0.85, 0.90, 0.30, HingeEdge.Forward, 0.85),
                    LowWingStep, 0.70, 1.05, 1.60, 0.90, wearsParachute: false, guessed: false);
            if (id.StartsWith("cirrus"))
            {
                // Two gull-wing doors hinged at the top-front (treated as top-hinged, E).
                ExitSide side = rightSeat ? ExitSide.Right : ExitSide.Left;
                double s = side == ExitSide.Right ? 1.0 : -1.0;
                return new ExitSpec(id, seat, ExitControl.Door, side, new[] { side == ExitSide.Right ? "Grip_Door_R" : "Grip_Door_L" },
                    new Vec3(0.45 * s, -0.35, 0.05), new ExitOpening(1.00, 0.85, 0.20, HingeEdge.Top, 0.80),
                    LowWingStep, 0.55, 0.95, 1.55, 0.90, wearsParachute: false, guessed: true);
            }
            if (id.StartsWith("boeing") || id.StartsWith("dc3") || id.StartsWith("hughes"))
                // Placeholder only: a cabin door on the left; the climb path is a light aeroplane's, not a stair (E).
                return new ExitSpec(id, seat, ExitControl.Door, ExitSide.Left, new[] { "Grip_Door_L" },
                    new Vec3(-0.50, -0.35, 0.0), new ExitOpening(0.85, 1.80, 0.40, HingeEdge.Forward, 0.85),
                    HighWingStep, 0.60, 1.00, 1.50, 0.0, wearsParachute: false, guessed: true);

            // ---- open cockpits: the belt is the whole of it ----
            if (id.StartsWith("great-lakes") || id.StartsWith("stearman") || id.StartsWith("jn4") || id.StartsWith("aircam"))
                return new ExitSpec(id, seat, ExitControl.BeltOnly, ExitSide.Left, Array.Empty<string>(), Vec3.Zero,
                    new ExitOpening(0.0, 0.0, 0.0, HingeEdge.Forward, 1.0), BiplaneStep, 0.40, 0.75, 1.30, 0.40,
                    wearsParachute: id.StartsWith("great-lakes"), guessed: !id.StartsWith("great-lakes"));

            // ---- no seat-belt exit ----
            if (id.StartsWith("hang-glider") || id.StartsWith("paraglider") || id.StartsWith("target-drone"))
                return new ExitSpec(id, seat, ExitControl.None, ExitSide.Left, Array.Empty<string>(), Vec3.Zero,
                    new ExitOpening(0.0, 0.0, 0.0, HingeEdge.Forward, 1.0), 0.0, 0.0, 0.0, 0.0, 0.0, false, guessed: false);

            // ---- every other canopy: gliders, racers, warbirds — a generic canopy-release lever (E) ----
            bool glider = id.StartsWith("glider");
            double step = glider ? GliderStep : LowWingStep;
            return Canopy(id, seat, ExitSide.Left, 1.6, 0.70, 0.35, step, guessed: true, generic: true, wearsParachute: true);
        }

        private static ExitSpec Canopy(string id, string seat, ExitSide side, double lengthM, double spanM, double hingeOutboardM,
                                       double step, bool guessed, bool generic = false, bool wearsParachute = true)
        {
            // Hinge line to the plan centroid (the centre-line): HingeOutboardM; to the free edge: + half the span.
            double load = hingeOutboardM, hand = hingeOutboardM + 0.5 * spanM;
            // A generic canopy's release lever: left sill, just ahead of the eye (E). Latch-built types use their latch.
            return new ExitSpec(id, seat, ExitControl.Canopy, side, generic ? new[] { "Grip_Canopy_Release", "CanopyRelease_L" } : Array.Empty<string>(),
                generic ? new Vec3(-0.30, -0.30, 0.25) : Vec3.Zero,
                new ExitOpening(spanM, lengthM, -0.10, HingeEdge.CanopySide, hand / load, load),
                step, 0.45, 0.85, 1.45, step >= LowWingStep ? 0.90 : 0.40, wearsParachute, guessed);
        }
    }

    /// <summary>Where the aeroplane is when the pilot goes: which way out the exit takes.</summary>
    public enum EgressSituation { InFlight = 0, OnGround = 1, Afloat = 2, Submerged = 3 }

    /// <summary>The automatic exit that follows.</summary>
    public enum ExitPath
    {
        None = 0,
        /// <summary>In flight: out over the side into the air (the P2 jumper: free fall, ripcord, parachute).</summary>
        Bailout = 1,
        /// <summary>On the ground: climb out (ClimbPath), then on foot (GroundWalker).</summary>
        ClimbOutGround = 2,
        /// <summary>Afloat: climb out over the sill into the water beside it, then tread water (PilotSwim).</summary>
        IntoWater = 3,
        /// <summary>Under water: out through the opening, swim up, tread water at the surface (PilotSwim).</summary>
        SwimUp = 4,
    }

    /// <summary>
    /// "YOU CAN ALWAYS GET OUT" (Ty, 2026-11-02 13:28): release the belt, then work the door handle or the canopy release,
    /// and the exit follows by itself, by situation. This decides when it starts and which one it is; once started it
    /// stays started (one-way, until the aeroplane is reset).
    /// </summary>
    public sealed class AutoExit
    {
        /// <summary>On the ground, the aeroplane must be all but stopped first (E: a walking pace).</summary>
        public const double MaxGroundSpeedMs = 1.5;
        /// <summary>The cockpit counts as under water once this much of it is full, even with the eye clear (E).</summary>
        public const double SubmergedFlood = 0.9;

        public ExitPath Path { get; private set; } = ExitPath.None;
        public bool Started => Path != ExitPath.None;
        /// <summary>Why it has not started yet (for the log and the docs), "" once it has.</summary>
        public string Waiting { get; private set; } = "belt";

        public void Reset() { Path = ExitPath.None; Waiting = "belt"; }

        /// <summary>The exit has found it is somewhere else than it thought (a ground exit stepping down into water).</summary>
        public void Redirect(ExitPath p) { if (Started && p != ExitPath.None) Path = p; }

        /// <summary>A canopy's exit control has been worked: its latch is open, or its jettison / emergency release has
        /// fired (Ty: the Pitts' knob counts, latch or no latch — the exit then opens the latch itself), or it has gone.</summary>
        public static bool CanopyWorked(bool latchOpen, bool jettisonFired, bool canopyGone) => latchOpen || jettisonFired || canopyGone;

        /// <summary>Which situation it is. <paramref name="onGround"/>: a wheel or the airframe on the ground (not water);
        /// <paramref name="eyeUp"/>: the pilot's eye height; <paramref name="surfaceAtEye"/>: the outside water surface
        /// over it, or null.</summary>
        public static EgressSituation Classify(bool onGround, double eyeUp, double? surfaceAtEye, ICockpitWaterState water)
        {
            bool eyeUnder = surfaceAtEye.HasValue && surfaceAtEye.Value > eyeUp;
            if (eyeUnder || (water != null && water.FloodFraction >= SubmergedFlood)) return EgressSituation.Submerged;
            if (water != null && water.Phase != CockpitWaterPhase.Dry) return EgressSituation.Afloat;
            return onGround ? EgressSituation.OnGround : EgressSituation.InFlight;
        }

        /// <summary>
        /// One frame. <paramref name="beltLatched"/>: any restraint still fastened (SeatHarness.Latched; an open cockpit's
        /// belt too). <paramref name="exitWorked"/>: the canopy's latch is open, or its jettison / release pulled, or it has
        /// gone; or the door handle has been turned; always true for an open cockpit. <paramref name="doorHeld"/>: the
        /// water holds the opening shut (DoorWaterLoad.CanOpen false) — only asked under water.
        /// </summary>
        public ExitPath Step(bool beltLatched, bool exitWorked, EgressSituation s, double groundSpeedMs, bool doorHeld)
        {
            if (Started) return Path;
            if (beltLatched) { Waiting = "belt"; return ExitPath.None; }
            if (!exitWorked) { Waiting = "exit control"; return ExitPath.None; }
            switch (s)
            {
                case EgressSituation.Submerged:
                    if (doorHeld) { Waiting = "water pressure"; return ExitPath.None; }
                    Path = ExitPath.SwimUp; break;
                case EgressSituation.Afloat: Path = ExitPath.IntoWater; break;
                case EgressSituation.OnGround:
                    if (groundSpeedMs > MaxGroundSpeedMs) { Waiting = "still rolling"; return ExitPath.None; }
                    Path = ExitPath.ClimbOutGround; break;
                default: Path = ExitPath.Bailout; break;
            }
            Waiting = "";
            return Path;
        }
    }
}
