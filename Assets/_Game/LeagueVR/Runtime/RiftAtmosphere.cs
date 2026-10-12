using UnityEngine;

namespace LeagueVR
{
    /// <summary>
    /// The Rift's distance haze, read by the map shaders (Rift Stylized and Rift Foundation Atlas): ground fades toward
    /// the sky colour between <see cref="start"/> and <see cref="end"/> metres from the eye, up to <see cref="strength"/>.
    /// It works on true distance, so nothing near the player is ever hazed.
    /// </summary>
    [ExecuteAlways]
    public class RiftAtmosphere : MonoBehaviour
    {
        public Color color = new(.7f, .79f, .86f);
        public float start = 90, end = 240;
        [Range(0, 1)] public float strength = .4f;

        static readonly int HazeId = Shader.PropertyToID("_RiftHaze"), HazeColorId = Shader.PropertyToID("_RiftHazeColor");

        void OnEnable() => Publish();

        void OnValidate() => Publish();

        void OnDisable() => Shader.SetGlobalVector(HazeId, Vector4.zero);

        void Publish()
        {
            Shader.SetGlobalVector(HazeId, new Vector4(start, Mathf.Max(start + 1, end), strength, 0));
            Shader.SetGlobalColor(HazeColorId, color);
        }
    }
}
