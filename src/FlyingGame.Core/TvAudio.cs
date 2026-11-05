namespace FlyingGame.Core;

/// <summary>Audio output ports, as iOS reports them (<c>AVAudioSessionPortDescription.portType</c>), grouped by what the
/// radio cares about.</summary>
[Flags]
public enum AudioPorts
{
    None = 0,
    Speaker = 1 << 0,
    Receiver = 1 << 1,
    Headphones = 1 << 2,
    Bluetooth = 1 << 3,
    Usb = 1 << 4,
    Hdmi = 1 << 5,
    AirPlay = 1 << 6,
    Car = 1 << 7,
    Other = 1 << 8,
}

/// <summary>What the audio session should do this frame (Plugins/iOS/VoiceIO.mm carries it out).</summary>
public readonly struct VoiceRoute
{
    /// <summary>Keep the app's audio off the TV: the play-and-record session without AirPlay, so iOS never offers it one.</summary>
    public readonly bool KeepOffTv;
    /// <summary>A TV display is connected and nothing is plugged in to listen on: send the audio to the phone's speaker
    /// (an HDMI adapter is an eligible play-and-record output, so leaving it alone would put the radio on the TV).</summary>
    public readonly bool ForceSpeaker;
    /// <summary>The radio may be mixed into the game's sound. False while the route is still on a TV.</summary>
    public readonly bool RadioAudible;

    public VoiceRoute(bool keepOffTv, bool forceSpeaker, bool radioAudible)
    {
        KeepOffTv = keepOffTv; ForceSpeaker = forceSpeaker; RadioAudible = radioAudible;
    }

    public override string ToString() => $"keepOffTv {KeepOffTv} · speaker {ForceSpeaker} · radio {(RadioAudible ? "audible" : "held")}";
}

/// <summary>
/// RADIO AND INTERCOM WITH A TV CONNECTED (Ty 2026-11-04, Q7: "radio/intercom audio stays on the phone or headset, not
/// the TV"). iOS gives an app ONE audio route, so the radio cannot go to the phone while the engine goes to the TV in
/// the same session: whenever voice is in use and a TV is there, the whole session is held on the phone or the headset.
/// Without voice in use nothing changes and the game sound goes to the TV as iOS routes it. And whatever happens to
/// the route, a radio call is never mixed into audio that is on its way to a TV: it is held silent instead.
/// </summary>
public static class TvAudioPolicy
{
    /// <summary>Outputs that are a TV (or a TV's receiver).</summary>
    public const AudioPorts TvPorts = AudioPorts.Hdmi | AudioPorts.AirPlay;

    /// <summary>Outputs a pilot listens on privately: headphones, a Bluetooth headset, a USB headset.</summary>
    public const AudioPorts HeadsetPorts = AudioPorts.Headphones | AudioPorts.Bluetooth | AudioPorts.Usb;

    /// <param name="voiceInUse">Intercom on, or in a room with a radio.</param>
    /// <param name="tvDisplay">A second display is connected (AirPlay or HDMI), whatever it shows.</param>
    /// <param name="outputs">The session's current outputs.</param>
    public static VoiceRoute Decide(bool voiceInUse, bool tvDisplay, AudioPorts outputs)
    {
        bool tvAudio = (outputs & TvPorts) != 0;
        bool keepOff = voiceInUse && (tvDisplay || tvAudio);
        // Keyed to the DISPLAY, not to the current output: the override itself moves the output to the speaker, and a
        // rule that read the output would then release it and put it back on the TV, forever.
        bool speaker = keepOff && tvDisplay && (outputs & HeadsetPorts) == 0;
        return new VoiceRoute(keepOff, speaker, !tvAudio);
    }

    /// <summary>One iOS port type (the AVAudioSessionPort constants' string values).</summary>
    public static AudioPorts FromPortType(string? portType) => portType switch
    {
        "Speaker" => AudioPorts.Speaker,
        "Receiver" => AudioPorts.Receiver,
        "Headphones" => AudioPorts.Headphones,
        "BluetoothA2DPOutput" or "BluetoothHFP" or "BluetoothLE" => AudioPorts.Bluetooth,
        "USBAudio" => AudioPorts.Usb,
        "HDMI" or "DisplayPort" => AudioPorts.Hdmi,
        "AirPlay" => AudioPorts.AirPlay,
        "CarAudio" => AudioPorts.Car,
        null or "" => AudioPorts.None,
        _ => AudioPorts.Other,
    };

    /// <summary>The comma-separated list VoiceIO reports (<c>VO_RouteOutputs</c>).</summary>
    public static AudioPorts FromPortTypes(string? list)
    {
        var ports = AudioPorts.None;
        if (string.IsNullOrEmpty(list)) return ports;
        foreach (string t in list!.Split(',')) ports |= FromPortType(t.Trim());
        return ports;
    }
}
