using System.IO;
using UnityEngine;

namespace FlyingGame.EditorTools
{
    /// <summary>Legacy C172 batch entry points.</summary>
    public static class C172Render
    {
        public static void Render() => AircraftRender.Render("c172-like", "type",
            Path.Combine(Application.dataPath, "../../build/c172-render"));
        public static void RenderTalkFlying() => AircraftRender.Render("c172-like", "talk-flying",
            Path.Combine(Application.dataPath, "../../build/c172-render/talkflying"));
    }
}
