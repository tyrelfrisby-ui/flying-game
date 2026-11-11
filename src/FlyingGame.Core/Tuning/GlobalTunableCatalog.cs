using FlyingGame.Core.Aero;

namespace FlyingGame.Core.Tuning;

/// <summary>
/// THE GLOBAL KNOBS THAT EXIST TODAY — one value for every aeroplane in the game.
///
/// A global belongs here when the number is a statement about the PHYSICS rather than about an airframe: the
/// flutter law's rate exponent is the same claim whether you are flying the Cub or the Sabre, and giving each type
/// its own copy would invite thirty-five different laws with no way to tell which one a report was flown on.
///
/// A number that differs legitimately between aeroplanes is NOT a global, however tempting the single slider is.
/// It goes in <see cref="TunableCatalog"/> against its own config field.
///
/// Adding the next one is one <see cref="GlobalTunableRegistry.Register"/> call in here and nothing else.
/// </summary>
public static class GlobalTunableCatalog
{
    internal static void Load() => Flutter_();

    private static void Flutter_()
    {
        const string g = GlobalTunableRegistry.FlutterGroup;

        GlobalTunableRegistry.Register(new GlobalTunable
        {
            Id = "flutter.rateExponent",
            Group = g,
            Label = "How fast flutter eats the structure with speed",
            Min = 0.5, Max = 4.0, Step = 0.25, FineStep = 0.05, Decimals = 2,
            Description = "How much worse flutter gets for each knot past where it starts. Ty's own calibration fixes "
                        + "it at ONE speed — a minute at 1.35 times the dive speed is a whole fatigue life — and says "
                        + "nothing about the curve through that point, so this is the curve. At 1 the damage piles up "
                        + "in straight proportion to how far past the onset you are, which is the shipped value and "
                        + "the one he chose: it gives three minutes at 1.25 Vd, so a long run there really does kill "
                        + "the aeroplane. Raise it and the low end of the band becomes survivable in practice — at 2 "
                        + "you would have nine minutes at 1.25 Vd, which no pilot could hold anyway. Lower it and "
                        + "even a brief excursion past the onset starts to count.",
            Get = () => Flutter.RateExponent,
            Set = v => Flutter.RateExponent = v,
        });

        GlobalTunableRegistry.Register(new GlobalTunable
        {
            Id = "flutter.armVd",
            Group = g,
            Label = "Speed flutter arms at",
            Units = " Vd",
            Min = 1.0, Max = 1.5, Step = 0.025, FineStep = 0.005, Decimals = 3,
            Description = "Where the flutter mode goes unstable, as a multiple of the dive speed (Vd is 1.1 times "
                        + "Vne). Past this the aeroplane is ARMED: nothing is shaking yet, but any control that moves "
                        + "faster than a degree a second will set it off, and that control is the one that flutters. "
                        + "Below it the mode is damped again, so the shaking stops — though the damage already done "
                        + "stays done. Ty has deliberately not moved this one.",
            Get = () => Flutter.ArmVd,
            Set = v => Flutter.ArmVd = v,
        });

        GlobalTunableRegistry.Register(new GlobalTunable
        {
            Id = "flutter.fallbackInitiateVd",
            Group = g,
            Label = "Speed it starts by itself",
            Units = " Vd",
            Min = 1.0, Max = 1.6, Step = 0.025, FineStep = 0.005, Decimals = 3,
            Description = "Where flutter starts even with the stick and pedals held perfectly still, as a multiple of "
                        + "the dive speed. Between the arming speed and this one it waits for a control to move; past "
                        + "this the ambient air is excitation enough and it begins on a surface at random. Ty moved it "
                        + "from 1.3 to 1.35 and then back to 1.275, so the armed-and-waiting band is 1.20 to 1.275 as "
                        + "it ships.",
            Get = () => Flutter.FallbackInitiateVd,
            Set = v => Flutter.FallbackInitiateVd = v,
        });

        GlobalTunableRegistry.Register(new GlobalTunable
        {
            Id = "flutter.ceilingVd",
            Group = g,
            Label = "Speed the structure fails regardless",
            Units = " Vd",
            Min = 1.0, Max = 1.8, Step = 0.025, FineStep = 0.005, Decimals = 3,
            Description = "The hard ceiling: at this multiple of the dive speed the wing or tail structure fails "
                        + "however briefly you were there, with no fatigue to accumulate first. It is its own "
                        + "mechanism and not the fatigue law running away — asking one law to do both would have made "
                        + "the exponent above carry a job it cannot. Ty brought it down from 1.5 to 1.3, which puts "
                        + "the whole flutter event inside a ten per cent band of speed.",
            Get = () => Flutter.CeilingVd,
            Set = v => Flutter.CeilingVd = v,
        });
    }
}
