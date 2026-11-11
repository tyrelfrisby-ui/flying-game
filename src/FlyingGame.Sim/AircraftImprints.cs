using FlyingGame.Core;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim;

/// <summary>
/// Laying this aeroplane's marks in the ground, every physics step.
///
/// Ty 2026-11-10: *"if it is penetrating it should leave imprints behind as it slides and rolls displacing the dirt or
/// snow etc"*. This is the half that READS the contact and writes the record; <see cref="GroundImprints"/> holds the
/// soil mechanics and the ring, and the renderer draws the ring. Nothing here computes a depth of its own — that was
/// the whole point of putting it behind the contact rather than beside it.
///
/// ## Two sources, because there are two kinds of contact
/// - **The gear and the hull** come through <see cref="Aircraft.SurfaceContacts"/>, which the surface-contact pass
///   fills from every tyre, wheel-ski, ski leg, float and skid in contact (lane/claude-surface-tracks PART A, taken
///   forward here). Each already carries its own footprint width, its sinkage and its slip, so a rut is the tyre's
///   own footprint and a ski track is the ski's own running surface.
/// - **The airframe** comes through <see cref="Aircraft.ContactPoints"/>, which the contact model records on the
///   step's first stage (<c>AirframeContact.Compute(..., record: stage &lt;= 0)</c>): per hard point, the penetration,
///   the normal load, the slip speed and where in the world it is. A wingtip bow dragging, a belly ploughing down a
///   hillside, a tailskid's line through a turn.
///
/// ## Depth: taken, not derived
/// A TYRE's <see cref="SurfaceContact.DepthM"/> is Bekker compaction sinkage (<see cref="TireModel.GroundSinkageM"/>),
/// which is a permanent set by definition, so it is the rut as it stands. An AIRFRAME point's
/// <see cref="ContactPoint.DepthM"/> is the contact spring's elastic penetration, so only
/// <see cref="GroundImprints.PlasticRetention01"/> of it stays (<see cref="GroundImprints.ScarDepthM"/>). The
/// distinction is laboured because two models quietly disagreeing about one rut is the failure to avoid here.
///
/// ## Where a mark is put
/// On the SURFACE, not at the part. A contact reports where the part is, which is <em>inside</em> the ground by its
/// penetration; the mark's floor is there but the stretch is recorded at the surface the part came through, so a
/// renderer laying a quad on the terrain does not have to undo the penetration to find the ground again.
/// </summary>
public sealed partial class Aircraft
{
    /// <summary>
    /// The marks this aeroplane is laying. One slot per gear leg, then one per airframe contact point, so a track index
    /// is a fixed offset and the step path needs no dictionary (the J-3 was found 8.8x too expensive on 2026-11-09 with
    /// a ConditionalWeakTable in the hot path; nothing of that shape goes in here).
    /// </summary>
    public GroundImprintPen Imprints { get; private set; } = new(GroundImprints.World, 1, -1);

    /// <summary>Where the airframe hard points' track slots begin: after the gear legs'.</summary>
    private int _imprintAirframeBase;

    /// <summary>Next owner id handed out, so each aeroplane's marks can be told apart (GroundImprint.Owner).</summary>
    private static int _imprintOwners;

    private void BuildImprints()
    {
        _imprintAirframeBase = Config.Gear.Count;
        int owner = System.Threading.Interlocked.Increment(ref _imprintOwners);
        Imprints = new GroundImprintPen(GroundImprints.World, _imprintAirframeBase + _contacts.Count, owner);
    }

    /// <summary>
    /// Lay this step's marks. Called once per step, after the surface contacts have been collected and while the
    /// airframe contact points still hold what the step's first stage recorded.
    /// </summary>
    private void LayImprints(double timeSec)
    {
        GroundImprintPen pen = Imprints;
        pen.BeginStep();
        LayGearImprints(pen, timeSec);
        LayAirframeImprints(pen, timeSec);
        pen.EndStep();
    }

    /// <summary>Tyres, skis, floats and skids, from the surface-contact buffer.</summary>
    private void LayGearImprints(GroundImprintPen pen, double timeSec)
    {
        SurfaceContactBuffer buf = SurfaceContacts;
        for (int i = 0; i < buf.Count; i++)
        {
            ref readonly SurfaceContact k = ref buf[i];
            // A float on the water leaves a wake, not an imprint: GroundImprints.Marks(Water) is false, but skip it
            // here too so a float's slot is not churned every step of a water taxi.
            if (k.Material == GroundMaterial.Water) continue;
            // Only the gear legs have a reserved slot. A ski leg added by the ski model reports its own leg index
            // (SurfaceContacts.AddSki), so it lands in the same slot its wheel would have; that is correct, because a
            // leg is on its wheel or on its ski and never both.
            if ((uint)k.Source >= (uint)_imprintAirframeBase) continue;

            // The surface the part came through: the contact tells us where it is (SurfaceContact.SurfaceWorld), which
            // is NOT the part's own position — that is inside the ground by the leg's and the tyre's deflection.
            double depth = DepthOfGearContact(in k, k.Source < _wheels.Length ? _wheels[k.Source] : null);
            Vec3 at = k.SurfaceWorld;
            GroundImprintKind kind = GroundImprints.KindOf(k.Type, k.SlipWorld.Length, k.GroundVelocityWorld.Length, k.Locked);
            // A tyre's footprint, a ski's running surface, a float's beam: the contact measured its own patch. A skid
            // or a foot carries only an estimated breadth, and the bearing capacity is the better answer there, so it
            // is left to the pen (0) rather than asserting a width nothing measured.
            double patch = k.Type == SurfaceContactType.Skid || k.Type == SurfaceContactType.Foot ? 0.0 : k.WidthM;
            pen.Lay(k.Source, kind, k.Material, at, patch, depth, k.LoadN, timeSec);
        }
    }

    /// <summary>
    /// How deep a gear contact's mark is, m. Two cases, and which one applies is decided by whether the physics has a
    /// SINKAGE for this part, never by the part's type.
    ///
    /// **It has one** (<see cref="SurfaceContact.DepthM"/> non-zero: a tyre's Bekker compaction, a ski model's own
    /// sinkage, a float's draft). A sinkage is the soil's PERMANENT set by definition, so it is the mark as it stands,
    /// with no retention applied.
    ///
    /// **It has none** — a bearing ski on this tip, a skid shoe, a float keel on land, a pilot's feet: nothing in the
    /// physics computes how far they compact the ground. Their mark is the plastic part of the leg's whole contact
    /// compression (<see cref="SurfaceContact.PenetrationM"/>), exactly as an airframe hard point's is, because that
    /// compression is the same kind of number — an elastic spring deflection. Without this a Cub on SKIS left no track
    /// at all, which is the one surface Ty operates on where the mark is most of the point.
    ///
    /// ONE BOUND is applied to a tyre, and it is geometric, not cosmetic: a tyre's rut cannot be deeper than the tyre's
    /// own rolling radius. Past that the axle is at the surface and the gear leg is on the ground, so whatever is
    /// making the mark is no longer the tyre.
    ///
    /// It is needed because <see cref="TireModel.GroundSinkageM"/> inverts Bekker's STEADY-STATE compaction relation
    /// (R/W = z/((n+1)l)), and handing a steady-state relation an impulsive load gives an impulsive answer: a J-3 put
    /// down on grass at 14 m/s reported a 306 mm rut for the one step of the touchdown spike, against the 3.8 mm it
    /// settles to standing on the same grass (measured). That is a fault in the number where it is MADE, not here, and
    /// it is flagged for the owner rather than papered over; this bound only stops one step's spike from being drawn as
    /// a trench. REJECTED: recomputing the sinkage from a filtered load, which would put a second rut model in the
    /// codebase beside the physics' own — the exact drift this split exists to prevent.
    /// </summary>
    private static double DepthOfGearContact(in SurfaceContact k, WheelState? w)
    {
        double d = System.Math.Max(0, k.DepthM);
        if (d > 0)
        {
            if (k.Type == SurfaceContactType.Tire && w != null && w.RollingRadiusM > 0) d = System.Math.Min(d, w.RollingRadiusM);
            return d;
        }
        return GroundImprints.ScarDepthM(k.Material, k.PenetrationM);
    }

    /// <summary>
    /// Wingtips, bellies, noses, fins, tailskids and gear stubs, from the airframe contact points as the step's first
    /// stage recorded them.
    ///
    /// Points of one PATCH are one contact (<see cref="ContactPoint.Patch"/>: a wingtip lying flat is five points of
    /// one patch, sharing one stiffness), so the mark is laid once per patch, at the point of that patch carrying the
    /// most load and from the whole patch's load — otherwise a wingtip would lay five overlapping gouges and its width,
    /// which comes from the load, would be five times too narrow.
    /// </summary>
    private void LayAirframeImprints(GroundImprintPen pen, double timeSec)
    {
        int i = 0;
        while (i < _contacts.Count)
        {
            int patch = _contacts[i].Patch;
            // Patch −1 means a point that is its own contact; the run below is then just that one point.
            int end = i + 1;
            if (patch >= 0) while (end < _contacts.Count && _contacts[end].Patch == patch) end++;

            // The point of the patch that is DEEPEST is where the mark is put and what decides its kind; the whole
            // patch's load is what decides its width. Whether the part is SLIDING is deliberately not consulted: a
            // belly resting on soft ground presses a hollow into it without going anywhere, and that hollow is the
            // thing Ty asked to stay ("I want the penetration to show and stay as ground scars").
            ContactPoint? deepest = null;
            double loadN = 0, maxDepth = 0;
            for (int j = i; j < end; j++)
            {
                ContactPoint p = _contacts[j];
                // A propeller DISC station is not a part pressing on the ground; a blade strike is its own event
                // (ContactEventKind.PropBladeStrike) and it chops rather than ploughs.
                if (p.PropDisc) continue;
                if (!(p.NormalN > 0) || p.SurfaceCode < 0) continue;     // in the air, on a solid, or in the water
                loadN += p.NormalN;
                if (p.DepthM > maxDepth) { maxDepth = p.DepthM; deepest = p; }
            }
            if (deepest is null || !(loadN > 0)) { for (int j = i; j < end; j++) pen.Lift(_imprintAirframeBase + j); i = end; continue; }

            GroundMaterial m = GroundMaterials.Of((WorldTerrain.Surface)deepest.SurfaceCode);
            double depth = GroundImprints.ScarDepthM(m, maxDepth);
            // World up: the airframe contact model works against the ground's HEIGHT (a vertical penetration), so the
            // surface above a hard point is straight up from it. The gear path, which samples the ground's normal, uses
            // that normal instead.
            Vec3 at = SurfaceOf(deepest.WorldPos, new Vec3(0, 0, -1), maxDepth);
            // Width is left to the pen (patch 0): a ContactPoint has no breadth, and the ground's bearing capacity is
            // the one honest way to size it from the load it is carrying.
            pen.Lay(_imprintAirframeBase + i, GroundImprints.KindOf(deepest.Component), m, at, 0.0, depth, loadN, timeSec);
            for (int j = i + 1; j < end; j++) pen.Lift(_imprintAirframeBase + j);
            i = end;
        }
    }

    /// <summary>The undisturbed surface above a part that is <paramref name="depthM"/> into the ground, along the
    /// surface's own normal <paramref name="upWorld"/>.</summary>
    private static Vec3 SurfaceOf(Vec3 partWorld, Vec3 upWorld, double depthM)
    {
        if (!(depthM > 0)) return partWorld;
        double l = upWorld.Length;
        return l > 1e-6 ? partWorld + upWorld * (depthM / l) : partWorld;
    }
}
