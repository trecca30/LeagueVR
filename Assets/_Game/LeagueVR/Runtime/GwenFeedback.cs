using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR;
namespace LeagueVR
{
    public class GwenFeedback : MonoBehaviour
    {
        public GwenAbilities champion;
        public Material cyanMaterial, roseMaterial;
        public TMP_Text status;
        public Transform wristDisplay;
        LineRenderer mist;
        readonly List<LineRenderer> mistWalls=new List<LineRenderer>();
        float hudNext;
        void OnEnable() { champion.Cast+=Effect; champion.Health.Damaged+=Hurt; }
        void OnDisable() { champion.Cast-=Effect; champion.Health.Damaged-=Hurt; }
        void Start()
        {
            mist=Line("Hallowed Mist boundary",cyanMaterial,.035f,81);
            for (int i=0;i<12;i++) mistWalls.Add(Line("Mist thread",cyanMaterial,.015f,3));
        }
        void Update()
        {
            if (!mist) return;
            mist.gameObject.SetActive(champion.MistActive);
            foreach(var wall in mistWalls) wall.gameObject.SetActive(champion.MistActive);
            if (champion.MistActive)
            {
                Vector3 center=champion.MistCenter+Vector3.up*.06f;
                for(int i=0;i<81;i++) { float a=i*2*Mathf.PI/80; mist.SetPosition(i,center+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*champion.tuning.wRadius); }
                for(int i=0;i<mistWalls.Count;i++) { float a=i*2*Mathf.PI/mistWalls.Count+Time.time*.1f; Vector3 pos=center+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*champion.tuning.wRadius; mistWalls[i].SetPosition(0,pos); mistWalls[i].SetPosition(1,pos+Vector3.up*.6f); mistWalls[i].SetPosition(2,pos+Vector3.up*(1.1f+.25f*Mathf.Sin(Time.time+i))); }
            }
            if (wristDisplay)
            {
                wristDisplay.position=champion.DesktopMode ? champion.head.transform.TransformPoint(new Vector3(-.26f,-.2f,.75f)) : champion.leftHand.position+Vector3.up*.1f;
                wristDisplay.rotation=Quaternion.LookRotation(wristDisplay.position-champion.head.transform.position,champion.head.transform.up);
            }
            if (Time.time<hudNext || !status) return; hudNext=Time.time+.08f;
            string Ready(string key) => champion.Cooldown(key)<=0?"READY":champion.Cooldown(key).ToString("0.0")+"s";
            status.text=$"<color=#82F3F4>GWEN</color>  {champion.Health.Health:0}/{champion.Health.maxHealth:0}\nQ  {Ready("Q")}  <color=#F3C782>{champion.QStacks}/4</color>\nW  {(champion.MistActive?champion.MistRemaining.ToString("0.0")+"s MIST":Ready("W"))}\nE  {Ready("E")}{(champion.Empowered?"  EMPOWERED":"")}\nR  {(champion.RStage>0?"RECAST "+(champion.RStage+1):Ready("R"))}\n<color=#A2AAB8>Defeated {champion.Defeated}</color>";
            if(!champion.Health.IsAlive) status.text="<color=#EF81B7>Returning to the Rift...</color>";
        }
        void Effect(string ability,Vector3 start,Vector3 dir)
        {
            if(champion.OtherActive){if(ability!="Death")Haptic(XRNode.RightHand,.18f,.08f);return;}
            if (ability=="Snip" || ability=="Attack1")
            {
                Vector3 side=Vector3.Cross(dir,Vector3.up).normalized;
                var cut=Line("Snip trail",cyanMaterial,ability=="Snip"?.035f:.015f,3);
                float range=ability=="Snip"?champion.tuning.qRange:champion.tuning.attackReach;
                cut.SetPosition(0,start+side*.35f); cut.SetPosition(1,start+dir*range); cut.SetPosition(2,start-side*.35f); Destroy(cut.gameObject,.13f);
            }
            if (ability=="Hit" || ability=="E")
            {
                var spark=Line("Thread burst",cyanMaterial,.035f,2); spark.SetPosition(0,start-Vector3.up*.2f); spark.SetPosition(1,start+Vector3.up*.4f); Destroy(spark.gameObject,.18f);
            }
            if (ability!="Death") Haptic(XRNode.RightHand,ability=="Hit"?.5f:.18f,.08f);
        }
        void Hurt(DamageHit hit,float damage) { if(damage>0)Haptic(XRNode.LeftHand,.4f,.12f); }
        static void Haptic(XRNode node,float amplitude,float duration) { var d=UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node); if(d.isValid) d.SendHapticImpulse(0,amplitude,duration); }
        LineRenderer Line(string name,Material mat,float width,int count)
        {
            var go=new GameObject(name); go.transform.SetParent(transform); var line=go.AddComponent<LineRenderer>();
            line.sharedMaterial=mat; line.startWidth=line.endWidth=width; line.positionCount=count; line.useWorldSpace=true;
            line.numCapVertices=3; line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; return line;
        }
    }
}
