namespace FlyingGame.Core;

/// <summary>Read-only compiled authoring values. No parser, file IO or Unity-only dependency.</summary>
public sealed class IslandValue
{
    private readonly object? value;
    private IslandValue(object? value) { this.value = value; }
    public static readonly IslandValue Empty = new(null);
    public static IslandValue Scalar(object value) => new(value);
    public static IslandValue Array(params IslandValue[] items) => new(items);
    public static IslandValue Map(params (string key, IslandValue value)[] items)
    {
        var map = new Dictionary<string, IslandValue>();
        foreach (var item in items) map.Add(item.key, item.value);
        return new IslandValue(map);
    }
    public IslandValue this[string key] => value is Dictionary<string, IslandValue> d && d.TryGetValue(key, out var v) ? v : Empty;
    public IslandValue this[int i] => value is IslandValue[] a && i >= 0 && i < a.Length ? a[i] : Empty;
    public IslandValue[] Items => value as IslandValue[] ?? System.Array.Empty<IslandValue>();
    public bool Has(string key) => this[key] != Empty;
    public double Number(double fallback = 0) => value is double d ? d : fallback;
    public string Text(string fallback = "") => value as string ?? fallback;
    public bool Flag(bool fallback = false) => value is bool b ? b : fallback;
    public double N(string key, double fallback = 0) => this[key].Number(fallback);
    public string S(string key, string fallback = "") => this[key].Text(fallback);
    public IslandPoint Point => new(this[0].Number(), this[1].Number());
    public IslandPoint[] Points => Items.Select(v => v.Point).ToArray();
}

/// <summary>Authoring east/north km. Convert once at sim/render boundary.</summary>
public readonly struct IslandPoint
{
    public readonly double E, N;
    public IslandPoint(double e, double n) { E = e; N = n; }
    public double X => N * 1000;
    public double Y => E * 1000;
    public static IslandPoint operator +(IslandPoint a, IslandPoint b) => new(a.E + b.E, a.N + b.N);
    public static IslandPoint operator -(IslandPoint a, IslandPoint b) => new(a.E - b.E, a.N - b.N);
    public static IslandPoint operator *(IslandPoint a, double t) => new(a.E * t, a.N * t);
    public double Dot(IslandPoint b) => E * b.E + N * b.N;
    public double Length => Math.Sqrt(E * E + N * N);
    public static IslandPoint Heading(double h) => new(Math.Sin(h * Math.PI / 180), Math.Cos(h * Math.PI / 180));
}
