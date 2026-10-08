using UnityEngine;

namespace FlyingGame.Bridge.Widget
{
    /// <summary>References the Syphon / NDI shader resources so a player build keeps them (made by BuildScript.BuildWidget at
    /// Assets/_Project/Widget/Resources/WidgetAssets.asset).</summary>
    public sealed class WidgetAssets : ScriptableObject
    {
        public Object Syphon;
        public Object Ndi;
    }
}
