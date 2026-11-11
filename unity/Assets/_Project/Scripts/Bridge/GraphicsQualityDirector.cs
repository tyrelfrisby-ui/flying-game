using FlyingGame.Core;
using UnityEngine;

namespace FlyingGame.Bridge
{
    /// <summary>
    /// The thin Unity half of the graphics slider. The policy — the tiers, the knob table, the downgrade and
    /// recovery state machine, the wording of the warning — all lives in <see cref="GraphicsQuality"/> and
    /// <see cref="AutoQuality"/>, headless and under test. This file does three things and nothing else:
    /// <list type="number">
    /// <item>answers "what tier are we on" to anyone who asks, before any instance exists, because most of the
    /// expensive knobs are read while the world is being BUILT;</item>
    /// <item>samples the two signals once a frame and hands them to the state machine;</item>
    /// <item>applies a <see cref="GraphicsQuality.Knobs"/> to the things that can change in flight.</item>
    /// </list>
    ///
    /// <para><b>The signal.</b> <see cref="FlightSimDriver.CatchUpCappedFrames"/> is the primary trigger, not the
    /// frame rate. A capped frame means the fixed-step loop threw simulated time away (<see cref="SimCatchUp"/>) and
    /// the aeroplane flew a trajectory physics did not produce. Nothing was reading that counter before this.</para>
    /// </summary>
    public sealed class GraphicsQualityDirector : MonoBehaviour
    {
        // ---- the one scale, readable from anywhere, including from world-build code before Awake --------------

        private static GraphicsQuality.Tier _effective = GraphicsQuality.Tier.Balanced;
        private static bool _started;

        /// <summary>What is actually being drawn. Never above <see cref="SessionSettings.GraphicsTier"/>.</summary>
        public static GraphicsQuality.Tier Effective
        {
            get { EnsureStarted(); return _effective; }
        }

        /// <summary>The knob table for the tier in force. The ONE answer: every expensive subsystem reads this.</summary>
        public static GraphicsQuality.Knobs Knobs => GraphicsQuality.Knobs.For(Effective);

        /// <summary>The Unity quality level this tier maps to, for the call sites that used to read
        /// <c>QualitySettings.GetQualityLevel()</c> directly and compare it against a hand-written number. The
        /// mapping keeps their meaning: <c>&lt;= 1</c> is still "the cheap tiers", <c>&gt; 2</c> still "the top".</summary>
        public static int UnityLevel => Knobs.UnityLevel;

        /// <summary>"The cheap tiers": what the string-matching <c>tier.Contains("low")</c> reads used to mean.</summary>
        public static bool Low => Effective <= GraphicsQuality.Tier.Low;

        /// <summary>MINIMUM / LOW / BALANCED / HIGH.</summary>
        public static string TierName => GraphicsQuality.Label(Effective);

        /// <summary>This machine, for the default tier and the frame time the auto-downgrade is judged against.</summary>
        public static GraphicsQuality.Platform Platform =>
            XrHeadset.Active ? GraphicsQuality.Platform.Headset
            : Application.isMobilePlatform ? GraphicsQuality.Platform.Handheld
            : GraphicsQuality.Platform.Desktop;

        private static void EnsureStarted()
        {
            if (_started) return;
            _started = true;
            SessionSettings.LoadPrefs();
            _effective = SessionSettings.GraphicsTier;
        }

        /// <summary>Put the tier back to the pilot's choice. Called when the slider moves and when a flight starts:
        /// a new flight re-proves the choice from scratch rather than inheriting the last flight's penalty box.</summary>
        public static void ResetToChosen()
        {
            EnsureStarted();
            _effective = SessionSettings.GraphicsTier;
            if (_instance != null) _instance.OnChosenChanged();
            else ApplyStatics(GraphicsQuality.Knobs.For(_effective));
        }

        // ---- the instance -------------------------------------------------------------------------------------

        private static GraphicsQualityDirector _instance;

        /// <summary>The warning waiting to be shown, or null. The HUD reads it, shows it once and calls
        /// <see cref="DismissWarning"/>; the state machine rate-limits it so it cannot nag.</summary>
        public static AutoQuality.Notice? Warning => _instance?.Auto.Pending;

        public static void DismissWarning() => _instance?.Auto.Dismiss();

        /// <summary>True while the machine is holding the picture below what the pilot asked for — the HUD shows a
        /// quiet "GFX LOW (auto)" for as long as this is true, which is how a pilot sees it recover without a second
        /// message.</summary>
        public static bool AutoDowngraded => _instance != null && _instance.Auto.Downgraded;

        public AutoQuality Auto { get; private set; }

        private int _lastCapped;
        private double _lastDiscarded;
        private WaterReflection _reflection;
        private GroundShadow _shadow;

        private void Awake()
        {
            EnsureStarted();
            _instance = this;
            Auto = new AutoQuality(SessionSettings.GraphicsTier, new AutoQuality.Policy
            {
                TargetFrameSec = GraphicsQuality.TargetFrameSec(Platform, XrHeadset.Active ? XrHeadset.CurrentRefreshHz : 60.0),
            });
            _lastCapped = FlightSimDriver.CatchUpCappedFrames;
            _effective = Auto.Effective;
            Apply(Auto.Knobs);
        }

        private void OnDestroy() { if (_instance == this) _instance = null; }

        /// <summary>The slider moved: the machine starts again from the new choice (AutoQuality.SetChosen).</summary>
        public void OnChosenChanged()
        {
            Auto.SetChosen(SessionSettings.GraphicsTier);
            _effective = Auto.Effective;
            Apply(Auto.Knobs);
        }

        private void Update()
        {
            if (SessionSettings.MenuOpen || SessionSettings.ReplayActive)
            {
                // A paused sim does not step, so it cannot cap; a replay is not flying. Keep the counters in step
                // so the first frame back is not charged for the whole pause.
                _lastCapped = FlightSimDriver.CatchUpCappedFrames;
                return;
            }

            int capped = FlightSimDriver.CatchUpCappedFrames;
            double discarded = FlightSimDriver.CatchUpDiscardedSec;
            int dCapped = capped - _lastCapped;
            double dDiscarded = discarded - _lastDiscarded;
            _lastCapped = capped; _lastDiscarded = discarded;

            if (!Auto.Sample(Time.unscaledDeltaTime, dCapped, dDiscarded)) return;
            _effective = Auto.Effective;
            Apply(Auto.Knobs);
            if (Auto.Pending.HasValue) Debug.Log("[Graphics] " + Auto.Pending.Value.Message);
        }

        // ---- applying a tier ----------------------------------------------------------------------------------

        /// <summary>
        /// What can be changed in flight. Everything else on the table is STARTUP cost — geometry built when the
        /// world or the cloud field is built — and takes effect on the next flight; <see cref="GraphicsQuality.Diff"/>
        /// says so in the warning rather than promising a saving that will not arrive.
        /// </summary>
        private void Apply(GraphicsQuality.Knobs k)
        {
            ApplyStatics(k);

            if (_reflection == null) _reflection = Object.FindAnyObjectByType<WaterReflection>();
            if (_reflection != null)
            {
                _reflection.TextureSize = k.ReflectionTextureSize;
                _reflection.ReflectScenery = k.ReflectScenery;
                _reflection.enabled = k.ReflectionOn;
            }

            if (_shadow == null) _shadow = Object.FindAnyObjectByType<GroundShadow>();
            if (_shadow != null)
            {
                _shadow.MaxAltitudeM = (float)k.GroundShadowMaxAltitudeM;
                _shadow.enabled = k.GroundShadowOn;
            }
        }

        /// <summary>The part of a tier that needs no scene object: Unity's own level, which carries its shadows,
        /// anti-aliasing and LOD bias, and which <c>UrbanFacades</c> and the airport dressing read through
        /// <see cref="UnityLevel"/>. The cloud cull radius, the fog range and the wake grid are read straight from
        /// <see cref="Knobs"/> by the code that uses them, so there is nothing to push.</summary>
        private static void ApplyStatics(GraphicsQuality.Knobs k)
        {
            if (QualitySettings.GetQualityLevel() != k.UnityLevel)
                QualitySettings.SetQualityLevel(k.UnityLevel, false);   // false: no expensive mid-flight reload
        }
    }
}
