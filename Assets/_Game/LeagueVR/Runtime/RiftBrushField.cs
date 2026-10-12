using UnityEngine;

namespace LeagueVR
{
    /// <summary>
    /// Summoner's Rift brush at runtime. Tells the brush shader where the player walks, so blades bend away, and answers
    /// League's brush rule for hiding: a unit standing in brush is hidden from enemies outside that same patch, unless
    /// it has just attacked or cast. The patches come from League's own navigation grid (see <see cref="RiftNavGrid"/>).
    /// </summary>
    public class RiftBrushField : MonoBehaviour
    {
        /// <summary>Seconds a unit stays visible from outside after attacking or casting from brush.</summary>
        public const float RevealSeconds = 1.5f;

        public float pushRadius = .7f;

        static readonly int PushId = Shader.PropertyToID("_RiftBrushPush");

        /// <summary>The player's feet each frame (set by the match), or null.</summary>
        public static System.Func<Vector3?> Walker;

        void Update()
        {
            var feet = Walker?.Invoke();
            Shader.SetGlobalVector(PushId, feet.HasValue ? new Vector4(feet.Value.x, feet.Value.y, feet.Value.z, pushRadius) : Vector4.zero);
        }

        void OnDisable() => Shader.SetGlobalVector(PushId, Vector4.zero);

        /// <summary>
        /// True when <paramref name="hidden"/> (standing at <paramref name="at"/>) is concealed by brush from
        /// <paramref name="viewer"/>: it stands in a patch the viewer is not in and has not revealed itself lately.
        /// </summary>
        public static bool Conceals(Vector3 at, Combatant viewer, float lastAction)
        {
            var grid = RiftNavGrid.Active;
            if (!grid || !viewer || Time.time - lastAction < RevealSeconds)
                return false;
            int patch = grid.BrushAt(at);
            return patch != 0 && grid.BrushAt(viewer.transform.position) != patch;
        }
    }
}
