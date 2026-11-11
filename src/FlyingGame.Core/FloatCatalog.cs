namespace FlyingGame.Core;

/// <summary>
/// How a size's installed weight was arrived at. The catalog derives every weight from one ratio
/// (<see cref="FloatCatalog.InstalledLbPerDisplacementLb"/>) rather than hand-writing nineteen numbers, so this says
/// whether the row has a published weight to check the rule against.
/// </summary>
public enum FloatWeightBasis
{
    /// <summary>A published installed weight exists for this size and is in <see cref="FloatSize.PublishedStraightLb"/>
    /// — the rule is being checked, not trusted.</summary>
    Measured,
    /// <summary>No published weight, but the size sits inside the span the measured ones cover.</summary>
    Interpolated,
    /// <summary>No published weight and above every measured size. The rule is extrapolating, and the one large
    /// comparable says it runs HEAVY up there — see the class comment on the Beaver.</summary>
    Extrapolated,
}

/// <summary>
/// One float RATING, as the catalog lists it. Figures are kept in the units their sources print them in (pounds) and
/// converted once; <see cref="Source"/> says where each came from and which are derived.
///
/// THE NUMBER IS THE DISPLACEMENT OF ONE FLOAT, in pounds of fresh water. That is the convention the real aluminium
/// floats this family is modelled on use — a "2000" is 2,000 lb of buoyancy PER FLOAT, so a pair displaces 4,000 lb —
/// and it is the convention FloatHydro and DisplacementTests already measure against (HydroJ3Tests: the float is named
/// for the pounds it displaces). Everything else on this class falls out of that one number.
///
/// STRAIGHT AND AMPHIBIAN ARE SEPARATE MODELS, NOT A SWITCH. This was the single biggest thing the research corrected:
/// the real range has no "amphibious version of the 2870". It has the 2870, a straight float, and the 2700, 2705 and
/// 2790, which are amphibians on the same hull with their own ratings — a wheel well eats displacement, so an
/// amphibian's rating runs a few per cent BELOW its straight sister's. So a row here is a rating, and
/// <see cref="Straight"/> and <see cref="Amphibian"/> say which versions were built at it. Some ratings are one, some
/// the other, and the 4580 is both.
/// </summary>
public sealed class FloatSize
{
    public string Id = "", Name = "", FamilyId = "", Source = "";

    /// <summary>Displacement of ONE float, lb of fresh water. The rating, and the size's name.</summary>
    public double DisplacementLb;

    /// <summary>A STRAIGHT float was built at this rating.</summary>
    public bool Straight;

    /// <summary>An AMPHIBIAN — the same hull with retractable wheels — was built at this rating. False is the
    /// conservative default: a rating is only true here when there is evidence for it, named in <see cref="Source"/>.</summary>
    public bool Amphibian;

    /// <summary>Installed weight of the STRAIGHT-float system, lb: both floats, struts, wires, fittings and water
    /// rudders — everything the conversion bolts on. Derived; see <see cref="Basis"/>. Populated on every row, but
    /// only OFFERED where <see cref="Straight"/> is true.</summary>
    public double StraightInstalledLb;

    /// <summary>Installed weight of the AMPHIBIAN system, lb. Derived from the straight weight by one factor, and
    /// only offered where <see cref="Amphibian"/> is true. NO published weight for a real amphibian at any rating was
    /// found, so this number has nothing measured behind it at all.</summary>
    public double AmphibianInstalledLb;

    /// <summary>The PUBLISHED installed weight of the straight-float pair, lb, where a certificate prints one; 0
    /// where none was found. The rule is checked against this, never fitted to the row.</summary>
    public double PublishedStraightLb;

    public FloatWeightBasis Basis;

    public const double Lb = 0.45359237;

    /// <summary>What the PAIR displaces, lb — the number FAR 23.751 is written against.</summary>
    public double PairDisplacementLb => 2 * DisplacementLb;

    /// <summary>
    /// The heaviest aeroplane this size may legally carry: 14 CFR 23.751(a)(1) wants the main floats to have 80 %
    /// more buoyancy than is needed to support the maximum weight in fresh water, i.e. pair ≥ 1.8 W, so
    /// W ≤ 2N/1.8 = N/0.9.
    /// </summary>
    public double MaxGrossLb => PairDisplacementLb / FloatCatalog.ReserveBuoyancyFactor;

    public double DisplacementKg => DisplacementLb * Lb;
    public double StraightInstalledKg => StraightInstalledLb * Lb;
    public double AmphibianInstalledKg => AmphibianInstalledLb * Lb;

    /// <summary>Was this version built at this rating?</summary>
    public bool Offers(bool amphibian) => amphibian ? Amphibian : Straight;

    /// <summary>Installed weight of the version asked for, lb.</summary>
    public double InstalledLb(bool amphibian) => amphibian ? AmphibianInstalledLb : StraightInstalledLb;

    public double InstalledKg(bool amphibian) => InstalledLb(amphibian) * Lb;

    /// <summary>Reserve buoyancy this size gives an aeroplane of <paramref name="grossLb"/>: pair displacement over
    /// weight. 1.8 is the floor; the real light installations run 2.0–2.3.</summary>
    public double ReserveRatio(double grossLb) => grossLb > 0 ? PairDisplacementLb / grossLb : 0;
}

/// <summary>One float FAMILY — a material and a maker's range, as the player picks it.</summary>
public sealed class FloatCatalogFamily
{
    public string Id = "", Name = "", Source = "";
    public IReadOnlyList<FloatSize> Sizes = System.Array.Empty<FloatSize>();
}

/// <summary>
/// THE FLOAT CATALOG (Ty 2026-11-11: "Use the EDO drawings and make every model they have available straight floats
/// and amphibs. The game selects the nearest to the gross weight and offers them an amphib choice if one exists like
/// the j3 cub does not for example"). Every float any aeroplane in the game can be put on, in one table, so that a
/// rating's displacement, installed weight and amphibian availability have one source of truth.
///
/// THE PLAYER-FACING NAME IS "ALUMINUM FLOATS", NEVER THE MAKER'S (Ty, same conversation: "Just call this 'aluminum'
/// floats and we'll make a 'composite floats' later with other selections"). The fleet already dodges live marks this
/// way — "J-3 Cub-like", "Pitts S-2B-like" — and this repo is public and heading for the App Store. The engineering is
/// modelled on Edo Aircraft Corporation's real range because that is where the measurements are; the source comments
/// below name those certificates and drawings, as this codebase's rule requires, and the UI never shows them. That is
/// why the family is a FAMILY with SIZES and not a flat list: "Composite floats" is a second family with its own
/// weight ratio and its own size ladder, and nothing above the <see cref="Families"/> array has to change to add it.
///
/// THE WEIGHT RULE. Nineteen hand-written weights would be nineteen places to be wrong. So every installed weight here
/// is DERIVED from one ratio:
///
///   installed system lb ≈ 0.132 × (displacement rating per float)
///
/// It is fitted to the two weights taken on the COMPLETE installation — everything that is really bolted on:
///  - 60-1320 (the J-3's): owner-weighed 171–182 lb installed with all rigging. 0.132 × 1320 = 174 lb — inside that.
///  - 89/89A-2000: 263 lb with water rudders and all attachment struts, wires and fittings, per the Edo drawings.
///    0.132 × 2000 = 264 lb.
///
/// AND IT IS CHECKED, NOT TRUSTED, against a second and quite different population: the absolute float weights the FAA
/// Aircraft Specifications print in their equipment lists, per the AIRFRAMER's installation drawing —
///
///   1140 → 147 lb (A-691 Rev 34 Item 151)        0.129
///   1320 → 171 lb (A-691 Rev 34 Item 206)        0.130
///   1400 → 174 lb (A-768 Rev 35 Item 402(b))     0.124
///   1650 → 208 lb (A-768 Rev 35 Item 402(a))     0.126
///   2000 → 251 lb (A-799 Rev 55 Item 209(a))     0.126
///
/// — which fit a line of 0.1264, about 5 % under this catalog's 0.132. THE GAP IS REAL AND THE CATALOG IS ON THE RIGHT
/// SIDE OF IT: the certificate figure for the J-3's own floats is 171 lb and the owner weighed the same installation
/// at up to 182 lb, +6 %, which is the same bias and in the same direction. An equipment list counts the float
/// installation as its drawing defines it; an aeroplane carries what is actually on it. The catalog therefore sits
/// 0 to +6 % above every published figure and never below one, and FloatCatalogTests pins that band. Dropping the
/// constant to 0.1264 is a one-line change if that reading is ever overturned.
///
/// WHERE IT IS EXTRAPOLATION, AND WHICH WAY IT LEANS. Above 2,000 lb there is no published weight for ANY size in this
/// range — the big types' certificates refer to the aeroplane's own equipment list instead of itemising the float, and
/// the parts lists are parts-only. The one large comparable sits LOW: a modern straight-float system for a DHC-2
/// Beaver weighs 491 lb, where the ratio at the Beaver's 4,580-lb float predicts 605 lb — 19 % high, and wider still
/// if that figure is for a bigger float than 4,580. A float is a shell, and a shell's weight grows more slowly than
/// the volume it encloses, so a constant lb-per-lb-of-displacement ratio SHOULD run heavy at the big end. The ratio is
/// kept anyway rather than bent to one unmatched point, and every row above 2,000 lb is flagged
/// <see cref="FloatWeightBasis.Extrapolated"/> so a configurator can say so.
///
/// THE AMPHIBIAN UPLIFT IS THE WEAKEST NUMBER HERE. An amphibian is the same hull with a retractable main wheel in
/// each float, a bow or tail wheel, wells, hydraulics and brakes. NOT ONE published weight for a real amphibian in
/// this range was found — not on a certificate, not in a parts list, not in the field. So the uplift is taken from a
/// modern maker's published seaplane-vs-amphibian system weights on comparable aircraft — PA-18 280 → 427 lb, Cessna
/// 172 305 → 468 lb, DHC-2 Beaver 491 → 694 lb, i.e. ×1.53, ×1.53 and ×1.41 — and 1.5 is the constant used. Every
/// amphibian weight in this table is that factor on a derived straight weight: a derivation on a derivation.
///
/// WHICH RATINGS HAVE AN AMPHIBIAN IS EVIDENCE, NOT A FORMULA, and the evidence turned out to be the opposite shape
/// from what a formula would assume. See the table: amphibians are their own models at their own ratings, and Ty's own
/// worked example — the J-3's 1320 has none — is confirmed by its certificate containing no amphibian entry at all.
/// </summary>
public static class FloatCatalog
{
    /// <summary>14 CFR 23.751(a)(1): main float buoyancy ≥ 1.8 × maximum weight, fresh water.</summary>
    public const double ReserveBuoyancyFactor = 1.8;

    /// <summary>
    /// The sizing floor as the catalog applies it, per float: since the rating is the displacement of ONE float,
    /// pair ≥ 1.8 W becomes N ≥ 0.9 W. Checked against the real fitments this family is modelled on, N/W runs 0.90 to
    /// 1.14 — a Cessna 172 on 2000s is 0.90 to the pound, a J-3 on 1320s is 1.08.
    /// </summary>
    public const double MinDisplacementPerGrossLb = ReserveBuoyancyFactor / 2;

    /// <summary>Installed straight-float system weight per pound of rated displacement (see the class comment).</summary>
    public const double InstalledLbPerDisplacementLb = 0.132;

    /// <summary>Amphibian system weight over straight-float system weight (see the class comment — this is the
    /// weakest constant in the file).</summary>
    public const double AmphibianWeightFactor = 1.5;

    /// <summary>
    /// How far BELOW the floor a size may sit and still be offered, as a fraction. It exists because the two largest
    /// certificated installations in the real range sit exactly ON the floor and would otherwise be rejected by a
    /// rounding pound: the DHC-2 Beaver's pair of 4,580s is 9,160 lb against 1.8 × 5,090 = 9,162, and the DHC-3
    /// Otter's pair of 7,170s is 14,340 lb against 1.8 × 8,000 = 14,400 (0.02 % and 0.4 % short). Those are real,
    /// flown, type-certificated fits; a floor that throws them out is measuring its own arithmetic, not the aeroplane.
    /// 1 % covers both and is small enough that nothing else slips through.
    /// </summary>
    public const double FloorTolerance = 0.01;

    /// <summary>
    /// How far an amphibian's rating may sit from the straight float's and still be the SAME SIZE OF AEROPLANE's
    /// amphibian option, below and above. THE BAND IS LOPSIDED BECAUSE THE REAL PAIRS ARE: a wheel well is cut out of
    /// the hull, so an amphibian displaces LESS than its straight sister, not more. Measured off every pair in the
    /// range — 2700 and 2705 against the straight 2870 are −5.9 % and −5.8 %, 2790 against the 2870 is −2.8 % and
    /// against the 2960 −5.7 %, the 4580 is both versions at once so 0 %, and the single exception is the 3500
    /// against the straight 3430 at +2.0 %, because that one is a bigger hull and not the 3430 with gear bolted on.
    /// So: −15 % and +5 %, which covers all six with room.
    ///
    /// A SYMMETRIC 15 % WOULD BE WRONG IN A WAY THAT SHOWS: it reaches from the Maule's straight 2425 up to the 2700,
    /// and offers a Maule an amphibian nothing ever certificated. The rule is not "is there an amphibian bigger than
    /// this" — every amphibian in the range is bigger than a J-3's float, so that question has an answer for a Cub,
    /// and the answer is wrong. It is "is there an amphibian OF THIS SIZE", which is what Ty asked for ("offers them
    /// an amphib choice if one exists like the j3 cub does not for example").
    /// </summary>
    public const double AmphibianBandBelow = 0.15, AmphibianBandAbove = 0.05;

    private static FloatSize S(string family, double n, bool straight, bool amphibian, FloatWeightBasis basis,
                               double publishedLb, string source)
    {
        double raw = InstalledLbPerDisplacementLb * n;
        return new FloatSize
        {
            Id = family + "-" + (int)n,
            FamilyId = family,
            Name = FamilyName(family) + " " + (int)n,
            DisplacementLb = n,
            Straight = straight,
            Amphibian = amphibian,
            // Both weights round off the SAME unrounded ratio, so the amphibian is 1.5 x the rule and not 1.5 x a
            // number that has already been rounded once.
            StraightInstalledLb = System.Math.Round(raw, 1),
            AmphibianInstalledLb = System.Math.Round(AmphibianWeightFactor * raw, 1),
            PublishedStraightLb = publishedLb,
            Basis = basis,
            Source = source,
        };
    }

    private static string FamilyName(string family) => family == AluminumId ? "Aluminum" : "Composite";

    public const string AluminumId = "aluminum";

    // ---- the aluminium ladder ------------------------------------------------------------------------------------
    // Nineteen ratings, each a real aluminium float that was built and flown, ordered by rating. The designations
    // behind them (the drawing/series prefixes 54-, 60-, 92-, 88/88A-, 89/89A-, 689-, 44-, 248B-, 289-, 439-, 597-,
    // 249/249A-, 628-, 582-, 696-, 58-, 679-, 55-, 78-) are the maker's and stay out of Id and Name; they are in
    // Source, where a measurement needs its provenance.
    //
    // THE AMPHIBIAN COLUMN IS THE RESEARCHED ONE, and it is the part of this file most likely to be wrong. The
    // evidence behind each "yes" is in its Source. The documents read were the FAA Aircraft Specifications and TCDSs
    // (which say "Floatplane with ..." or "Amphibian with ..." in so many words) and the maker's own parts lists,
    // whose Figure 1 exploded views are decisive: an amphibian lists a Bow Well Assembly, a Main Well Assembly, a Main
    // Gear Assy, a Retraction System and hydraulic lines, and a straight float lists a keel shoe, a skeg and beaching
    // gear fittings.
    private static readonly FloatSize[] AluminumSizes =
    {
        S(AluminumId, 1140, straight: true, amphibian: false, FloatWeightBasis.Measured, 147,
          "54-1140. PUBLISHED WEIGHT 147 lb (FAA Aircraft Specification A-691 Rev 34 Item 151; A-698 Rev 14 Item 151, 'Edo 54-1140 floats with water rudder'). NO AMPHIBIAN: neither specification contains the word 'amphibian' anywhere."),
        S(AluminumId, 1320, straight: true, amphibian: false, FloatWeightBasis.Measured, 171,
          "60-1320, the J-3 Cub's. PUBLISHED WEIGHT 171 lb (A-691 Rev 34 Item 206, 'Edo 60-1320 floats with water rudders (171 lb.)'; same in A-698 Rev 14 Item 207); OWNER-WEIGHED 171-182 lb installed with all rigging, which is where the ratio's +6 % over the certificate comes from. NO AMPHIBIAN (Ty 2026-11-11, naming it as the worked example; and neither specification has an amphibian entry of any kind)."),
        S(AluminumId, 1400, straight: true, amphibian: false, FloatWeightBasis.Measured, 174,
          "92-1400. PUBLISHED WEIGHT 174 lb (A-768 Rev 35 Item 402(b), 'per Edo Dwgs 06704 & 06822'; same in 5A2 Rev 22 Item 402(b)). NO AMPHIBIAN: straight in all four specifications that list it (A-691 Items 208/209, A-768, 5A2, 1A2 Rev 38 Item 209(a))."),
        S(AluminumId, 1650, straight: true, amphibian: false, FloatWeightBasis.Measured, 208,
          "88-1650 / 88A-1650: Cessna 120/140, 140A, and the 150 ('Seaplane - 1650 lb. (Edo 88A-1650 floats)', TCDS 3A19 Rev 50). PUBLISHED WEIGHT 208 lb (A-768 Rev 35 Item 402(a), per Cessna Dwg 0440111/0441100). The maker's parts list puts 88-1650, 88A-1650, 89-2000 and 689-2130 in ONE four-column table - one straight-float family, the 'A' a revision letter. NO AMPHIBIAN."),
        S(AluminumId, 2000, straight: true, amphibian: false, FloatWeightBasis.Measured, 251,
          "89/89A-2000, the classic 1,750-2,200 lb-class float: PA-18, PA-12/20/22, Champion/Citabria, Cessna 170/172. TWO PUBLISHED WEIGHTS ON DIFFERENT BASES - 263 lb with water rudders and all attachment struts, wires and fittings per the maker's own drawings (the ratio's heavier anchor), and 251 lb per Cessna Dwg 0541125 in A-799 Rev 55 Item 209(a) (245 lb for the -B drawing). NO AMPHIBIAN: TCDS 3A12 Rev 84 says 'Seaplane (Edo 89-2000 or 89A2000 floats)' in five places and has no amphibian configuration at all."),
        S(AluminumId, 2130, straight: true, amphibian: false, FloatWeightBasis.Extrapolated, 0,
          "689-2130, the Cessna 172 float. Weight DERIVED. Straight: it is a column of the same four-model parts list as the 1650 and 2000. No amphibian established."),
        S(AluminumId, 2425, straight: true, amphibian: false, FloatWeightBasis.Extrapolated, 0,
          "44-2425: Cessna 180 (TCDS 5A6, 'Floatplane with Edo 44-2425'). Weight DERIVED. Straight: the Figure 1 drawing lists keel shoe, skeg and an optional axle tube assembly - beaching gear, not retractable wheels. Fifteen pounds of displacement from the 2440; the two are the same float to a pilot, and the selector will treat them as a near tie."),
        S(AluminumId, 2440, straight: true, amphibian: false, FloatWeightBasis.Extrapolated, 0,
          "248B-2440: Maule M-4 210/220 ('EDO 248A2440 or 248B2440 Floats: 2300 lbs', Maule TCDS) and Cessna 172. Weight DERIVED. Straight: plain hull, skeg, keel, no wells in the parts list. No amphibian established."),
        S(AluminumId, 2700, straight: false, amphibian: true, FloatWeightBasis.Extrapolated, 0,
          "289-2700. AMPHIBIAN, AND ONLY AN AMPHIBIAN: TCDS 5A6 Rev 68 lists 'Amphibian with Edo 289-2700 ... at 2850 lbs.' on the Cessna 180/180A/180B, and the parts list's Figure 1 draws a main wheel and a bow wheel with a Main Well Assembly and a Bow Well Assembly in the table. Electric gear. This rating is why the catalogue models amphibians as their own models: it shares the 2870's hull, and the wells cost it 170 lb of displacement. Both weights DERIVED; no published weight for any amphibian in this range was found."),
        S(AluminumId, 2705, straight: false, amphibian: true, FloatWeightBasis.Extrapolated, 0,
          "439-2705. AMPHIBIAN: parts list Figure 1 draws the wheels and lists a Bow Well Assembly and a Main Well Assembly; approved on the Cessna 180/180A-D by STC (SA1-464, reissued 1998, 'Installation of Edo models 439-2705 amphibious floats'), not on the type certificate itself. Hydraulic gear, where the 2700 is electric. Weights DERIVED."),
        S(AluminumId, 2790, straight: false, amphibian: true, FloatWeightBasis.Extrapolated, 0,
          "597-2790. AMPHIBIAN, the best-attested one in the range: TCDS 5A6 Rev 68 and 3A24 Rev 40 both read 'Amphibian with Edo 597-2790' (3A24 Note 2 naming two gear systems, the engine-driven hydraulic 597-11 and the electro-hydraulic 597-12); there is a maintenance manual titled for it as an amphibian; the parts list shows wheels and wells; and a 2014 TSB of Canada report describes a Cessna 185E 'equipped with EDO model 2790 amphibious floats'. Weights DERIVED."),
        S(AluminumId, 2870, straight: true, amphibian: false, FloatWeightBasis.Extrapolated, 0,
          "249-2870 / 249A-2870: Cessna 180 class. Weight DERIVED. STRAIGHT: TCDS 5A6 and 3A24 always say 'Floatplane with Edo 249-2870 / 249A-2870', and both parts lists show no wells, only an optional axle tube assembly (beaching gear). Its amphibious siblings on the same hull are the 2700, 2705 and 2790 - which is the correction that reshaped this table; they are NOT alternative designations for the 2870."),
        S(AluminumId, 2960, straight: true, amphibian: false, FloatWeightBasis.Extrapolated, 0,
          "628-2960: Cessna 180. Weight DERIVED. STRAIGHT: the parts list has no wells and no gear, and 5A6 Rev 68 and 3A24 Rev 40 print 'Floatplane with Edo 628-2960' against 'Amphibian with Edo 597-2790' in the same table."),
        S(AluminumId, 3430, straight: true, amphibian: false, FloatWeightBasis.Extrapolated, 0,
          "582-3430: Cessna 185 and 206. Weight DERIVED. STRAIGHT: water rudder, drain kit, bumpers, no wells, no hydraulics in the parts list. Its amphibious counterpart is the 3500."),
        S(AluminumId, 3500, straight: false, amphibian: true, FloatWeightBasis.Extrapolated, 0,
          "696-3500. AMPHIBIAN, not a straight float - this row was wrong in the brief and the parts list settles it: Figure I 'Float Assembly 696-3500' (rel. May 1981) calls out Bow Wheel Well, Main Wheel Well, Bow Wheel Assy, Main Gear Assy, Retraction Sys. and hydraulic brake lines. NTSB ANC17LA053 (Cessna U206G, 2017) records 'EDO 696-3500 amphibious floats'. It is a bigger hull than the 3430, not a 3430 with gear bolted on. Weights DERIVED."),
        S(AluminumId, 4580, straight: true, amphibian: true, FloatWeightBasis.Extrapolated, 0,
          "58-4580: DHC-2 Beaver. BOTH VERSIONS EXIST. Straight is solid (NTSB CEN19MA141 airworthiness factual, a Beaver 'equipped with Edo 58-4580 floats'). Amphibian is attested but WITHOUT a primary document: a US Fish & Wildlife Beaver's maintenance history records 'the EDO Bristol 4580 amphibious floats were converted to EDO 4930', and the gear is described in the field as hand-pump only with the nose wheel folding on top of the float - and the 1956 Beaver flight manual calls the amphibious gear de Havilland's, on the maker's hull, which is why no float document carries it. Treat the amphibian as likely, not proven. At the Beaver's 5,090 lb float gross a pair is 9,160 lb against the 9,162 the rule wants: the installation sits ON the floor, which is why FloorTolerance exists. Weight DERIVED 0.132 x 4580 = 604.6 lb, and the comparable modern Beaver system weighs 491 lb, so this is the row most likely to be heavy."),
        S(AluminumId, 4930, straight: true, amphibian: false, FloatWeightBasis.Extrapolated, 0,
          "679-4930: Turbo Beaver; also what the game's own dhc2-beaver-floats-like flies on. Weight DERIVED, same caveat as the 4580. STRAIGHT as a factory model: the drawing shows keel shoe, skeg and axle tube assembly, no wells. Amphibious 4930s exist only as third-party conversions of 4580 amphibians onto 4930 hulls, and operators call them rare - so this catalogue does not offer one."),
        S(AluminumId, 7170, straight: true, amphibian: false, FloatWeightBasis.Extrapolated, 0,
          "55-7170A: DHC-3 Otter. NO AMPHIBIAN, and the trailing 'A' is NOT an amphibian marker - the drawing (Macdonald Bros. Aircraft, 30-4-53) titles it 55-7170A and item 24 is 'beaching gear fittings'; there are no wells, no gear and no hydraulics. Otters on amphibians today are on another maker's floats. A pair is 14,340 lb against 1.8 x 8,000 = 14,400: on the floor again. Weight DERIVED."),
        S(AluminumId, 29000, straight: false, amphibian: true, FloatWeightBasis.Extrapolated, 0,
          "78-29000: the wartime C-47 installation. AMPHIBIAN AND MILITARY ONLY: the single-step metal floats 'contained retractable wheels, located at the step', 150 sets ordered and about 30 completed, and the civil DC-3 specifications (A-618 Rev 22, A-669 Rev 31) contain NO float or amphibian entry whatsoever. No straight version of this float is attested, so none is offered - if the game ever wants a DC-3 on straight floats that is an invention, and should be made as one deliberately. Weights DERIVED, extrapolated nine times past the heaviest published figure, and worth no faith at all."),
    };

    /// <summary>
    /// ALUMINUM FLOATS — the family the game has. Modelled on the real aluminium range for the engineering and
    /// presented by size.
    /// </summary>
    public static readonly FloatCatalogFamily Aluminum = new()
    {
        Id = AluminumId,
        Name = "Aluminum floats",
        Source = "Ratings, straight/amphibian availability and the five published weights are a real aluminium float range's (Edo Aircraft Corporation), from the FAA Aircraft Specifications and TCDSs that approve them and from the maker's own parts lists, now held by Kenmore Air Harbor. Installed weights are derived from a ratio fitted to the two complete-installation figures at 1320 and 2000 lb. Player-facing naming is Ty's (2026-11-11).",
        Sizes = AluminumSizes,
    };

    // COMPOSITE FLOATS go here as a second FloatCatalogFamily when the lane for them is written (Ty 2026-11-11: "we'll
    // make a 'composite floats' later with other selections"). It needs its own size ladder and its own
    // InstalledLbPerDisplacementLb -- a composite float is the lighter choice and the whole point of offering it, so
    // sharing this family's ratio would make the option meaningless. Nothing below this line needs to change.
    private static readonly FloatCatalogFamily[] FamilyList = { Aluminum };

    public static IReadOnlyList<FloatCatalogFamily> Families => FamilyList;

    private static readonly Dictionary<string, FloatSize> ById =
        FamilyList.SelectMany(f => f.Sizes).ToDictionary(s => s.Id);

    /// <summary>Every size in every family, so a test or a debug screen can walk the lot.</summary>
    public static IEnumerable<FloatSize> AllSizes => FamilyList.SelectMany(f => f.Sizes);

    public static FloatCatalogFamily? GetFamily(string? id) =>
        id == null ? null : System.Array.Find(FamilyList, f => f.Id == id);

    public static FloatSize? Get(string? id) => id != null && ById.TryGetValue(id, out FloatSize? s) ? s : null;

    /// <summary>
    /// Does this size carry <paramref name="grossLb"/> legally — pair displacement ≥ 1.8 × gross, within
    /// <see cref="FloorTolerance"/>?
    /// </summary>
    public static bool MeetsReserve(FloatSize size, double grossLb) =>
        size.DisplacementLb >= MinDisplacementPerGrossLb * grossLb * (1 - FloorTolerance);

    /// <summary>Every size in the family that may legally carry <paramref name="grossLb"/> in the version asked for,
    /// smallest first.</summary>
    public static IReadOnlyList<FloatSize> Legal(double grossLb, bool amphibian = false, string familyId = AluminumId)
    {
        FloatCatalogFamily? f = GetFamily(familyId);
        if (f == null) return System.Array.Empty<FloatSize>();
        List<FloatSize> ok = new();
        foreach (FloatSize s in f.Sizes) if (s.Offers(amphibian) && MeetsReserve(s, grossLb)) ok.Add(s);
        return ok;
    }

    /// <summary>
    /// THE SELECTION RULE (Ty 2026-11-11: "The game selects the nearest to the gross weight"). Of the sizes that are
    /// built in the version asked for AND meet the reserve floor, the one whose rating is nearest the aeroplane's
    /// gross weight; a tie goes to the larger, because the tie-break a seaplane pilot wants is more buoyancy.
    ///
    /// <paramref name="grossLb"/> MUST BE THE FLOAT GROSS, not the landplane gross, and the difference changes the
    /// answer: the J-3 is 1,220 lb on wheels and 1,300 lb on floats, and at 1,300 this returns the 1320 every J-3 on
    /// floats has flown on, while at 1,220 it returns the 1140, which none ever did. The float gross is not always the
    /// HIGHER of the two either — the J-3 is up-grossed on floats, a Cessna 170 is RESTRICTED down to 2,107 lb from a
    /// 2,200 lb landplane gross — so it cannot be inferred, only looked up. An AMPHIBIAN has a third gross again,
    /// usually lower still, and the caller must pass that one when asking for an amphibian.
    ///
    /// Null when nothing in the family is big enough, or when no amphibian was built in the size range the aeroplane
    /// needs. The caller decides what to do about it rather than being handed a float that cannot legally lift the
    /// aeroplane, or one that was never built.
    /// </summary>
    public static FloatSize? Select(double grossLb, bool amphibian = false, string familyId = AluminumId)
    {
        FloatCatalogFamily? f = GetFamily(familyId);
        if (f == null || grossLb <= 0) return null;
        FloatSize? best = null;
        double bestErr = 0;
        foreach (FloatSize s in f.Sizes)
        {
            if (!s.Offers(amphibian) || !MeetsReserve(s, grossLb)) continue;
            double err = System.Math.Abs(s.DisplacementLb - grossLb);
            if (best == null || err < bestErr || (err == bestErr && s.DisplacementLb > best.DisplacementLb))
            {
                best = s; bestErr = err;
            }
        }
        return best;
    }

    /// <summary>As <see cref="Select"/>, from a gross mass in kilograms — what an AircraftConfig holds.</summary>
    public static FloatSize? SelectForMassKg(double grossKg, bool amphibian = false, string familyId = AluminumId) =>
        Select(grossKg / FloatSize.Lb, amphibian, familyId);

    /// <summary>
    /// THE AMPHIBIAN OFFER (Ty: "offers them an amphib choice if one exists like the j3 cub does not for example").
    /// Given the straight float the aeroplane was fitted with, the amphibian that belongs to the same size of
    /// aeroplane: nearest rating to the straight float's, inside the lopsided band on
    /// <see cref="AmphibianBandBelow"/>, and legal on this aeroplane's weight. Null means the configurator shows no
    /// amphibian switch at all — which is the J-3's answer, and the Cessna 172's, and the Otter's.
    /// </summary>
    public static FloatSize? AmphibianFor(FloatSize straight, double grossLb, string familyId = AluminumId)
    {
        FloatCatalogFamily? f = GetFamily(familyId);
        if (f == null) return null;
        double lo = straight.DisplacementLb * (1 - AmphibianBandBelow), hi = straight.DisplacementLb * (1 + AmphibianBandAbove);
        FloatSize? best = null;
        double bestErr = 0;
        foreach (FloatSize s in f.Sizes)
        {
            if (!s.Amphibian || !MeetsReserve(s, grossLb)) continue;
            if (s.DisplacementLb < lo || s.DisplacementLb > hi) continue;
            double err = System.Math.Abs(s.DisplacementLb - straight.DisplacementLb);
            if (best == null || err < bestErr || (err == bestErr && s.DisplacementLb > best.DisplacementLb))
            {
                best = s; bestErr = err;
            }
        }
        return best;
    }

    /// <summary>The pair a configurator shows for one aeroplane: the straight float it gets, and the amphibian it may
    /// be offered instead (null when there is none).</summary>
    public static (FloatSize? straight, FloatSize? amphibian) Offer(double grossLb, string familyId = AluminumId)
    {
        FloatSize? s = Select(grossLb, amphibian: false, familyId);
        return (s, s == null ? null : AmphibianFor(s, grossLb, familyId));
    }
}

/// <summary>
/// THE EXCHANGE RULE (Ty 2026-11-11, and the whole weight side rests on it): putting an aeroplane on floats REMOVES
/// the landing gear and ADDS the floats, so the change in empty weight is
///
///   Δ empty = float system installed − landing gear removed
///
/// not the float system's weight. The industry publishes exactly this quantity and calls it the "seaplane exchange
/// weight" — a modern maker's for a Cessna 172 on 2350-class floats is 168 lb, against a 305 lb float system, so
/// 137 lb of wheels, legs and fairings came off.
///
/// IT MATTERS BY A THIRD. Ty's own J-3 goes from 700 lb on wheels to 825 lb on floats: +125 lb, which is the 1320's
/// ~175 lb system minus ~50 lb of gear. Charge the full 175 and the aeroplane is 50 lb heavy before a drop of fuel
/// goes in — on a 1,300 lb gross that is a third of the useful load the floats cost.
///
/// IT IS VISIBLE IN THE CERTIFICATES TOO, which print both numbers for the same float: A-691 lists the J-3's 1320s at
/// 171 lb absolute and a weight CHANGE of +130 lb, so 41 lb of wheel gear came off that airframe by the certificate's
/// reckoning. (The 50 lb here is Ty's own figure for his own aeroplane, and it is the one that reproduces his two
/// weighings.)
///
/// NOT TO BE CONFUSED WITH THE GROSS WEIGHT, which also changes on floats and does not always change the same way:
/// the J-3's float gross is 1,300 lb against 1,220 on wheels, an ALLOWANCE granted with the float approval, while a
/// Cessna 170 is restricted DOWN to 2,107 lb. Neither is a consequence of anything weighed. The configs' mass.massKg
/// are flying weights and already hold the float gross on the float types; this class is about the EMPTY weight and
/// only that.
/// </summary>
public static class FloatExchange
{
    /// <summary>
    /// The weight of the landing gear a float conversion removes, lb, for the types whose gear weight is known.
    /// Null for a type nobody has weighed — the caller must not guess, because the guess lands straight in the empty
    /// weight.
    ///
    /// Ty 2026-11-11 gave the first four from his own knowledge of the airframes; the 172's is a modern maker's
    /// arithmetic (305 lb system − 168 lb published exchange weight = 137 lb of gear).
    /// </summary>
    public static double? GearRemovedLb(string? aircraftId) => aircraftId switch
    {
        "j3-cub-like" or "j3-cub-floats-like" => 50,    // Ty: and 700 + 175 - 50 = 825, his own aeroplane's two weights
        "pitts-s2b-like" => 100,                        // Ty
        "extra-300-like" => 100,                        // Ty
        "glider-2-33-like" => 25,                       // Ty: "tyre and brakes" - a 2-33 has one wheel and a skid
        "c172-like" => 137,                             // 305 lb 2350-class system - 168 lb published seaplane exchange weight
        _ => null,
    };

    /// <summary>
    /// The EXCHANGE weight, lb: what the aeroplane's empty weight changes BY. Positive on every real conversion —
    /// floats are heavier than the gear they replace — but the sign is not forced, because a type with heavy
    /// retractable gear and a small float is the case where it would not be.
    /// </summary>
    public static double ExchangeLb(double gearRemovedLb, FloatSize size, bool amphibian) =>
        size.InstalledLb(amphibian) - gearRemovedLb;

    /// <summary>The aeroplane's empty weight on floats, lb, from its empty weight on wheels.</summary>
    public static double FloatsEmptyLb(double wheelsEmptyLb, double gearRemovedLb, FloatSize size, bool amphibian) =>
        wheelsEmptyLb + ExchangeLb(gearRemovedLb, size, amphibian);

    /// <summary>The same in kilograms, which is what the configs and the sim hold.</summary>
    public static double ExchangeKg(double gearRemovedKg, FloatSize size, bool amphibian) =>
        size.InstalledKg(amphibian) - gearRemovedKg;

    public static double FloatsEmptyKg(double wheelsEmptyKg, double gearRemovedKg, FloatSize size, bool amphibian) =>
        wheelsEmptyKg + ExchangeKg(gearRemovedKg, size, amphibian);

    /// <summary>
    /// The whole chain for one aeroplane the catalog knows the gear weight of: pick the size for its float gross, and
    /// say what it then weighs empty. Null when the type's gear weight is unknown, or nothing is built that is both
    /// big enough and the version asked for — the cases a configurator must not paper over.
    ///
    /// The amphibian it reaches for is <see cref="FloatCatalog.AmphibianFor"/>'s — the one OF THIS AEROPLANE'S SIZE —
    /// and not merely the nearest amphibian that floats it. Without that, asking for an amphibian J-3 returns a
    /// Cessna 180's float, because a 2,700 lb-a-side amphibian certainly does float a Cub.
    /// </summary>
    public static (FloatSize size, double emptyKg)? Fit(string aircraftId, double wheelsEmptyKg, double floatGrossKg,
                                                        bool amphibian, string familyId = FloatCatalog.AluminumId)
    {
        double? gearLb = GearRemovedLb(aircraftId);
        double grossLb = floatGrossKg / FloatSize.Lb;
        FloatSize? straight = FloatCatalog.Select(grossLb, amphibian: false, familyId);
        FloatSize? size = !amphibian ? straight
                        : straight == null ? FloatCatalog.Select(grossLb, amphibian: true, familyId)
                        : FloatCatalog.AmphibianFor(straight, grossLb, familyId);
        if (gearLb == null || size == null) return null;
        return (size, FloatsEmptyKg(wheelsEmptyKg, gearLb.Value * FloatSize.Lb, size, amphibian));
    }
}
