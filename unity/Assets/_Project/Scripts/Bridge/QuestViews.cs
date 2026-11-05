namespace FlyingGame.Bridge
{
    /// <summary>
    /// THE VIEWS A HEADSET OFFERS, in the VR menu's order (Ty 2026-11-04). Only those that can be comfortable with the
    /// head free and the horizon level (QuestRig + Core's QuestOutsideView):
    ///  - Cockpit: the seat's eye.
    ///  - Chase: behind the aeroplane on its flight path (the desktop's "Relative wind"), its offset eased, heading in snaps.
    ///  - Fixed-direction chase: the desktop's "Direction lock": the camera keeps one direction in the world and rides
    ///    with the aeroplane — no turning at all, the steadiest outside view there is.
    ///  - Fly-by: parked ahead and to the side, still in the world, watching it pass (the tower's view of a fly-by).
    /// Left out on purpose: Tail, Left / Right side, Top, Bottom and Front are bolted to the airframe, so the camera
    /// itself would swing round with every roll and pitch, levelled or not.
    /// </summary>
    public static class QuestViews
    {
        public static readonly ChaseCamera.View[] Offered =
        {
            ChaseCamera.View.Cockpit, ChaseCamera.View.RelativeWind, ChaseCamera.View.Locked, ChaseCamera.View.Flyby,
        };

        public static string Label(ChaseCamera.View v) => v switch
        {
            ChaseCamera.View.Cockpit => "Cockpit",
            ChaseCamera.View.RelativeWind => "Chase",
            ChaseCamera.View.Locked => "Fixed-direction chase",
            ChaseCamera.View.Flyby => "Fly-by",
            _ => ChaseCamera.ViewNames[(int)v],
        };

        /// <summary>The offered view <paramref name="d"/> steps from <paramref name="now"/> (one not offered goes to the first).</summary>
        public static ChaseCamera.View Next(ChaseCamera.View now, int d)
        {
            int i = System.Array.IndexOf(Offered, now);
            if (i < 0) return Offered[0];
            int n = Offered.Length;
            return Offered[((i + d) % n + n) % n];
        }
    }
}
