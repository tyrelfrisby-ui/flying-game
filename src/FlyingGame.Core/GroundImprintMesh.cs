using FlyingGame.Core.MathTypes;

namespace FlyingGame.Core;

/// <summary>
/// How one mark in the ground is DRAWN: a colour, and how plainly it shows at its floor and on its slumped shoulder.
///
/// It is a renderer's number, not a record's. <see cref="GroundImprints"/> says honestly that a Cub's rut on a dirt
/// strip is 3.3 mm deep; whether that reads as flattened, bruised earth or as nothing at all is decided here (and a
/// 5 mm floor in the RECORD was tried and rejected for exactly that reason — see <see cref="GroundImprints.MinDepthM"/>).
/// </summary>
public readonly struct GroundImprintLook
{
    /// <summary>The colour of the disturbed ground, 0-1 linear-ish sRGB as the rest of the renderer's palettes are.</summary>
    public readonly float R, G, B;
    /// <summary>How plainly the FLOOR of the mark shows over the undisturbed ground, 0-1.</summary>
    public readonly float Alpha;
    /// <summary>How plainly the slumped SHOULDER shows, as a fraction of <see cref="Alpha"/>: a near-vertical shoulder
    /// (turf's root mat, 60 deg) is as dark as the floor, a 32 deg ramp of sand catches the light and barely shows.</summary>
    public readonly float Shoulder01;

    public GroundImprintLook(float r, float g, float b, float alpha, float shoulder01)
    {
        R = r; G = g; B = b; Alpha = alpha; Shoulder01 = shoulder01;
    }
}

/// <summary>
/// The look of a mark, and the geometry that draws it. **Engine-free, deliberately**: the whole imprint record is
/// (docs/GROUND-IMPRINTS.md), so iOS, the Mac, the Quest and the Aero widget draw the same marks from the same code
/// and a platform cannot quietly fall behind. The engine's share is converting
/// <see cref="GroundImprintMesh.Positions"/> into its own frame and handing it to a mesh.
///
/// **No particles** (a standing prohibition of Ty's): a mark is geometry lying on the ground.
///
/// ## What one mark is
/// A 4 x 4 grid of vertices — 16 vertices, 18 triangles — over the stretch:
/// - ALONG: a cap, the stretch's own start, its end, a cap. The caps exist because a curving track is a CHAIN of
///   stretches and consecutive stretches are one physics step apart, which at a 14 m/s touchdown is 12 cm of bare
///   ground between them; a cap of half the mark's width closes that.
/// - ACROSS: the shoulder, the floor, the floor, the shoulder. The trough's width already includes the slump
///   (<see cref="GroundImprints.TroughWidthM"/>), so the outer columns are the slumped sides and are drawn at
///   <see cref="GroundImprintLook.Shoulder01"/>.
///
/// 18 triangles a mark, so 18,432 for 1,024 marks — the whole world's, every aeroplane's, forever. The record's own
/// budget note reckoned 2,048 for one flat quad each; this spends 9x that to get the fades, and it is still a fraction
/// of one of ForestBuilder's detail trees. The index buffer is FIXED (mark i always owns vertices 16i..16i+15), so
/// only vertices are ever rewritten, and 16,384 vertices stays inside a 16-bit index buffer for the Quest and the phone.
///
/// ## Where the vertices go
/// On the surface the AEROPLANE'S WHEELS are on — <see cref="WorldTerrain.GroundHeightAt"/>, the analytic ground the
/// contact physics runs against — sampled per vertex, so a mark follows the ground across its width as well as its
/// length and never stands off a slope. The drawn island mesh is that same height function sampled on a 25 m lattice
/// (IslandTerrainMesh), so the two differ by the lattice's own curvature: MEASURED over the island's land, on ground
/// flat enough to roll on, the median gap is 0 mm and the 95th percentile 38 mm, and where it is larger the AEROPLANE
/// is standing off the drawn mesh by the same amount — so a mark drawn here is always under the wheel that made it,
/// which is the only consistent choice available. <see cref="SurfaceLiftM"/> lifts it clear of a z-fight.
///
/// Every ground that marks at all is open terrain: <see cref="GroundImprints.Marks"/> is false for asphalt, a deck,
/// ice and water, so there is no runway, carrier deck or cave floor to special-case here.
/// </summary>
public static class GroundImprintDrawing
{
    /// <summary>Vertices one mark costs (a 4 x 4 grid).</summary>
    public const int VerticesPerMark = 16;
    /// <summary>Triangles one mark costs (3 x 3 quads).</summary>
    public const int TrianglesPerMark = 18;
    /// <summary>Indices one mark costs.</summary>
    public const int IndicesPerMark = 3 * TrianglesPerMark;

    /// <summary>
    /// How far the mark is lifted off the surface, m. **5 cm, and it is a MEASUREMENT, not a taste.**
    ///
    /// It is not a fudge for the record's depth — a mark is drawn ON the ground, at the ground's height, and its depth
    /// is in its colour and not in its geometry. This is the clearance that keeps it clear of the DRAWN mesh, which
    /// interpolates the same height function on a 25 m lattice and therefore stands above the analytic surface the
    /// marks (and the wheels) sit on.
    ///
    /// 2 cm was the first value and it was NOT ENOUGH, and the way that showed is worth keeping: in the first real
    /// renders the marks were plainly visible from half a metre up and **completely absent from every plan view**,
    /// which is the signature of losing the depth test face-on. The material's Offset is SLOPE-SCALED, so at a
    /// grazing angle it biases the mark by a lot and rescues it; seen flat-on the slope is zero, the bias is nothing,
    /// and the only thing holding the mark above the mesh is this number.
    ///
    /// MEASURED over the AP1 grass strip against its own 25 m lattice: the mesh stands up to **120 mm** above the
    /// analytic surface, the 95th percentile is **47 mm**, and at a 2 cm lift **14.8 %** of the strip buries the
    /// mark. (On the 5 m sub-lattice a shaped field gets at LOD0 it is ±9 mm and nothing is buried — but the LOD in
    /// use is the renderer's business, not this one's, so the lift has to survive the coarse case.) 5 cm clears the
    /// 95th percentile there and the 95th percentile of open rollable land (38 mm, class remarks).
    ///
    /// The cost of 5 cm is a mark that floats half a finger's width above the ground, which at any distance a pilot
    /// looks at his tracks from is nothing — and a mark that floats slightly beats a mark that is invisible.
    ///
    /// Straight UP, not along the surface's normal: moving a vertex along the normal moves it sideways as well, off
    /// the ground whose height it was just given, which on a slope is a bigger error than the lift itself. Lifting
    /// vertically costs a cosine of the clearance on a slope and keeps the vertex over its own ground.
    /// </summary>
    public const double SurfaceLiftM = 0.05;

    /// <summary>
    /// How far past each end of the stretch the mark is drawn, as a fraction of its own WIDTH. Half.
    ///
    /// A chain of stretches has a one-step gap at every joint (the stretch that closed ended at the previous step's
    /// contact point, the stretch that opened starts at this one's), which is up to 12 cm at a 14 m/s touchdown and
    /// millimetres at a taxi. Half a width closes it at every speed a wheel marks the ground at, because the mark's
    /// own width is the scale at which a gap in it would be seen.
    /// </summary>
    public const double CapFraction = 0.5;

    /// <summary>
    /// Where the floor of the trough ends and the slumped shoulder begins, as a fraction of the half width. 0.6.
    ///
    /// The trough is patch + 2d/tan(repose) across (<see cref="GroundImprints.TroughWidthM"/>), so the shoulders are
    /// the outer part of it by construction; 0.6 is a drawing's approximation of that split and not a second geometry.
    /// </summary>
    public const double FloorFraction = 0.6;

    /// <summary>
    /// The alpha at the very tip of a cap, for a mark whose floor is drawn at <paramref name="alpha"/>.
    ///
    /// It is DERIVED, not chosen. At a joint the two caps overlap, and alpha-blending two coverages c and c gives
    /// 1 − (1 − c)², which COMPOUNDS: two full-alpha caps over each other draw a dark bead at every joint, one every
    /// 2-4 m down a landing roll, and two zero-alpha caps leave a light band instead. The cap alpha that makes the
    /// overlap read EXACTLY the mark's own is the one that solves 1 − (1 − c)² = a:
    ///
    ///     c = 1 − √(1 − a)
    ///
    /// — 0.163 for a faint 0.3 mark (0.54 of it) and 0.613 for a saturated 0.85 one (0.72 of it), exact at both,
    /// which no fixed fraction can be. An ISOLATED end, the start of a landing roll, then fades to that and stops,
    /// which is what the end of a rut looks like, rather than being chopped off square.
    /// </summary>
    public static float CapAlpha(float alpha) =>
        1f - (float)System.Math.Sqrt(System.Math.Max(0.0, 1.0 - System.Math.Min(1.0, alpha)));

    /// <summary>
    /// The depth at which a mark is drawn at 1 − 1/e of its full strength, m. 8 mm.
    ///
    /// Alpha saturates (1 − exp(−d/this)) rather than growing with depth, because what the eye reads off a mark is
    /// CONTRAST and contrast saturates: a 20 cm rut in snow does not look five times more like a rut than a 4 cm one,
    /// it just looks like a rut. 8 mm puts the marks the record actually holds across the visible range instead of
    /// bunched at one end: a Cub's 3.3 mm rut on a dirt strip reads 0.34 of full, its 5.6 mm on mown grass 0.50, a
    /// ski's 1.9 cm track in settled snow 0.91, and its bare tyre's 11.5 cm rut in the same snow is saturated.
    /// </summary>
    public const double DepthScaleM = 0.008;

    /// <summary>
    /// The faintest a mark is ever drawn. 0.12.
    ///
    /// The record already decided what counts as a mark at all (<see cref="GroundImprints.MinDepthM"/>, 1 mm, chosen
    /// because a 5 mm floor erased a Cub's tracks on a dirt strip). Drawing what it kept at an alpha nobody can see
    /// would put that rejected floor back in the renderer, so a mark the record kept is always at least faintly there.
    /// </summary>
    public const float MinAlpha = 0.12f;

    /// <summary>
    /// How plainly each kind of mark shows at full depth. A hollow in the ground is seen by its shadow and by the
    /// colour of what has been turned up, so the order follows how much the surface has been BROKEN: a gouge and a
    /// scar tear it open, a rut compacts it with the tread pressed in, a skid smears it without compacting it
    /// (<see cref="GroundImprintKind.Skid"/>: "the rubber slides over the surface instead of laying its tread into
    /// it"), and a ski's track is the flattest, smoothest, most lightly marked thing anything leaves — which is the
    /// whole point of a ski.
    /// </summary>
    public static float PeakAlpha(GroundImprintKind kind) => kind switch
    {
        GroundImprintKind.Gouge => 0.95f,
        GroundImprintKind.Scar => 0.90f,
        GroundImprintKind.Rut => 0.85f,
        GroundImprintKind.Skid => 0.70f,
        GroundImprintKind.Track => 0.60f,
        _ => 0.80f,
    };

    /// <summary>
    /// How much darker the disturbed ground is than the undisturbed, 0-1 of the surface's own colour. A mark is
    /// mostly its own SHADOW, and these are read off the renderer's own light model rather than chosen.
    ///
    /// FlyingGame/GroundImprint.shader lights a mark exactly as FlyingGame/IslandTerrain lights the ground it lies
    /// on — sky + sun·d, with sky (.50, .57, .64), sun (.54, .50, .42) and d the sun's own dot product. Ground that a
    /// hollow's own walls keep the sun off is lit by the SKY ALONE, which is .50/.94, .57/.98 and .64/.98 of the lit
    /// ground: **0.59**. That is where a compaction's number comes from — a rut and a ski track are the surface's own
    /// material, lying in the surface's own shade.
    ///
    /// A GOUGE or a SCAR has torn the surface open and brought up what was under it, which on anything that marks is
    /// damper and darker than the dried, bleached top: 0.75 of the surface's albedo, and in the same shade, so
    /// 0.59 × 0.75 = 0.44. A SKID has smeared the surface without opening a hollow in it
    /// (<see cref="GroundImprintKind.Skid"/>), so it has less of a wall to shade itself with and brings nothing up:
    /// two thirds of the way back to the lit ground, 0.66. That a skid on soft ground marks it LESS than a rolling
    /// tyre does is not a slip — a locked wheel's famous black streak is on PAVEMENT, where
    /// <see cref="GroundImprints.Marks"/> is false and the streak is <see cref="GroundMaterialRow.SkidMark"/>, not
    /// geometry at all.
    /// </summary>
    public static float Darkening(GroundImprintKind kind) => kind switch
    {
        GroundImprintKind.Gouge or GroundImprintKind.Scar => 0.44f,   // torn open, in shade: the damp subsoil
        GroundImprintKind.Skid => 0.66f,                              // smeared, not opened: little to shade it
        _ => 0.59f,                                                   // compacted: the same material, in its own shade
    };

    /// <summary>
    /// How plainly a slumped shoulder shows against the floor of the mark, 0-1: sin of the ground's angle of repose
    /// (<see cref="GroundImprints.ReposeAngleDeg"/>).
    ///
    /// The shoulder is a ramp from the undisturbed surface down to the floor, and how dark it reads is how steeply it
    /// turns away from the light — the sine of its slope. Turf's root mat holds a 60 deg shoulder and reads at 0.87,
    /// nearly as dark as the floor, which is why a tyre rut in grass has a visibly sharp edge; dry sand's 32 deg
    /// shoulder reads at 0.53 and the mark fades out at its sides. Nothing is tuned: the angle is the soil table's.
    /// </summary>
    public static float ShoulderAlpha01(GroundMaterial m) =>
        (float)System.Math.Sin(GroundImprints.ReposeAngleDeg(m) * System.Math.PI / 180.0);

    /// <summary>
    /// How a mark is drawn: the surface's own palette, darkened for the kind of disturbance, at an alpha that
    /// saturates with the depth the record holds.
    ///
    /// The colour comes from <see cref="CrashDust.For"/> — the per-surface palette the dust of a crash already uses,
    /// which is keyed on the same <see cref="WorldTerrain.Surface"/> the physics decided (Ty 2026-11-04: "Crashing on
    /// snow kicks up BROWN dust; it must be white"). Reusing it is the point: the mark cannot disagree about what the
    /// ground is made of with the dust that was thrown making it, nor with the friction the slide was given.
    /// </summary>
    public static GroundImprintLook For(GroundImprintKind kind, GroundMaterial material, double depthM)
    {
        CrashDust.Look dust = CrashDust.For((int)GroundMaterials.Row(material).Physics);
        float k = Darkening(kind);
        float a = MinAlpha;
        if (double.IsFinite(depthM) && depthM > 0)
        {
            double sat = 1.0 - System.Math.Exp(-depthM / DepthScaleM);
            a = (float)System.Math.Max(MinAlpha, PeakAlpha(kind) * sat);
        }
        return new GroundImprintLook(dust.R * k, dust.G * k, dust.B * k, a, ShoulderAlpha01(material));
    }

    /// <inheritdoc cref="For(GroundImprintKind, GroundMaterial, double)"/>
    public static GroundImprintLook For(in GroundImprint k) => For(k.Kind, k.Material, k.DepthM);

    /// <summary>
    /// The fixed index buffer for <paramref name="markCapacity"/> marks: mark i owns vertices 16i..16i+15, so the
    /// triangles never change and only vertices are ever rewritten. Wound for a front face looking down on the ground
    /// (the material draws both sides anyway, so a mark seen from under an overhang is still there).
    /// </summary>
    public static int[] Indices(int markCapacity)
    {
        int n = System.Math.Max(0, markCapacity);
        var idx = new int[n * IndicesPerMark];
        int w = 0;
        for (int m = 0; m < n; m++)
        {
            int b = m * VerticesPerMark;
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                {
                    int v00 = b + r * 4 + c, v10 = v00 + 4, v01 = v00 + 1, v11 = v10 + 1;
                    idx[w++] = v00; idx[w++] = v10; idx[w++] = v11;
                    idx[w++] = v00; idx[w++] = v11; idx[w++] = v01;
                }
        }
        return idx;
    }

    /// <summary>
    /// Lay out one mark's 16 vertices at <paramref name="at"/> in <paramref name="positions"/> (world NED, on the
    /// surface), <paramref name="normals"/> (the surface's own outward normal) and <paramref name="rgba"/> (four
    /// floats a vertex). Nothing is allocated.
    /// </summary>
    public static void Build(in GroundImprint k, Vec3[] positions, Vec3[] normals, float[] rgba, int at)
    {
        int v0 = at * VerticesPerMark;
        GroundImprintLook look = For(in k);

        // The mark's own frame, on the ground it lies on. The normal is taken at the MIDPOINT and used for the whole
        // mark: a stretch is broken as soon as it strays 0.3 of its own width off its line IN 3D
        // (GroundImprintField.StraightTolerance), so no stretch spans enough undulation for one normal to be wrong.
        Vec3 start = k.StartWorld, end = k.EndWorld;
        Vec3 mid = (start + end) * 0.5;
        Vec3 up = WorldTerrain.GroundNormalAt(mid.X, mid.Y);
        double ul = up.Length;
        up = ul > 1e-9 ? up / ul : new Vec3(0, 0, -1);

        Vec3 along = end - start;
        along -= up * Vec3.Dot(along, up);                 // the lie of the mark, in the surface
        double al = along.Length;
        // A stretch only one step old has no length and so no direction; it is a square patch, and which way round a
        // square patch lies cannot be seen.
        if (al > 1e-6) along /= al; else { along = PerpendicularTo(up); al = 0; }
        Vec3 across = Vec3.Cross(up, along);
        double cl = across.Length;
        across = cl > 1e-9 ? across / cl : PerpendicularTo(up);

        double half = 0.5 * System.Math.Max(GroundImprints.MinWidthM, k.WidthM);
        double floor = FloorFraction * half;
        double cap = CapFraction * 2.0 * half;

        // Along: cap before the start, the start, the end, cap past it. Across: shoulder, floor, floor, shoulder.
        double s0 = -cap, s1 = 0, s2 = al, s3 = al + cap;
        float capA = look.Alpha > 0 ? CapAlpha(look.Alpha) / look.Alpha : 0f;
        for (int r = 0; r < 4; r++)
        {
            double s = r == 0 ? s0 : r == 1 ? s1 : r == 2 ? s2 : s3;
            float rowA = r == 0 || r == 3 ? capA : 1f;
            for (int c = 0; c < 4; c++)
            {
                double u = c == 0 ? -half : c == 1 ? -floor : c == 2 ? floor : half;
                float colA = c == 0 || c == 3 ? look.Shoulder01 : 1f;
                Vec3 p = start + along * s + across * u;
                // On the ground, then clear of it: the height is the terrain's AT THIS VERTEX, so the mark follows the
                // slope across its width as well as along its length. The lift is straight up and not along the
                // normal, so the vertex stays over the ground whose height it was given; on a slope that costs a
                // cosine of the clearance, which the shader's depth bias covers.
                p = new Vec3(p.X, p.Y, -WorldTerrain.GroundHeightAt(p.X, p.Y) - SurfaceLiftM);
                int vi = v0 + r * 4 + c;
                positions[vi] = p;
                normals[vi] = up;
                int ci = vi * 4;
                rgba[ci] = look.R; rgba[ci + 1] = look.G; rgba[ci + 2] = look.B;
                rgba[ci + 3] = look.Alpha * rowA * colA;
            }
        }
    }

    /// <summary>Collapse one mark's vertices to a point so it draws nothing (an unused slot in the ring).</summary>
    public static void Blank(Vec3[] positions, Vec3[] normals, float[] rgba, int at)
    {
        int v0 = at * VerticesPerMark;
        for (int i = 0; i < VerticesPerMark; i++)
        {
            positions[v0 + i] = Vec3.Zero;
            normals[v0 + i] = new Vec3(0, 0, -1);
            int ci = (v0 + i) * 4;
            rgba[ci] = 0; rgba[ci + 1] = 0; rgba[ci + 2] = 0; rgba[ci + 3] = 0;
        }
    }

    /// <summary>Any unit vector at right angles to <paramref name="up"/>.</summary>
    private static Vec3 PerpendicularTo(Vec3 up)
    {
        Vec3 a = System.Math.Abs(up.Z) < 0.9 ? new Vec3(0, 0, -1) : new Vec3(1, 0, 0);
        Vec3 p = Vec3.Cross(up, a);
        double l = p.Length;
        return l > 1e-9 ? p / l : new Vec3(1, 0, 0);
    }
}

/// <summary>
/// The whole world's marks as drawable geometry, kept in step with <see cref="GroundImprints.World"/> and rebuilt
/// INCREMENTALLY: at most a dozen stretches change in a physics step (one per contact in contact), so a refresh
/// rewrites those marks' vertices and leaves the other thousand alone.
///
/// Nothing is allocated after construction. A renderer calls <see cref="Refresh"/>, and when it returns true it
/// uploads <see cref="Positions"/>, <see cref="Normals"/> and <see cref="Rgba"/> — converting to its own frame — with
/// <see cref="Indices"/>, which never change.
///
/// It does NOT need to be refreshed every frame. <see cref="GroundImprintField.Version"/> changes on every step a
/// wheel is rolling, and the mark the record has already grown to where the wheel is; a renderer that refreshes at
/// 20 Hz leaves the growing end of a rut up to 0.7 m behind a 14 m/s wheel, which is under the aeroplane, and spends
/// a twentieth of the vertex traffic. The rate is the renderer's to pick: nothing here keeps a clock.
/// </summary>
public sealed class GroundImprintMesh
{
    private readonly GroundImprintField _field;
    private readonly GroundImprint[] _mirror;
    private int _version = int.MinValue;

    /// <summary>Marks the buffers hold room for: the field's own capacity.</summary>
    public int Capacity { get; }
    /// <summary>Marks with geometry in the buffers after the last <see cref="Refresh"/>.</summary>
    public int Count { get; private set; }
    /// <summary>Vertex positions, world NED, <see cref="GroundImprintDrawing.VerticesPerMark"/> per mark.</summary>
    public Vec3[] Positions { get; }
    /// <summary>Vertex normals, world NED: the surface's own outward normal under each mark.</summary>
    public Vec3[] Normals { get; }
    /// <summary>Vertex colours, four floats (r, g, b, a) a vertex.</summary>
    public float[] Rgba { get; }
    /// <summary>The triangles. Fixed for the life of this object: a mark's slot owns the same vertices forever.</summary>
    public int[] Indices { get; }
    /// <summary>Which marks <see cref="Refresh"/> rewrote, so a renderer can upload a sub-range if it wants to.</summary>
    public bool[] Changed { get; }
    /// <summary>Marks rewritten by the last <see cref="Refresh"/>: what the incremental rebuild actually cost.</summary>
    public int ChangedCount { get; private set; }
    /// <summary>Triangles with geometry in them (the rest are collapsed to a point and draw nothing).</summary>
    public int Triangles => Count * GroundImprintDrawing.TrianglesPerMark;

    public GroundImprintMesh(GroundImprintField field)
    {
        _field = field ?? throw new System.ArgumentNullException(nameof(field));
        Capacity = field.Capacity;
        _mirror = new GroundImprint[Capacity];
        int verts = Capacity * GroundImprintDrawing.VerticesPerMark;
        Positions = new Vec3[verts];
        Normals = new Vec3[verts];
        Rgba = new float[verts * 4];
        Changed = new bool[Capacity];
        Indices = GroundImprintDrawing.Indices(Capacity);
        for (int i = 0; i < Capacity; i++) GroundImprintDrawing.Blank(Positions, Normals, Rgba, i);
    }

    /// <summary>
    /// Bring the geometry up to date with the field. Returns true if anything changed — including the field having
    /// been <see cref="GroundImprints.Clear"/>ed, which blanks every mark.
    /// </summary>
    public bool Refresh()
    {
        int v = _field.Version;
        if (v == _version) { ChangedCount = 0; return false; }
        _version = v;
        ChangedCount = 0;
        int count = _field.Count;
        for (int i = 0; i < Capacity; i++)
        {
            bool live = i < count;
            GroundImprint now = live ? _field[i] : default;
            bool was = i < Count;
            if (live == was && Same(in now, in _mirror[i])) { Changed[i] = false; continue; }
            _mirror[i] = now;
            if (live) GroundImprintDrawing.Build(in now, Positions, Normals, Rgba, i);
            else GroundImprintDrawing.Blank(Positions, Normals, Rgba, i);
            Changed[i] = true;
            ChangedCount++;
        }
        Count = count;
        return ChangedCount > 0;
    }

    /// <summary>Is this the same stretch, drawn the same way? Only what the geometry and the colour are built from —
    /// a mark's TimeSec moving on without its shape changing is not a reason to rewrite sixteen vertices.</summary>
    private static bool Same(in GroundImprint a, in GroundImprint b) =>
        a.Kind == b.Kind && a.Material == b.Material
        && a.WidthM == b.WidthM && a.DepthM == b.DepthM
        && a.StartWorld.X == b.StartWorld.X && a.StartWorld.Y == b.StartWorld.Y && a.StartWorld.Z == b.StartWorld.Z
        && a.EndWorld.X == b.EndWorld.X && a.EndWorld.Y == b.EndWorld.Y && a.EndWorld.Z == b.EndWorld.Z;
}
