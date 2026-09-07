using FlyingGame.Sim;
using FlyingGame.Sim.Challenge;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// Runs a graded challenge in Unity: loads a ChallengeDefinition, spawns the aircraft at its start
    /// state through the shared ChallengeRunner, hands the sim to the FlightSimDriver to fly, and ticks
    /// the runner each frame. The ChallengeHud reads Runner for live bands and the final score card.
    /// Press C to (re)start the current challenge; N cycles to the next one.
    /// </summary>
    public sealed class ChallengeController : MonoBehaviour
    {
        private static readonly string[] Ladder =
        {
            "a1c1-wings-level", "a1c2-best-glide", "a1c3-cardinal-turn", "a1c4-stall-recover",
        };

        public ChallengeRunner Runner { get; private set; }
        public string CurrentId => Ladder[_index];
        public bool Active { get; private set; }

        private FlightSimDriver _driver;
        private int _index;
        private double _accumulator;

        private void Awake() => _driver = GetComponent<FlightSimDriver>();

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.C))
            {
                StartChallenge();
            }
            if (Input.GetKeyDown(KeyCode.N))
            {
                _index = (_index + 1) % Ladder.Length;
                StartChallenge();
            }

            if (Active && Runner != null && !Runner.Complete)
            {
                _accumulator += Time.deltaTime;
                while (_accumulator >= SimLoop.DefaultFixedDtSec)
                {
                    Runner.Tick(_driver.Sim.Aircraft, SimLoop.DefaultFixedDtSec);
                    _accumulator -= SimLoop.DefaultFixedDtSec;
                }
            }
        }

        private void StartChallenge()
        {
            ChallengeDefinition def = UnityChallengeLoader.Load(CurrentId);
            var config = UnityAircraftConfigLoader.LoadFromStreamingAssets(
                string.IsNullOrEmpty(def.AircraftId) ? _driver.AircraftId : def.AircraftId);
            Runner = new ChallengeRunner(def);
            var aircraft = Runner.Spawn(config);
            _driver.AdoptSim(new SimLoop(aircraft));
            _accumulator = 0;
            Active = true;
        }
    }
}
