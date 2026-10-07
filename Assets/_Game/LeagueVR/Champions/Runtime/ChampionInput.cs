using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.XR;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Maps VR controllers (and a desktop fallback) to the champion's attack and QWER abilities.
    /// Right trigger auto-attacks (swinging a melee weapon through enemies attacks too, see PlayerChampion);
    /// B = Q, X = W, A = E, left trigger = R. Buttons report press and release, so abilities can be held and thrown.
    /// Each hand is independent: tracking loss, an aimed teleport or a held item on one hand never blocks the other.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class ChampionInput : MonoBehaviour
    {
        [FormerlySerializedAs("champion")] public PlayerChampion player;
        public InputActionAsset combatActions;
        public bool enableDesktopFallback = true;

        InputAction attack, q, w, e, r;
        bool xrSessionSeen, wasDesktop;
        float yaw, pitch;
        GameObject leftTeleport, rightTeleport;
        RiftItemRack rack;

        public bool IsDesktop => player.DesktopMode;

        void OnEnable()
        {
            if (!combatActions)
                return;
            attack = combatActions.FindAction("Combat/Attack", true);
            q = combatActions.FindAction("Combat/Q", true);
            w = combatActions.FindAction("Combat/W", true);
            e = combatActions.FindAction("Combat/E", true);
            r = combatActions.FindAction("Combat/R", true);
            combatActions.Enable();
            foreach (var t in player.origin.GetComponentsInChildren<Transform>(true))
                if (t.name == "Teleport Interactor" && t.parent)
                {
                    if (t.parent.name.StartsWith("Left"))
                        leftTeleport = t.gameObject;
                    else if (t.parent.name.StartsWith("Right"))
                        rightTeleport = t.gameObject;
                }
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
            if (match && (!match.Running || match.ui.IsOpen || match.economy.Stasis))
                return;
            if (!rack && match && match.economy)
                rack = match.economy.Rack;
            if (player.DesktopMode)
            {
                DesktopInput();
                return;
            }
            wasDesktop = false;

            // A hand that holds an item or aims a teleport keeps its trigger for that; its face buttons still cast.
            bool usedItem = RiftItemRack.InputConsumedThisFrame;
            bool rightOccupied = (rack && rack.IsHandHolding(1)) || (rightTeleport && rightTeleport.activeInHierarchy) || usedItem;
            bool leftOccupied = (rack && rack.IsHandHolding(0)) || (leftTeleport && leftTeleport.activeInHierarchy) || usedItem;

            Slot(q, 0, true);
            Slot(e, 2, true);
            Slot(w, 1, true);
            Slot(r, 3, !leftOccupied);
            if (!rightOccupied && attack != null && attack.IsPressed())
                player.BasicAttack();
        }

        /// <summary>Forwards press and release so abilities can be tapped, held to aim or charged, and thrown.</summary>
        void Slot(InputAction action, int slot, bool allowed)
        {
            if (action == null)
                return;
            if (allowed && action.WasPressedThisFrame())
                player.PressSlot(slot);
            if (action.WasReleasedThisFrame() || (!allowed && player.IsHolding(slot)))
                player.ReleaseSlot(slot);
        }

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
            if (m != null && m.leftButton.isPressed && !RiftItemRack.Holding && !RiftItemRack.InputConsumedThisFrame)
                player.BasicAttack();
            Key(k.qKey, 0);
            Key(k.fKey, 1);
            Key(k.eKey, 2);
            Key(k.rKey, 3);
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

        void Key(UnityEngine.InputSystem.Controls.KeyControl key, int slot)
        {
            if (key.wasPressedThisFrame)
                player.PressSlot(slot);
            if (key.wasReleasedThisFrame)
                player.ReleaseSlot(slot);
        }

        void LateUpdate()
        {
            if (player.DesktopMode)
                player.head.transform.localRotation = Quaternion.Euler(pitch, yaw, 0);
        }
    }
}
