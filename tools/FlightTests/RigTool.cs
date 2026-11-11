using System.Globalization;
using System.Text.RegularExpressions;
using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using FlyingGame.Sim;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>
/// The tool that RIGS the fleet (owner 2026-11-02, "trim/rig all of the airplanes for hands off level 75% power").
///
/// RIG_PROBE=1 prints, for every config, what <see cref="RigSolver.SolveRig"/> finds: the aileron rig, the fin offset,
/// the elevator trim, the speed, α and bank of the rig condition, and the residual. RIG_WRITE=1 also writes the
/// `rig` block into configs/aircraft/*.json — it is the only thing that should ever write it. The solve starts each
/// type from an UNRIGGED airframe, so re-running it is idempotent and nothing it wrote last time leaks into the answer.
///
/// Without either variable it does nothing; the rig is checked by RigTests, against hand-copied numbers.
/// </summary>
public class RigTool
{
    private readonly ITestOutputHelper _out;
    public RigTool(ITestOutputHelper o) { _out = o; }

    private static string Repo
    {
        get { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !Directory.Exists(Path.Combine(d.FullName, "configs", "aircraft"))) d = d.Parent; return d!.FullName; }
    }

    [Fact]
    public void SolveAndWriteTheFleetRig()
    {
        bool write = Environment.GetEnvironmentVariable("RIG_WRITE") == "1";
        if (!write && Environment.GetEnvironmentVariable("RIG_PROBE") == null) return;
        string dir = Path.Combine(Repo, "configs", "aircraft");
        // RIG_ONLY=<id>[,<id>...] scopes the WRITE to those types. Without it a write rewrites every config in the
        // directory, which on 2026-10-19 is how a calibration run quietly re-rigged two unrelated J-3s; and today only
        // one type's physics moved, so only that type should move. The probe listing is unscoped either way.
        var only = (Environment.GetEnvironmentVariable("RIG_ONLY") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
        const double D = 180.0 / Math.PI;
        _out.WriteLine("id | thr | alt ft | KTAS | KIAS | alpha | theta | elev | ail rig | fin (LE left +) | bank | gamma | lat | resid | iters");
        foreach (string path in Directory.GetFiles(dir, "*.json").OrderBy(p => p))
        {
            AircraftConfig c = AircraftConfigLoader.LoadFromFile(path);
            c.Rig = new RigConfig();   // solve from the bare airframe
            RigSolver.Result r = RigSolver.SolveRig(c);
            double rho = Atmosphere.DensityAtAltitude(r.AltitudeM);
            double kias = r.TasMs * Math.Sqrt(rho / 1.225) / 0.514444;
            _out.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0} | {1:0.00} | {2:0} | {3:0.0} | {4:0.0} | {5:0.00} | {6:0.00} | {7:0.00} | {8:+0.000;-0.000} | {9:+0.000;-0.000} | {10:+0.000;-0.000} | {11:+0.00;-0.00} | {12} | {13:E1} | {14}{15}",
                c.Id, r.Throttle, r.AltitudeM / 0.3048, r.TasMs / 0.514444, kias, r.AlphaRad * D, r.ThetaRad * D, r.ElevatorRad * D,
                r.AileronRigRad * D, r.FinOffsetRad * D, r.BankRad * D, r.GammaRad * D, r.LateralSolved ? "solved" : "none", r.ResidualNorm, r.Iterations,
                r.Converged ? "" : "  ** NOT CONVERGED **"));
            if (!write || (only.Length > 0 && !only.Contains(c.Id))) continue;
            Assert.True(r.Converged, $"{c.Id}: rig did not converge (residual {r.ResidualNorm:E2})");
            File.WriteAllText(path, WithRig(File.ReadAllText(path), r));
            _out.WriteLine($"   WROTE {c.Id}");
        }
    }

    /// <summary>The config text with its top-level `rig` block replaced (or inserted before `controls`), leaving every
    /// other byte of the file alone.
    ///
    /// THE FILE'S OWN INDENT, not two spaces. Every config but one is written with two, and both regexes here used to
    /// be anchored on that literally — so when the Grob 103 arrived from a generator that writes ONE space, the only
    /// thing that writes a rig could not find its `controls` block and the whole fleet write died on an assert at the
    /// seventeenth type, with sixteen files already rewritten. The Grob then had no rig at all (and no
    /// <see cref="RigTable"/> row, so neither the rig pin nor the spiral test could see that it had none), which is how
    /// a type can sit in the fleet unrigged while every rig test is green about it. Whitespace must not be able to do
    /// that.</summary>
    public static string WithRig(string json, RigSolver.Result r)
    {
        static string N(double v) => (Math.Abs(v) < 5e-7 ? 0.0 : v).ToString("0.######", CultureInfo.InvariantCulture);
        string nl = json.Contains("\r\n") ? "\r\n" : "\n";
        var existingAny = new Regex("^([ \t]+)\"rig\": \\{", RegexOptions.Multiline);
        var controlsAny = new Regex("^([ \t]+)\"controls\": \\{", RegexOptions.Multiline);
        Match mRig = existingAny.Match(json), mControls = controlsAny.Match(json);
        Assert.True(mRig.Success || mControls.Success, "config has no top-level \"rig\" or \"controls\" block to put the rig at or before");
        string ind = (mRig.Success ? mRig : mControls).Groups[1].Value, ind2 = ind + ind;
        string block = string.Join(nl, new[]
        {
            ind + "\"rig\": {",
            $"{ind2}\"aileronRad\": {N(r.AileronRigRad)},",
            $"{ind2}\"finOffsetRad\": {N(r.FinOffsetRad)},",
            $"{ind2}\"throttleFraction\": {N(r.Throttle)},",
            $"{ind2}\"altitudeM\": {N(r.AltitudeM)},",
            $"{ind2}\"tasMs\": {r.TasMs.ToString("0.###", CultureInfo.InvariantCulture)},",
            $"{ind2}\"alphaRad\": {N(r.AlphaRad)},",
            $"{ind2}\"elevatorTrimRad\": {N(r.ElevatorRad)},",
            $"{ind2}\"bankRad\": {N(r.BankRad)}",
            ind + "},",
        }) + nl;
        if (mRig.Success)
        {
            string e = Regex.Escape(ind);
            var existing = new Regex("^" + e + "\"rig\": \\{.*?^" + e + "\\},?\\r?\\n", RegexOptions.Singleline | RegexOptions.Multiline);
            Assert.True(existing.IsMatch(json), "the config's \"rig\" block does not close at its own indent");
            return existing.Replace(json, block, 1);
        }
        return json.Insert(mControls.Index, block);
    }
}
