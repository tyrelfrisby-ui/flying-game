using System.Collections.Generic;
using FlyingGame.Core.DataContracts;

namespace FlyingGame.Core;

/// <summary>
/// WHICH SEAT, by the seat's ID, never by its position in the list (owner Ty, 2026-11-02, after flying the Pitts on
/// the Quest: "i chose the back seat and it put me in the front seat").
///
/// The cockpit view used to keep a bare index into whatever aeroplane happened to be loaded. The landing page has
/// the 2-33 glider loaded (FlightSimDriver's default), whose seats are front = 0, rear = 1; choosing "Rear seat"
/// there stored 1, FLY spawned the Pitts, and the Pitts' seat 1 is its FRONT seat. Any two aeroplanes that list
/// their seats in a different order did the same. A seat ID ("rear", "front", "pilot") means the same seat in
/// every aeroplane that has one, and an aeroplane that has no such seat falls back to its PRIMARY seat.
///
/// THE PRIMARY SEAT IS LISTED FIRST. docs/models/eyepoints.json: "The PRIMARY seat is the solo seat: the rear on a
/// tandem, the front on the 2-33, the left on the 172", and every config lists that seat first
/// (SeatChoiceTests pins it), so the default for a pilot who has never chosen is index 0.
/// </summary>
public static class SeatChoice
{
    /// <summary>The seat a pilot asked for by ID, or the primary seat (0) when the ID is empty or this aeroplane
    /// has no such seat. 0 for an aeroplane with no authored seats at all.</summary>
    public static int IndexOf(IReadOnlyList<StationConfig>? seats, string? seatId)
    {
        if (seats == null || seats.Count == 0 || string.IsNullOrEmpty(seatId)) return 0;
        for (int i = 0; i < seats.Count; i++)
            if (string.Equals(seats[i].Id, seatId, System.StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }

    /// <summary>The ID of the seat at <paramref name="index"/> (clamped), or null where there are no seats.</summary>
    public static string? IdAt(IReadOnlyList<StationConfig>? seats, int index)
    {
        if (seats == null || seats.Count == 0) return null;
        int i = index < 0 ? 0 : index >= seats.Count ? seats.Count - 1 : index;
        return seats[i].Id;
    }

    /// <summary>The seat <paramref name="step"/> places on from the one chosen, wrapping: what a menu's &lt; &gt; does.</summary>
    public static string? Cycle(IReadOnlyList<StationConfig>? seats, string? seatId, int step)
    {
        if (seats == null || seats.Count == 0) return null;
        int n = seats.Count;
        return IdAt(seats, ((IndexOf(seats, seatId) + step) % n + n) % n);
    }

    /// <summary>What a menu calls the seat: its label, else "Seat n".</summary>
    public static string Label(IReadOnlyList<StationConfig>? seats, string? seatId)
    {
        if (seats == null || seats.Count == 0) return "Cockpit";
        int i = IndexOf(seats, seatId);
        return string.IsNullOrEmpty(seats[i].Label) ? $"Seat {i + 1}" : seats[i].Label;
    }
}
