using UnityEngine;

namespace LeagueVR.Champions
{
    /// <summary>
    /// The champion's full model as a stand-in while the view is outside the body (Zeri's long vault, watching from the
    /// brush). No scripts or colliders, sized like the first-person body, playing the champion's own clips.
    /// </summary>
    public class ChampionPuppet : MonoBehaviour
    {
        Animation clips;
        string playing;

        public static ChampionPuppet Spawn(PlayerChampion player, Vector3 at, Quaternion rotation)
        {
            var model = player.Definition ? player.Definition.model : null;
            if (!model)
                return null;
            var go = Instantiate(model, at, rotation);
            go.name = player.ChampionName + " (seen from outside)";
            go.transform.localScale *= player.Body ? player.Body.BodyScale : 1;
            foreach (var behaviour in go.GetComponentsInChildren<MonoBehaviour>())
                behaviour.enabled = false;
            foreach (var collider in go.GetComponentsInChildren<Collider>())
                collider.enabled = false;
            var puppet = go.AddComponent<ChampionPuppet>();
            puppet.clips = go.GetComponentInChildren<Animation>();
            if (puppet.clips)
                puppet.clips.cullingType = AnimationCullingType.AlwaysAnimate;
            player.Track(go);
            return puppet;
        }

        /// <summary>Plays the first clip that exists of <paramref name="names"/>, looping or held at its end, over <paramref name="seconds"/> if given.</summary>
        public void Play(bool loop, float seconds, params string[] names)
        {
            if (!clips)
                return;
            foreach (var name in names)
            {
                var state = clips[name];
                if (state == null)
                    continue;
                if (playing == name)
                    return;
                playing = name;
                state.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
                state.speed = seconds > 0 ? state.length / seconds : 1;
                clips.CrossFade(name, .15f);
                return;
            }
        }
    }
}
