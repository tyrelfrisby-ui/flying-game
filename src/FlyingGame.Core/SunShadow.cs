namespace FlyingGame.Core;

/// <summary>
/// How big, how soft and how dark an aeroplane's shadow on the ground is, from the height it is cast from.
///
/// THE BUG THIS EXISTS FOR (open visual report, 2026-11-09; Ty: "fix it now — it's cheap"): the shadow was the same
/// size at 50 ft and at 5,000 ft. That was not a fudge gone wrong, it is what the drawing method gives: GroundShadow
/// flattens the airframe's own meshes onto the ground plane along the sun direction, and a PARALLEL projection of a
/// solid onto a plane has exactly the solid's own cross-section whatever the distance. A perfectly point-like sun
/// would really behave that way. The real one does not.
///
/// THE GEOMETRY. The sun is a disc of angular diameter θ, so every edge of the aeroplane casts a transition band on
/// the ground rather than a line. At a slant range d from the occluder to the ground along the ray, that band is
/// d·θ wide, straddling the sharp-sun edge: the full-dark UMBRA ends d·θ/2 inside it and the outer edge of the
/// PENUMBRA is d·θ/2 outside it. So for an aeroplane of half-size w:
///
///   penumbra half-width   p = d·θ/2
///   the patch's half-size  w + p                      (the shadow GROWS)
///   the umbra's half-size  w − p, gone once p ≥ w     (the shadow SOFTENS, and then has no dark core at all)
///
/// The sun's mean angular diameter is 31′59″ = 0.5333° = <see cref="SunAngularDiameterRad"/> (it runs 31.46′ at
/// aphelion to 32.53′ at perihelion; one mean value is used — the 3 % swing is far below what anyone can see in a
/// shadow's edge). For a Pitts S-2B, upper span 6.0 m from its own config, so w = 3.0 m, the umbra is gone at a slant
/// range of 2w/θ = 645 m — about 1,500 ft above the ground with the sun at 45°. Above that there is no sharp shadow
/// anywhere on the ground, only a smear.
///
/// HOW DARK. A single flat silhouette cannot draw the gradient, so the one number it is drawn with has to stand for
/// the whole patch. It is the LIGHT BLOCKED that is conserved: the aeroplane removes the same amount of sunlight
/// whatever height it is at, and softening spreads it over a patch (1 + p/w)² larger in area. So the patch is drawn
/// at 1/(1 + p/w)² of the sharp shadow's darkness, which is the correct average over it.
/// REJECTED: drawing it at the darkness of the patch's CENTRE instead, min(1, (w/p)²) — the fraction of the sun's
/// disc the aeroplane covers as seen from directly under it. That is the right number for one pixel and the wrong
/// one for a uniform patch: it holds a shadow that has already DOUBLED in size at full darkness right up to the
/// height where the umbra vanishes, which reads as a shadow that suddenly swells rather than one that fades.
/// Both agree at zero height (sharp and full) and agree again far away (p ≫ w, where both go as (w/p)²); they
/// differ only across the transition, and the flux rule is the one that is continuous through it.
/// REJECTED: a second draw for the umbra inside the penumbra, to get a two-step gradient. It doubles the draws per
/// part on a phone, and the silhouette is drawn through a stencil (PlanarShadow.shader, Ref 7 NotEqual/Replace) that
/// exists precisely so overlapping parts darken a pixel once — a second pass at the same ref cannot pass it.
///
/// NOT MODELLED, deliberately: (1) the penumbra band is wider ALONG the sun's azimuth than across it, by 1/sin of the
/// sun's elevation, because the band lands on the ground at a slant — the flattening already stretches the silhouette
/// that way, and the extra anisotropy is a second-order shape change on something that is by then a fuzzy patch;
/// (2) haze. Ty asked for the shadow to "fade into the haze" and the fade here is the penumbra's, not a fog's: this
/// project draws no scene fog at all (nothing sets RenderSettings.fog outside the 2-D practice view), so there is no
/// transmittance to attenuate through. If a visibility model is ever added, its transmittance over the camera-to-
/// shadow path multiplies <see cref="Soften"/>'s opacity; it does not replace it.
/// </summary>
public static class SunShadow
{
    /// <summary>The sun's mean angular diameter, rad: 31′59″ = 0.5333°.</summary>
    public const double SunAngularDiameterRad = 9.308e-3;

    /// <summary>Half the width of the transition band an edge casts at slant range <paramref name="slantRangeM"/>, m.
    /// This is the one length in the whole model: everything below is it against the aeroplane's own size.</summary>
    public static double PenumbraHalfWidthM(double slantRangeM) =>
        slantRangeM > 0 ? 0.5 * slantRangeM * SunAngularDiameterRad : 0.0;

    /// <summary>Slant range at which an occluder of half-size <paramref name="halfSizeM"/> stops casting any fully
    /// dark shadow at all (p = w), m. 645 m for a Pitts' 6.0 m span.</summary>
    public static double UmbraVanishesAtM(double halfSizeM) =>
        halfSizeM > 0 ? 2.0 * halfSizeM / SunAngularDiameterRad : 0.0;

    /// <summary>
    /// The shadow of an occluder <paramref name="halfSizeM"/> across (half-size, m) cast <paramref name="slantRangeM"/>
    /// away along the sun's ray: how much bigger than the sharp silhouette to draw it (<c>Grow</c>, ≥ 1, a scale about
    /// the shadow's own centre) and what fraction of the sharp shadow's darkness to draw it at (<c>Opacity01</c>, 0..1).
    /// A zero or negative size or range gives the sharp shadow unchanged.
    /// </summary>
    public static (double Grow, double Opacity01) Soften(double slantRangeM, double halfSizeM)
    {
        if (!(halfSizeM > 0) || !(slantRangeM > 0)) return (1.0, 1.0);
        double grow = 1.0 + PenumbraHalfWidthM(slantRangeM) / halfSizeM;
        return (grow, 1.0 / (grow * grow));
    }
}
