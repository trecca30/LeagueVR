using UnityEngine;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    /// <summary>The selectable champions and the one used for the next match.</summary>
    [DefaultExecutionOrder(-110)]
    public class ChampionRoster : MonoBehaviour
    {
        public ChampionDefinition[] champions;
        public int selected;
        public PlayerChampion player;
        public ChampionVRAvatar avatar;

        public ChampionDefinition Active { get; private set; }
        public ChampionDefinition Selected => champions != null && champions.Length > 0 ? champions[Mathf.Clamp(selected, 0, champions.Length - 1)] : null;

        GwenAvatar gwen;

        void Awake()
        {
            if (!player)
                player = GetComponent<PlayerChampion>();
            if (!avatar)
                avatar = GetComponent<ChampionVRAvatar>();
            gwen = GetComponent<GwenAvatar>();
        }

        void Start()
        {
            // Equip the default champion so the menu scene already has working hands and stats.
            if (Active == null && Selected)
                ApplySelection();
        }

        public void Select(int index) => selected = Mathf.Clamp(index, 0, champions.Length - 1);

        /// <summary>Equips the selected champion: first-person body, kit and stats.</summary>
        public void ApplySelection()
        {
            Active = Selected;
            // The body comes first so the kit can attach its effects to it when it is equipped.
            bool isGwen = Active.id == ChampionId.Gwen;
            if (gwen)
                gwen.SetVisible(isGwen);
            if (avatar)
                avatar.SetChampion(isGwen ? null : Active);
            player.Body = isGwen ? gwen : avatar;
            player.SetChampion(Active);
            player.GetComponent<RiftEconomy>()?.SetChampionBase(Active);
            player.ResetPractice();
        }
    }
}
