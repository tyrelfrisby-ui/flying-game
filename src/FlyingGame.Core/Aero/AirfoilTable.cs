using FlyingGame.Core.DataContracts;

namespace FlyingGame.Core.Aero;

public readonly struct AeroCoefficients
{
    public readonly double Cl;
    public readonly double Cd;
    public readonly double Cm;

    public AeroCoefficients(double cl, double cd, double cm)
    {
        Cl = cl;
        Cd = cd;
        Cm = cm;
    }
}

/// <summary>
/// Full ±180° airfoil polar with linear interpolation, so post-stall (autorotation, flat-plate drag) is
/// real data rather than a special case. <see cref="AlphaRad"/> must be sorted ascending and span the
/// full [-pi, pi] range (see docs/DATA-CONTRACTS.md).
/// </summary>
public sealed class AirfoilTable
{
    private readonly double[] _alphaRad;
    private readonly double[] _cl;
    private readonly double[] _cd;
    private readonly double[] _cm;

    public AirfoilTable(AirfoilTableData data)
    {
        if (data.AlphaRad.Length < 2 || data.AlphaRad.Length != data.Cl.Length
            || data.AlphaRad.Length != data.Cd.Length || data.AlphaRad.Length != data.Cm.Length)
        {
            throw new ArgumentException("Airfoil table arrays must be non-empty and equal length.");
        }

        _alphaRad = data.AlphaRad;
        _cl = data.Cl;
        _cd = data.Cd;
        _cm = data.Cm;
    }

    /// <summary>Samples Cl/Cd/Cm at the given angle of attack (radians), wrapping into [-pi, pi] first.</summary>
    public AeroCoefficients Sample(double alphaRad)
    {
        double wrapped = WrapToPi(alphaRad);

        if (wrapped <= _alphaRad[0])
        {
            return new AeroCoefficients(_cl[0], _cd[0], _cm[0]);
        }

        int last = _alphaRad.Length - 1;
        if (wrapped >= _alphaRad[last])
        {
            return new AeroCoefficients(_cl[last], _cd[last], _cm[last]);
        }

        int hi = 1;
        while (hi < _alphaRad.Length && _alphaRad[hi] < wrapped)
        {
            hi++;
        }

        int lo = hi - 1;
        double span = _alphaRad[hi] - _alphaRad[lo];
        double t = span > 1e-12 ? (wrapped - _alphaRad[lo]) / span : 0.0;

        double cl = _cl[lo] + t * (_cl[hi] - _cl[lo]);
        double cd = _cd[lo] + t * (_cd[hi] - _cd[lo]);
        double cm = _cm[lo] + t * (_cm[hi] - _cm[lo]);
        return new AeroCoefficients(cl, cd, cm);
    }

    private static double WrapToPi(double angleRad)
    {
        const double twoPi = 2.0 * Math.PI;
        double wrapped = angleRad - twoPi * Math.Floor((angleRad + Math.PI) / twoPi);
        return wrapped;
    }
}
