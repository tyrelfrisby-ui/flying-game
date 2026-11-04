using FlyingGame.Core;
using FlyingGame.Core.DataContracts;
using Xunit;
using Xunit.Abstractions;

namespace FlyingGame.FlightTests;

/// <summary>SETBACK_PROBE=1: the full-aileron steady roll rate with the hinge at the aileron's nose and set back 30 %.</summary>
public class AileronSetBackProbe
{
    private readonly ITestOutputHelper _o;
    public AileronSetBackProbe(ITestOutputHelper o) { _o = o; }

    [Theory]
    [InlineData("extra-300-like", 160.0)]
    [InlineData("pitts-s2b-like", 160.0)]
    [InlineData("c172-like", 110.0)]
    public void Probe(string id, double mph)
    {
        if (Environment.GetEnvironmentVariable("SETBACK_PROBE") != "1") return;
        foreach (double b in new[] { 0.0, 0.30 })
        {
            (double steady, double to90, _) = PittsRoll.Measure(id, mph, cfg => cfg.Controls.Aileron.HingeSetBackFrac = b);
            _o.WriteLine($"{id} {mph} mph set-back {b:F2}: {steady:F1} deg/s, {to90:F2} s to 90 %");
        }
    }
}
