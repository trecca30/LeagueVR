using UnityEngine;
using LeagueVR.Match;
namespace LeagueVR.Champions
{
    [DefaultExecutionOrder(-110)] public class ChampionRoster:MonoBehaviour
    {
        public ChampionDefinition[] champions;
        public int selected;
        public GwenAbilities player;
        public ChampionAbilities abilities;
        public ChampionVRAvatar avatar;
        public ChampionDefinition Active {get;private set;}
        public ChampionDefinition Selected => champions!=null&&champions.Length>0?champions[Mathf.Clamp(selected,0,champions.Length-1)]:null;
        GwenAvatar gwen; Vector3 gwenScale;
        void Awake(){if(!player)player=GetComponent<GwenAbilities>();gwen=GetComponent<GwenAvatar>();if(gwen&&gwen.visualRoot)gwenScale=gwen.visualRoot.localScale;Active=champions[0];}
        public void Select(int index){selected=Mathf.Clamp(index,0,champions.Length-1);}
        public void ApplySelection()
        {
            Active=Selected;player.ResetPractice();abilities.SetChampion(Active);
            bool isGwen=Active.id==ChampionId.Gwen;
            if(gwen){gwen.enabled=isGwen;if(gwen.visualRoot)gwen.visualRoot.gameObject.SetActive(isGwen);if(gwen.scissorsRoot)gwen.scissorsRoot.gameObject.SetActive(isGwen);}
            avatar.SetChampion(isGwen?null:Active);
            player.GetComponent<RiftEconomy>()?.SetChampionBase(Active);
            player.Health.RefreshGuards();
        }
    }
}
