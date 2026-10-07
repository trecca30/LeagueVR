using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.XR;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Maps VR controllers (and a desktop fallback) to the champion's attack and QWER abilities.
    /// Right trigger or a grip-held swing attacks; B = Q, X = W, A = E, left trigger = R.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class ChampionInput : MonoBehaviour
    {
        [FormerlySerializedAs("champion")] public PlayerChampion player;
        public InputActionAsset combatActions;
        public bool enableDesktopFallback = true;
        [Tooltip("Hand speed (m/s, tracking space) that turns a grip-held swing into an attack.")]
        public float swingSpeed = 1.3f;

        InputAction attack, q, w, e, r, grip;
        Vector3 previousHand;
        bool swingArmed = true, xrSessionSeen, havePreviousPose, wasDesktop;
        float yaw, pitch;
        Transform[] teleportRays;

        public bool IsDesktop => player.DesktopMode;

        void OnEnable()
        {
            if (!combatActions)
                return;
            attack = combatActions.FindAction("Combat/Attack", true);
            grip = combatActions.FindAction("Combat/Grip", true);
            q = combatActions.FindAction("Combat/Q", true);
            w = combatActions.FindAction("Combat/W", true);
            e = combatActions.FindAction("Combat/E", true);
            r = combatActions.FindAction("Combat/R", true);
            combatActions.Enable();
            ResetSwing();
            teleportRays = System.Array.FindAll(player.origin.GetComponentsInChildren<Transform>(true), t => t.name == "Teleport Interactor");
        }

        void OnDisable()
        {
            if (combatActions)
                combatActions.Disable();
        }

        void Update()
        {
            var hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            // A tracking interruption must never hand camera control to the mouse.
            if (hmd.isValid || XRSettings.isDeviceActive)
                xrSessionSeen = true;
            player.DesktopMode = enableDesktopFallback && !xrSessionSeen && !hmd.isValid && !XRSettings.isDeviceActive;
            var match = RiftMatch.Instance;
            if ((match && (!match.Running || match.ui.IsOpen || match.economy.Stasis)) || RiftItemRack.InputConsumedThisFrame)
            {
                ResetSwing();
                return;
            }
            if (player.DesktopMode)
            {
                DesktopInput();
                return;
            }
            wasDesktop = false;
            if (RiftItemRack.Holding || !XRPoses.Tracked(false) || !XRPoses.Tracked(true) || TeleportAiming())
            {
                ResetSwing();
                return;
            }
            if (attack != null && attack.IsPressed())
                player.BasicAttack();
            if (q != null && q.WasPressedThisFrame())
                player.CastQ();
            if (w != null && w.WasPressedThisFrame())
                player.CastW();
            if (e != null && e.WasPressedThisFrame())
                player.CastE();
            if (r != null && r.WasPressedThisFrame())
                player.CastR();
            UpdateSwing();
        }

        bool TeleportAiming()
        {
            if (teleportRays == null)
                return false;
            foreach (var t in teleportRays)
                if (t && t.gameObject.activeInHierarchy)
                    return true;
            return false;
        }

        void UpdateSwing()
        {
            Vector3 localHand = player.origin.transform.InverseTransformPoint(XRPoses.Grip(player, false).position);
            float velocity = havePreviousPose ? RelativeSwingSpeed(previousHand, localHand, Time.deltaTime) : 0;
            previousHand = localHand;
            havePreviousPose = true;
            // A grip-held swing needs a slow reset between cuts, preventing stationary multi-hits.
            if (velocity < .45f)
                swingArmed = true;
            if (grip != null && grip.IsPressed() && swingArmed && velocity > swingSpeed && velocity < 12 && player.BasicAttack())
                swingArmed = false;
        }

        void ResetSwing()
        {
            havePreviousPose = false;
            swingArmed = false;
            previousHand = Vector3.zero;
        }

        /// <summary>Tracking-space speed excludes movement of the rig itself (snap turns, dashes, locomotion).</summary>
        public static float RelativeSwingSpeed(Vector3 previous, Vector3 current, float deltaTime) => deltaTime <= 0 || deltaTime > .1f ? 0 : (current - previous).magnitude / Mathf.Max(deltaTime, .001f);

        void DesktopInput()
        {
            if (!wasDesktop)
            {
                yaw = player.head.transform.localEulerAngles.y;
                pitch = 0;
                wasDesktop = true;
            }
            var k = Keyboard.current;
            var m = Mouse.current;
            if (k == null || !player.Health.IsAlive)
                return;
            if (m != null && m.leftButton.isPressed && !RiftItemRack.Holding)
                player.BasicAttack();
            if (k.qKey.wasPressedThisFrame)
                player.CastQ();
            if (k.fKey.wasPressedThisFrame)
                player.CastW();
            if (k.eKey.wasPressedThisFrame)
                player.CastE();
            if (k.rKey.wasPressedThisFrame)
                player.CastR();
            if (m != null && m.rightButton.isPressed)
            {
                var delta = m.delta.ReadValue();
                yaw += delta.x * .12f;
                pitch = Mathf.Clamp(pitch - delta.y * .12f, -65, 65);
            }
            if (k.zKey.wasPressedThisFrame)
                player.origin.RotateAroundCameraUsingOriginUp(-30);
            if (k.cKey.wasPressedThisFrame)
                player.origin.RotateAroundCameraUsingOriginUp(30);
            Vector3 forward = Vector3.ProjectOnPlane(player.head.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 move = forward * ((k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0)) + right * ((k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0));
            var cc = player.origin.GetComponent<CharacterController>();
            float speedBonus = player.Economy ? 1 + player.Economy.MoveSpeedBonus : 1;
            if (cc && cc.enabled && !player.Health.Rooted)
                cc.Move(move.normalized * 2.5f * speedBonus * player.Health.SlowMultiplier * player.Health.SpeedMultiplier * Time.deltaTime);
        }

        void LateUpdate()
        {
            if (player.DesktopMode)
                player.head.transform.localRotation = Quaternion.Euler(pitch, yaw, 0);
        }
    }
}
