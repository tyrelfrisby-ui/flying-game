using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>Air Racing: feeds the aircraft position to the Core scorer and shows the race line in the HUD.</summary>
    public sealed class RaceController : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public AirRace Race { get; private set; }
        public bool Active { get; private set; }

        public void Begin() { Race = new AirRace(); Race.Reset(); Active = true; }
        public void End() { Active = false; }

        private void Update()
        {
            if (!Active || Race == null || Driver?.Sim == null) return;
            var s = Driver.Sim.Aircraft.State;
            var q = s.Attitude;
            double bank = System.Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));
            Race.Update(s.Position, bank, Time.deltaTime);
        }

        public string Line => !Active || Race == null ? null
            : Race.Finished ? $"AIR RACE  {Race.LastEvent}   (penalties {Race.PenaltySec:F0} s)"
            : $"AIR RACE  next {Race.Next + 1}/{RaceCourse.Elements.Length}   {Race.TotalSec:F1} s   {Race.LastEvent}";
    }

    /// <summary>STOL contest on the dirt strip: touchdown at/after the line, stop short. Shows the score.</summary>
    public sealed class StolController : MonoBehaviour
    {
        public FlightSimDriver Driver;
        public StolRun Run { get; private set; }
        public bool Active { get; private set; }

        public void Begin(WorldTerrain.Airport a) { Run = StolRun.ForAirport(a); Active = true; }
        public void End() { Active = false; }

        private void Update()
        {
            if (!Active || Run == null || Driver?.Sim == null) return;
            var ac = Driver.Sim.Aircraft;
            var s = ac.State;
            bool onGround = LandingGear.AnyMainWheelOnGround(ac.Config, s);
            var vW = s.Attitude.Rotate(s.Velocity);
            double gs = System.Math.Sqrt(vW.X * vW.X + vW.Y * vW.Y);
            Run.Update(s.Position, onGround, gs);
        }

        public string Line => !Active || Run == null ? null : Run.Phase switch
        {
            StolRun.Phases.Approach => "STOL  land at or past the white line, stop short",
            StolRun.Phases.Rolling => $"STOL  touchdown +{Run.TouchdownPastLineM * 3.28084:F0} ft — stop!",
            StolRun.Phases.Stopped => $"STOL  SCORE {Run.StopPastLineM * 3.28084:F0} ft   (touchdown +{Run.TouchdownPastLineM * 3.28084:F0} ft)",
            _ => $"STOL  DQ — {Run.Reason}",
        };
    }
}
