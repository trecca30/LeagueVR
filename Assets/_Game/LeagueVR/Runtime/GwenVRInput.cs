using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace LeagueVR
{
    [DefaultExecutionOrder(100)]
    public class GwenVRInput : MonoBehaviour
    {
        public GwenAbilities champion;
        public InputActionAsset combatActions;
        public bool enableDesktopFallback = true;
        public float swingSpeed = 1.3f;
        InputAction attack, snip, mist, dash, needle, grip;
        Vector3 previousHand;
        bool swingArmed = true;
        float yaw, pitch;
        bool wasDesktop;
        bool xrSessionSeen,havePreviousPose;
        Transform[] teleportRays;
        public bool IsDesktop => champion.DesktopMode;
        void OnEnable()
        {
            if (!combatActions) return;
            attack=combatActions.FindAction("Combat/Attack",true); snip=combatActions.FindAction("Combat/Snip",true);
            mist=combatActions.FindAction("Combat/Mist",true); dash=combatActions.FindAction("Combat/Dash",true);
            needle=combatActions.FindAction("Combat/Needle",true); grip=combatActions.FindAction("Combat/Grip",true);
            combatActions.Enable(); ResetSwing();
            teleportRays=System.Array.FindAll(champion.origin.GetComponentsInChildren<Transform>(true),t=>t.name=="Teleport Interactor");
        }
        void OnDisable() { if (combatActions) combatActions.Disable(); }
        void Update()
        {
            var hmd=UnityEngine.XR.InputDevices.GetDeviceAtXRNode(XRNode.Head);
            // A tracking interruption must never hand camera control to the mouse.
            if(hmd.isValid||XRSettings.isDeviceActive)xrSessionSeen=true;
            champion.DesktopMode=enableDesktopFallback && !xrSessionSeen && !hmd.isValid && !XRSettings.isDeviceActive;
            var match=LeagueVR.Match.RiftMatch.Instance;
            if ((match&&(!match.Running||match.ui.IsOpen||match.economy.Stasis))||LeagueVR.Match.RiftItemRack.InputConsumedThisFrame) { ResetSwing(); return; }
            if (champion.DesktopMode) { DesktopInput(); return; }
            if (LeagueVR.Match.RiftItemRack.Holding||!GwenTracking.Tracked(false)||!GwenTracking.Tracked(true)) { ResetSwing(); return; }
            wasDesktop=false;
            if(teleportRays!=null && System.Array.Exists(teleportRays,t=>t && t.gameObject.activeInHierarchy))
            {ResetSwing();return;}
            if (attack != null && attack.IsPressed()) champion.BasicAttack();
            if (snip != null && snip.WasPressedThisFrame()) champion.CastQ();
            if (mist != null && mist.WasPressedThisFrame()) champion.CastW();
            if (dash != null && dash.WasPressedThisFrame()) champion.CastE();
            if (needle != null && needle.WasPressedThisFrame()) champion.CastR();
            Vector3 localHand=champion.origin.transform.InverseTransformPoint(GwenTracking.Grip(champion,false).position);
            float velocity=havePreviousPose?RelativeSwingSpeed(previousHand,localHand,Time.deltaTime):0;
            previousHand=localHand;havePreviousPose=true;
            // A grip-gated swing needs a slow reset between cuts, preventing stationary multi-hits.
            if (velocity < .45f) swingArmed=true;
            if (grip != null && grip.IsPressed() && swingArmed && velocity > swingSpeed && velocity < 12)
            { if (champion.BasicAttack()) swingArmed=false; }
        }
        void ResetSwing(){havePreviousPose=false;swingArmed=false;previousHand=Vector3.zero;}
        // Tracking-space velocity excludes movement of the rig, including snap turns and E.
        public static float RelativeSwingSpeed(Vector3 previous,Vector3 current,float deltaTime)=>deltaTime<=0||deltaTime>.1f?0:(current-previous).magnitude/Mathf.Max(deltaTime,.001f);
        void DesktopInput()
        {
            if (!wasDesktop) { yaw=champion.head.transform.localEulerAngles.y; pitch=0; wasDesktop=true; }
            var k=Keyboard.current; var m=Mouse.current;
            if (k==null || !champion.Health.IsAlive) return;
            if (m!=null && m.leftButton.isPressed && !LeagueVR.Match.RiftItemRack.Holding) champion.BasicAttack();
            if (k.qKey.wasPressedThisFrame) champion.CastQ();
            if (k.fKey.wasPressedThisFrame) champion.CastW();
            if (k.eKey.wasPressedThisFrame) champion.CastE();
            if (k.rKey.wasPressedThisFrame) champion.CastR();
            if (m!=null && m.rightButton.isPressed) { var delta=m.delta.ReadValue(); yaw+=delta.x*.12f; pitch=Mathf.Clamp(pitch-delta.y*.12f,-65,65); }
            if (k.zKey.wasPressedThisFrame) champion.origin.RotateAroundCameraUsingOriginUp(-30);
            if (k.cKey.wasPressedThisFrame) champion.origin.RotateAroundCameraUsingOriginUp(30);
            Vector3 forward=Vector3.ProjectOnPlane(champion.head.transform.forward,Vector3.up).normalized;
            Vector3 right=Vector3.Cross(Vector3.up,forward);
            Vector3 move=forward*((k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0))+right*((k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0));
            var cc=champion.origin.GetComponent<CharacterController>();
            if (cc && cc.enabled && !champion.Health.Rooted) cc.Move(move.normalized*2.5f*(champion.GetComponent<LeagueVR.Match.RiftEconomy>() ? 1+champion.GetComponent<LeagueVR.Match.RiftEconomy>().MoveSpeedBonus : 1)*champion.Health.SlowMultiplier*champion.Health.SpeedMultiplier*Time.deltaTime);
        }
        void LateUpdate()
        {
            if (!champion.DesktopMode) return;
            champion.head.transform.localRotation=Quaternion.Euler(pitch,yaw,0);
        }
    }
}
