namespace FlyingGame.Core.LocalMp;

/// <summary>
/// LOCAL 2-PLAYER: which gamepad flies which aeroplane (Ty 2026-11-05: "two 8BitDo controllers, split screen, one view
/// per plane"). Pure bookkeeping on the Input System's device ids, so it is tested without Unity.
///
/// The rule is sticky: a pad that holds a seat keeps it for as long as it is connected, whatever the other pad does,
/// so pressing a button on pad 2 never takes pad 1's aeroplane (the single-player <c>Gamepad.current</c> follows
/// whichever pad spoke last, which is exactly what two players must not have). An empty seat is filled by the
/// lowest-numbered free pad, which on a fresh pairing is the one connected first: P1 = the first pad, P2 = the second.
/// A pad that drops out frees its seat; when the same device comes back (the Input System keeps a reconnected
/// device's id) it gets its old seat back if that seat is still empty.
///
/// With one pad, P1 has it and P2 has none: P2's aeroplane is PARKED on its trimmed, hands-off start (the
/// documented fallback). The touch controls stay P1's.
/// </summary>
public sealed class PadBinding
{
    public const int NoPad = -1;
    private readonly int[] _seat;
    private readonly int[] _lastSeen;

    public PadBinding(int seats)
    {
        if (seats < 1) throw new ArgumentOutOfRangeException(nameof(seats));
        _seat = new int[seats];
        _lastSeen = new int[seats];
        for (int i = 0; i < seats; i++) { _seat[i] = NoPad; _lastSeen[i] = NoPad; }
    }

    public int Seats => _seat.Length;

    /// <summary>The device id flying seat <paramref name="index"/>, or <see cref="NoPad"/>.</summary>
    public int PadFor(int index) => index >= 0 && index < _seat.Length ? _seat[index] : NoPad;

    /// <summary>The seat a device flies, or −1.</summary>
    public int SeatOf(int deviceId)
    {
        for (int i = 0; i < _seat.Length; i++) if (_seat[i] == deviceId && deviceId != NoPad) return i;
        return -1;
    }

    /// <summary>Re-bind against the pads that are live now (any order). Returns true when any seat changed.</summary>
    public bool Update(IReadOnlyList<int> liveDeviceIds)
    {
        bool changed = false;
        // 1. A seat whose pad is gone is freed (remembering who had it).
        for (int i = 0; i < _seat.Length; i++)
        {
            if (_seat[i] == NoPad) continue;
            bool live = false;
            foreach (int id in liveDeviceIds) if (id == _seat[i]) { live = true; break; }
            if (!live) { _lastSeen[i] = _seat[i]; _seat[i] = NoPad; changed = true; }
        }
        // 2. A returning pad gets its old seat back if it is still empty.
        for (int i = 0; i < _seat.Length; i++)
        {
            if (_seat[i] != NoPad || _lastSeen[i] == NoPad) continue;
            foreach (int id in liveDeviceIds)
                if (id == _lastSeen[i] && SeatOf(id) < 0) { _seat[i] = id; changed = true; break; }
        }
        // 3. Empty seats, in order, take the lowest-numbered free pads.
        for (int i = 0; i < _seat.Length; i++)
        {
            if (_seat[i] != NoPad) continue;
            int best = int.MaxValue;
            foreach (int id in liveDeviceIds)
            {
                if (id == NoPad || SeatOf(id) >= 0 || id >= best) continue;
                // A pad that last flew ANOTHER seat that is still empty waits for it rather than being taken here.
                bool reserved = false;
                for (int j = 0; j < _seat.Length; j++) if (j != i && _seat[j] == NoPad && _lastSeen[j] == id) reserved = true;
                if (!reserved) best = id;
            }
            if (best != int.MaxValue) { _seat[i] = best; changed = true; }
        }
        return changed;
    }
}
