using System.Runtime.CompilerServices;
namespace FlyingGame.Core;

/// <summary>Forward derivative of the evaluated branch, in height metres per east/north km.
/// Value arithmetic preserves the scalar evaluator's operation order. At a nondifferentiable
/// tie, Min/Max select the first branch; Abs(0), a clamped endpoint and zero-length distance
/// use zero derivative. A true height discontinuity has no unique normal: this reports the
/// selected surface's one-sided derivative, never a secant across the jump.</summary>
internal readonly struct TerrainJet
{
    public readonly double Value, E, N;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TerrainJet(double value,double e=0,double n=0) { Value=value; E=e; N=n; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator TerrainJet(double v) => new(v);
    public static TerrainJet PositiveInfinity => new(double.PositiveInfinity);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TerrainJet operator +(TerrainJet a,TerrainJet b) => new(a.Value+b.Value,a.E+b.E,a.N+b.N);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TerrainJet operator -(TerrainJet a,TerrainJet b) => new(a.Value-b.Value,a.E-b.E,a.N-b.N);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TerrainJet operator -(TerrainJet a) => new(-a.Value,-a.E,-a.N);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TerrainJet operator *(TerrainJet a,TerrainJet b) => new(a.Value*b.Value,a.E*b.Value+a.Value*b.E,a.N*b.Value+a.Value*b.N);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TerrainJet operator /(TerrainJet a,TerrainJet b)
    {
        double v=a.Value/b.Value;
        return new(v,(a.E-v*b.E)/b.Value,(a.N-v*b.N)/b.Value);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TerrainJetPoint operator *(IslandPoint p,TerrainJet a) => new(p.E*a,p.N*a);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <(TerrainJet a,TerrainJet b) => a.Value<b.Value;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >(TerrainJet a,TerrainJet b) => a.Value>b.Value;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <=(TerrainJet a,TerrainJet b) => a.Value<=b.Value;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >=(TerrainJet a,TerrainJet b) => a.Value>=b.Value;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(TerrainJet a,TerrainJet b) => a.Value==b.Value;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(TerrainJet a,TerrainJet b) => a.Value!=b.Value;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? o) => o is TerrainJet j&&this==j;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => Value.GetHashCode();
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TerrainJet Min(TerrainJet a,TerrainJet b) => a.Value<=b.Value?a:b;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TerrainJet Max(TerrainJet a,TerrainJet b) => a.Value>=b.Value?a:b;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TerrainJet Abs(TerrainJet a) => a.Value>0?a:a.Value<0?-a:new TerrainJet(0);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TerrainJet Clamp(TerrainJet a,TerrainJet lo,TerrainJet hi) => a.Value<=lo.Value?lo:a.Value>=hi.Value?hi:a;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TerrainJet Sqrt(TerrainJet a)
    {
        double v=Math.Sqrt(a.Value);
        return v==0?new TerrainJet(v):new TerrainJet(v,a.E/(2*v),a.N/(2*v));
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TerrainJet Pow(TerrainJet a,double power)
    {
        double v=Math.Pow(a.Value,power),d=power*Math.Pow(a.Value,power-1);
        return new TerrainJet(v,a.E*d,a.N*d);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TerrainJet Smooth(TerrainJet t) { t=Clamp(t,0,1); return t*t*(3-2*t); }
}

internal readonly struct TerrainJetPoint
{
    public readonly TerrainJet E,N;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TerrainJetPoint(TerrainJet e,TerrainJet n) { E=e; N=n; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TerrainJetPoint Variable(IslandPoint p) => new(new TerrainJet(p.E,1,0),new TerrainJet(p.N,0,1));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator TerrainJetPoint(IslandPoint p) => new(p.E,p.N);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator IslandPoint(TerrainJetPoint p) => new(p.E.Value,p.N.Value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TerrainJetPoint operator +(TerrainJetPoint a,TerrainJetPoint b) => new(a.E+b.E,a.N+b.N);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TerrainJetPoint operator -(TerrainJetPoint a,IslandPoint b) => new(a.E-b.E,a.N-b.N);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TerrainJetPoint operator -(TerrainJetPoint a,TerrainJetPoint b) => new(a.E-b.E,a.N-b.N);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TerrainJetPoint operator *(TerrainJetPoint a,TerrainJet t) => new(a.E*t,a.N*t);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TerrainJet Dot(IslandPoint b) => E*b.E+N*b.N;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TerrainJet Dot(TerrainJetPoint b) => E*b.E+N*b.N;
    public TerrainJet Length => TerrainJet.Sqrt(E*E+N*N);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (TerrainJet d,TerrainJet t) Segment(TerrainJetPoint p,IslandPoint a,IslandPoint b)
    {
        var v=b-a; TerrainJet t=v.Dot(v)>0?TerrainJet.Clamp((p-a).Dot(v)/v.Dot(v),0,1):0;
        return ((p-((TerrainJetPoint)a+v*t)).Length,t);
    }
}
