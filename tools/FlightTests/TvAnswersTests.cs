using FlyingGame.Core;
using Xunit;

namespace FlyingGame.FlightTests;

/// <summary>
/// Ty's answers to the TV plan (2026-11-04 17:14, docs/TV-COMPANION-PLAN.md §9): Q2 iPad too, Q7 radio never on the TV,
/// Q8 instruments on the Mac's second monitor, Q9 QR + PIN pairing, Q10 TLS-PSK. Expected values are literals: the RFC
/// 5869 vectors are copied from the RFC, the PSKs were computed with Python's hmac module (an implementation independent
/// of Core.Hkdf), and the panel geometry is worked by hand in the comments.
/// </summary>
public sealed class TvAnswersTests
{
    // ---- Q2 / Q8: which device does what with a second screen ----

    [Theory]
    //          device           displays tv setting          monitor                     widget → mode
    [InlineData(TvDevice.IPhone, 2, TvSetting.GameOnTv, MonitorSetting.Off, false, TvMode.GameOnTv)]
    [InlineData(TvDevice.IPad, 2, TvSetting.GameOnTv, MonitorSetting.Off, false, TvMode.GameOnTv)]          // Q2: the iPad too
    [InlineData(TvDevice.IPad, 2, TvSetting.Mirror, MonitorSetting.Off, false, TvMode.Mirror)]
    [InlineData(TvDevice.IPad, 1, TvSetting.GameOnTv, MonitorSetting.Off, false, TvMode.Handheld)]
    [InlineData(TvDevice.IPad, 2, TvSetting.GameOnTv, MonitorSetting.Off, true, TvMode.Handheld)]           // never the widget app
    [InlineData(TvDevice.Mac, 2, TvSetting.GameOnTv, MonitorSetting.Instruments, false, TvMode.InstrumentsOnMonitor)]   // Q8
    [InlineData(TvDevice.Mac, 2, TvSetting.GameOnTv, MonitorSetting.Off, false, TvMode.Handheld)]          // a Mac keeps its monitor until asked
    [InlineData(TvDevice.Mac, 1, TvSetting.GameOnTv, MonitorSetting.Instruments, false, TvMode.Handheld)]
    [InlineData(TvDevice.Mac, 2, TvSetting.Mirror, MonitorSetting.Instruments, true, TvMode.Handheld)]      // the Aero Widget is a Mac app
    [InlineData(TvDevice.Quest, 2, TvSetting.GameOnTv, MonitorSetting.Instruments, false, TvMode.Handheld)]
    [InlineData(TvDevice.Other, 2, TvSetting.GameOnTv, MonitorSetting.Instruments, false, TvMode.Handheld)]
    [InlineData(TvDevice.IPhone, 2, TvSetting.GameOnTv, MonitorSetting.Instruments, false, TvMode.GameOnTv)] // the Mac setting is the Mac's
    public void DeviceModeTable(TvDevice device, int displays, TvSetting tv, MonitorSetting monitor, bool widget, TvMode expected)
        => Assert.Equal(expected, TvModeLogic.Decide(device, displays, tv, monitor, widget));

    [Fact]
    public void OptionsOffersEachDeviceItsOwnChoice()
    {
        Assert.True(TvModeLogic.HasTvSetting(TvDevice.IPhone));
        Assert.True(TvModeLogic.HasTvSetting(TvDevice.IPad));
        Assert.False(TvModeLogic.HasTvSetting(TvDevice.Mac));
        Assert.False(TvModeLogic.HasTvSetting(TvDevice.Quest));
        Assert.True(TvModeLogic.HasMonitorSetting(TvDevice.Mac));
        Assert.False(TvModeLogic.HasMonitorSetting(TvDevice.IPad));
        Assert.False(TvModeLogic.HasMonitorSetting(TvDevice.Quest));
    }

    [Fact]
    public void TheMonitorSettingDefaultsToOff()
    {
        // Saved as an int: a missing pref (GetInt default 0) must leave a Mac's second monitor alone.
        Assert.Equal(0, (int)MonitorSetting.Off);
        Assert.Equal(1, (int)MonitorSetting.Instruments);
    }

    [Theory]
    //          iPad screens in px at their own scale: 4:3-ish shapes the TV pad grid has to fit, 44 pt = 88 px at 2x
    [InlineData(2266, 1488, false)]   // iPad mini (6th)
    [InlineData(1488, 2266, true)]
    [InlineData(2752, 2064, false)]   // iPad Pro 13"
    [InlineData(2064, 2752, true)]
    public void ThePadGridFitsAnIPad(int sw, int sh, bool portrait)
    {
        double m = 0.04 * Math.Min(sw, sh), top = (portrait ? 0.12 : 0.22) * sh;
        double x = m, y = top, w = sw - 2 * m, h = sh - top - m, gap = 0.012 * Math.Min(sw, sh);
        var groups = TvPadLayout.Groups(true, true, true, true);
        var cells = TvPadLayout.Layout(groups, portrait, x, y, w, h, gap);
        Assert.Equal(18, cells.Count);
        foreach (var c in cells)
        {
            Assert.True(c.X >= x - 1e-6 && c.Y >= y - 1e-6 && c.X + c.W <= x + w + 1e-6 && c.Y + c.H <= y + h + 1e-6, $"{c.Button} outside");
            Assert.True(c.W >= 88 && c.H >= 88, $"{c.Button} is {c.W:F0} × {c.H:F0} px");
        }
    }

    // ---- Q8: the instrument panel on the Mac's second monitor ----
    //
    // On the 1206 reference square: RowWidth(pad) = 820, so x = (1206 − 820) / 2 = 193. With a toolbar bottom of 100
    // and gap 14.472: top = 100 + ToolbarGap 12 = 112; flight row bottom = 112 + 2 × 92.5 + ValueHeight 34.65 = 331.65;
    // engine row at 331.65 + 14.472 = 346.122, height 2 × 67.5 + 34.65 = 169.65, bottom 515.772; block H = 403.772.

    [Fact]
    public void ThePanelBlockIsTheFlightRowWithTheEngineRowStackedUnderIt()
    {
        var b = MonitorPanel.Block(1206, 100, 14.472, engine: true);
        Assert.Equal(193.0, b.X, 6);
        Assert.Equal(112.0, b.Y, 6);
        Assert.Equal(820.0, b.W, 6);
        Assert.Equal(403.772, b.H, 6);
        var e = MonitorPanel.EngineRow(1206, 1206, 14.472, 331.65);
        Assert.Equal((193.0, 346.122, 820.0, 169.65), (Math.Round(e.X, 6), Math.Round(e.Y, 6), Math.Round(e.W, 6), Math.Round(e.H, 6)));
    }

    [Fact]
    public void AGliderPanelIsTheFlightRowAlone()
    {
        var b = MonitorPanel.Block(1206, 100, 14.472, engine: false);
        Assert.Equal(219.65, b.H, 6);   // 331.65 − 112
    }

    [Theory]
    //          monitor      scale              offset x            offset y
    // 2560 × 1440: f = min(2560 × 0.92 / 820, 1440 × 0.92 / 403.772) = min(2.872195, 3.281) → width-bound;
    //              ox = (2560 − 820 f) / 2 = 102.4 (the margin), oy = (1440 − 403.772 f) / 2 = 140.144.
    [InlineData(2560, 1440, 2.872195122, 102.4, 140.144016)]
    [InlineData(1920, 1080, 2.154146341, 76.8, 105.108012)]
    public void ThePanelFillsTheMonitorUniformly(int mw, int mh, double f, double ox, double oy)
    {
        var (s, x, y) = MonitorPanel.Fit((193, 112, 820, 403.772), mw, mh);
        Assert.Equal(f, s, 6);
        Assert.Equal(ox, x, 4);
        Assert.Equal(oy, y, 4);
        // Inside the monitor's margin on every edge.
        Assert.True(x >= mw * MonitorPanel.MarginFraction - 1e-6 && y >= mh * MonitorPanel.MarginFraction - 1e-6);
        Assert.True(x + 820 * s <= mw * (1 - MonitorPanel.MarginFraction) + 1e-6 && y + 403.772 * s <= mh * (1 - MonitorPanel.MarginFraction) + 1e-6);
    }

    [Fact]
    public void APortraitMonitorIsHeightBoundAtWidthsEdge()
    {
        // 1080 × 1920 portrait: f = min(993.6 / 820 = 1.211707, 1766.4 / 403.772 = 4.37475) = 1.211707.
        var (s, x, y) = MonitorPanel.Fit((193, 112, 820, 403.772), 1080, 1920);
        Assert.Equal(1.211707317, s, 6);
        Assert.Equal(43.2, x, 4);
    }

    [Theory]
    [InlineData(2.872195122, 3464)]   // 2560 × 1440: 1206 × 2.8722 = 3463.87
    [InlineData(2.154146341, 2598)]   // 1920 × 1080: 2597.9
    [InlineData(0.1, 600)]            // a postage stamp is still laid out at a readable size
    [InlineData(10.0, 4096)]          // and an 8K wall at no more than 4096
    [InlineData(double.NaN, 1206)]
    public void ThePanelIsLaidOutNearTheMonitorsOwnScale(double scale, int side) => Assert.Equal(side, MonitorPanel.SideFor(scale));

    [Fact]
    public void NothingToFitGivesScaleZero()
    {
        Assert.Equal((0.0, 0.0, 0.0), MonitorPanel.Fit((0, 0, 0, 10), 1920, 1080));
        Assert.Equal((0.0, 0.0, 0.0), MonitorPanel.Fit((0, 0, 10, 10), 0, 1080));
    }

    // ---- Q7: the radio and intercom stay on the phone or the headset, never the TV ----

    [Theory]
    //          voice  display outputs                                  keepOff speaker radioAudible
    [InlineData(false, false, AudioPorts.Speaker, false, false, true)]                       // handheld: nothing changes
    [InlineData(true, false, AudioPorts.Speaker, false, false, true)]                        // intercom, no TV: as before
    [InlineData(true, false, AudioPorts.Headphones, false, false, true)]
    [InlineData(false, true, AudioPorts.Hdmi, false, false, false)]                          // no voice: game sound to the TV, no radio to hold
    [InlineData(true, true, AudioPorts.Hdmi, true, true, false)]                             // HDMI: to the speaker, held until it gets there
    [InlineData(true, true, AudioPorts.Speaker, true, true, true)]                           // ...and it got there: STAYS forced (no flip-flop)
    [InlineData(true, true, AudioPorts.AirPlay, true, true, false)]                          // AirPlay screen
    [InlineData(true, true, AudioPorts.Receiver, true, true, true)]                          // play-and-record's own default, then speaker
    [InlineData(true, true, AudioPorts.Headphones, true, false, true)]                       // a headset is plugged in: the radio is in it
    [InlineData(true, true, AudioPorts.Bluetooth, true, false, true)]
    [InlineData(true, true, AudioPorts.Usb, true, false, true)]
    [InlineData(true, false, AudioPorts.AirPlay, true, false, false)]                        // audio-only AirPlay (no display): off it, no override
    [InlineData(true, true, AudioPorts.Hdmi | AudioPorts.Headphones, true, false, false)]    // never mix the radio into a TV-bound route
    public void RadioRouteTable(bool voice, bool display, AudioPorts outputs, bool keepOff, bool speaker, bool audible)
    {
        var r = TvAudioPolicy.Decide(voice, display, outputs);
        Assert.Equal(keepOff, r.KeepOffTv);
        Assert.Equal(speaker, r.ForceSpeaker);
        Assert.Equal(audible, r.RadioAudible);
    }

    [Fact]
    public void TheForcedSpeakerIsAFixedPoint()
    {
        // Apply the decision, let iOS move the output, decide again: the speaker override must not undo itself.
        var r = TvAudioPolicy.Decide(true, true, AudioPorts.Hdmi);
        Assert.True(r.ForceSpeaker);
        var after = TvAudioPolicy.Decide(true, true, AudioPorts.Speaker);
        Assert.True(after.ForceSpeaker);
        Assert.True(after.RadioAudible);
    }

    [Theory]
    // The AVAudioSessionPort constants' string values (AVFAudio/AVAudioSessionTypes.h).
    [InlineData("Speaker", AudioPorts.Speaker)]
    [InlineData("Receiver", AudioPorts.Receiver)]
    [InlineData("Headphones", AudioPorts.Headphones)]
    [InlineData("BluetoothA2DPOutput", AudioPorts.Bluetooth)]
    [InlineData("BluetoothHFP", AudioPorts.Bluetooth)]
    [InlineData("BluetoothLE", AudioPorts.Bluetooth)]
    [InlineData("USBAudio", AudioPorts.Usb)]
    [InlineData("HDMI", AudioPorts.Hdmi)]
    [InlineData("DisplayPort", AudioPorts.Hdmi)]
    [InlineData("AirPlay", AudioPorts.AirPlay)]
    [InlineData("CarAudio", AudioPorts.Car)]
    [InlineData("LineOut", AudioPorts.Other)]
    [InlineData("", AudioPorts.None)]
    public void PortTypes(string type, AudioPorts expected) => Assert.Equal(expected, TvAudioPolicy.FromPortType(type));

    [Fact]
    public void APortListIsTheUnionOfItsPorts()
    {
        Assert.Equal(AudioPorts.Hdmi | AudioPorts.Speaker, TvAudioPolicy.FromPortTypes("HDMI, Speaker"));
        Assert.Equal(AudioPorts.None, TvAudioPolicy.FromPortTypes(null));
    }

    // ---- Q9 / Q10: pairing and the TLS pre-shared keys ----

    private static byte[] H(string hex) => Convert.FromHexString(hex);

    [Fact]
    public void HkdfMatchesRfc5869TestCase1()
    {
        var okm = Hkdf.DeriveKey(H("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b"), H("000102030405060708090a0b0c"), H("f0f1f2f3f4f5f6f7f8f9"), 42);
        Assert.Equal("3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865", CompanionPairing.Hex(okm));
        Assert.Equal("077709362c2e32df0ddc3f0dc47bba6390b6c73bb50f9c3122ec844ad7c2b3e5",
            CompanionPairing.Hex(Hkdf.Extract(H("000102030405060708090a0b0c"), H("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b"))));
    }

    [Fact]
    public void HkdfMatchesRfc5869TestCase3()
    {
        var okm = Hkdf.DeriveKey(H("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b"), Array.Empty<byte>(), Array.Empty<byte>(), 42);
        Assert.Equal("8da4e775a563c18f715f802a063c5a31b8a11f5c5ee1879ec3454e5f3c738d2d9d201395faa4b61a96c8", CompanionPairing.Hex(okm));
    }

    private const string Mac = "0123456789abcdef0123456789abcdef", Dev = "fedcba9876543210fedcba9876543210";

    [Fact]
    public void ThePinPskIsHkdfOfThePinSaltedWithTheMac()
        => Assert.Equal("bdbd3ffd9e653cba2fe87a15b810b441c32728cc62cb9749bab549cc8a178665", CompanionPairing.Hex(CompanionPairing.PinPsk("042117", Mac)));

    [Fact]
    public void TheLinkPskIsPerPairOfIds()
    {
        byte[] key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        Assert.Equal("5e1479fad72b1c15f317350306882ecf6d459e4e764a1efbe18405a41c3fd178", CompanionPairing.Hex(CompanionPairing.LinkPsk(key, Mac, Dev)));
        Assert.NotEqual(CompanionPairing.Hex(CompanionPairing.LinkPsk(key, Mac, Dev)), CompanionPairing.Hex(CompanionPairing.LinkPsk(key, Dev, Mac)));
        Assert.NotEqual(CompanionPairing.Hex(key), CompanionPairing.Hex(CompanionPairing.LinkPsk(key, Mac, Dev)));   // never the raw key
    }

    [Fact]
    public void QrOfferRoundTrips()
    {
        byte[] key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        string uri = CompanionPairing.OfferUri(Mac, "Ty's MacBook Pro", key);
        Assert.Equal("aeroplay://pair?v=1&id=0123456789abcdef0123456789abcdef&name=Ty%27s%20MacBook%20Pro&k=AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8", uri);
        Assert.True(CompanionPairing.TryParseOffer(uri, out var offer));
        Assert.Equal(Mac, offer!.MacId);
        Assert.Equal("Ty's MacBook Pro", offer.MacName);
        Assert.Equal(key, offer.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/pair?v=1")]
    [InlineData("aeroplay://pair?v=2&id=0123456789abcdef0123456789abcdef&k=AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8")]   // another version
    [InlineData("aeroplay://pair?v=1&id=XYZ&k=AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8")]                                // not an id
    [InlineData("aeroplay://pair?v=1&id=0123456789abcdef0123456789abcdef&k=AAEC")]                                           // short key
    [InlineData("aeroplay://pair?v=1&id=0123456789abcdef0123456789abcdef")]                                                  // no key
    [InlineData("aeroplay://pair?v=1&v=1&id=0123456789abcdef0123456789abcdef&k=AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8")] // repeated field
    [InlineData("aeroplay://pair?v=1&id=0123456789abcdef0123456789abcdef&k=A")]                                              // not base64
    public void AnythingElseIsRefused(string? uri) => Assert.False(CompanionPairing.TryParseOffer(uri, out _));

    [Fact]
    public void PinsAreSixDigitsAndUniform()
    {
        Assert.Equal("000000", CompanionPairing.PinFromBytes(new byte[] { 0, 0, 0, 0 }));
        Assert.Equal("999999", CompanionPairing.PinFromBytes(H("7f3df1ff")));                 // 4 293 999 999, the last kept word
        Assert.Equal("000001", CompanionPairing.PinFromBytes(H("ffffffff" + "01000000")));    // 4 294 967 295 is skipped
        Assert.Null(CompanionPairing.PinFromBytes(H("ffffffff")));
        for (int i = 0; i < 50; i++) Assert.True(CompanionPairing.IsPin(CompanionPairing.NewPin()));
        Assert.False(CompanionPairing.IsPin("12345"));
        Assert.False(CompanionPairing.IsPin("12a456"));
    }

    [Fact]
    public void ThePairingWindowIsTwoMinutesFiveTriesAndOneUse()
    {
        var w = new PairingWindow("042117", new byte[32], now: 1000);
        Assert.True(w.IsOpen(1000));
        Assert.True(w.IsOpen(1120));
        Assert.False(w.IsOpen(1120.01));
        Assert.Equal(60.0, w.SecondsLeft(1060), 9);
        for (int i = 0; i < 4; i++) w.Failed();
        Assert.True(w.IsOpen(1010));
        Assert.Equal(1, w.TriesLeft);
        w.Failed();
        Assert.False(w.IsOpen(1010));   // the fifth failure closes it
        var used = new PairingWindow("042117", new byte[32], 0);
        used.Used();
        Assert.False(used.IsOpen(1));
    }

    [Fact]
    public void TheQrPskIsHkdfOfTheSecretSaltedWithTheMac()
    {
        byte[] secret = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        Assert.Equal("8f0a5bfcbb7efc4b580fb998fa00dd2b0010ba7e4ff3b9a9c84b4c92f2220315", CompanionPairing.Hex(CompanionPairing.QrPsk(secret, Mac)));
    }

    [Fact]
    public void TheProofsAreLabelledHmacs()
    {
        byte[] k = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        const string nonce = "00112233445566778899aabbccddeeff";
        Assert.Equal("7ce6b90926ddea8129542926e0984e0839f325462ca93711a499a6a2c678feb7", CompanionAuth.PairProof(k, nonce, Mac, Dev));
        Assert.Equal("a1be680c77ff7eaf769ab7a7842e8e6d7c637adc43ad8a1b3eb17e17b80b444c", CompanionAuth.LinkProof(k, nonce, Mac, Dev));
        Assert.Equal("99879956f09392d48b742da7ae6fe6003817569a536372b0d47ebbe2759c8452", CompanionAuth.ServerProof(k, nonce, Mac, Dev));
        Assert.True(CompanionAuth.Same("abc", "abc"));
        Assert.False(CompanionAuth.Same("abc", "abd"));
        Assert.False(CompanionAuth.Same("abc", "ab"));
        Assert.False(CompanionAuth.Same(null, "ab"));
        Assert.Equal(32, CompanionAuth.NewNonce().Length);   // 16 random bytes
    }

    [Fact]
    public void IdentitiesNameTheDevice()
    {
        Assert.Equal("aero-link/1/" + Dev, CompanionPairing.LinkIdentity(Dev));
        Assert.Equal(Dev, CompanionPairing.DeviceOfIdentity("aero-link/1/" + Dev));
        Assert.Null(CompanionPairing.DeviceOfIdentity(CompanionPairing.PinIdentity));
        Assert.Null(CompanionPairing.DeviceOfIdentity("aero-link/1/NOT-HEX"));
        Assert.True(CompanionPairing.IsDeviceId(CompanionPairing.NewDeviceId()));
    }
}
