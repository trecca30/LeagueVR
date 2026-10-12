using LeagueVR.Match;
using UnityEngine;
using UnityEngine.XR;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Watching yourself in the brush. Standing in brush offers a view from outside (Y, or T on desktop): behind a short
    /// fade the view moves to a still spot behind and above you, and your champion's full model stands where you are.
    /// The stick walks the champion while the view stays put (no camera motion, so it stays comfortable). Step out of
    /// the brush, press Y again, attack or cast, and the view fades back into your body where the champion stands.
    /// While watching, the champion is where the model is: hidden by the brush, and unable to attack.
    /// </summary>
    [RequireComponent(typeof(PlayerChampion))]
    public class BrushView : MonoBehaviour
    {
        const float Back = 3.4f, Up = 2.1f;
        static readonly Color Fade = new(.08f, .2f, .14f);

        PlayerChampion player;
        ChampionPuppet puppet;
        Vector3 champion;
        Quaternion facing;
        bool wasInBrush, leaving;
        float enteredAt;

        public bool Active { get; private set; }

        /// <summary>Standing in brush with the view still inside the body: Y switches to the outside view.</summary>
        public bool Offered => !Active && InBrush(player.Feet) && Usable;

        bool Usable => player.Health && player.Health.IsAlive && player.CanAct && player.Definition && player.Definition.model
            && !(player.Kit is ZeriKit zeri && zeri.Vaulting) && !(player.Body && player.Body.Hidden);

        static bool InBrush(Vector3 at) => RiftNavGrid.Active && RiftNavGrid.Active.BrushAt(at) != 0;

        void Awake() => player = GetComponent<PlayerChampion>();

        void OnDisable()
        {
            if (Active)
                Finish(false);
        }

        public void Toggle()
        {
            if (Active)
                Leave();
            else if (Offered)
                Enter();
        }

        void Update()
        {
            if (!Active)
            {
                bool inBrush = InBrush(player.Feet) && Usable;
                if (inBrush && !wasInBrush)
                    RiftMatch.Instance?.Notify(player.DesktopMode ? "In the brush: hidden from enemies. T: watch from outside." : "In the brush: hidden from enemies. Y: watch from outside.");
                wasInBrush = inBrush;
                if (player.DesktopMode && UnityEngine.InputSystem.Keyboard.current is { } keys && keys.tKey.wasPressedThisFrame)
                    Toggle();
                return;
            }
            if (leaving)
                return;
            if (!player.Health.IsAlive || player.Kit == null)
            {
                Finish(false);
                return;
            }
            // Attacking or casting reveals you in League: it also brings the view back into the body.
            if (Time.time - enteredAt > .3f && ActionPressed())
            {
                Leave();
                return;
            }
            Walk();
            if (!InBrush(champion))
                Leave();
        }

        bool ActionPressed()
        {
            if (player.DesktopMode)
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                var keys = UnityEngine.InputSystem.Keyboard.current;
                return (mouse != null && mouse.leftButton.wasPressedThisFrame) || (keys != null && (keys.qKey.wasPressedThisFrame || keys.eKey.wasPressedThisFrame || keys.fKey.wasPressedThisFrame || keys.rKey.wasPressedThisFrame));
            }
            var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            return Pressed(right, CommonUsages.triggerButton) || Pressed(right, CommonUsages.primaryButton) || Pressed(right, CommonUsages.secondaryButton)
                || Pressed(left, CommonUsages.primaryButton) || Pressed(left, CommonUsages.triggerButton);
        }

        static bool Pressed(InputDevice device, InputFeatureUsage<bool> usage) => device.isValid && device.TryGetFeatureValue(usage, out bool down) && down;

        /// <summary>The stick walks the champion across the floor (never through walls); the view does not move.</summary>
        void Walk()
        {
            var direction = player.LocomotionDirection(out bool walking);
            if (walking)
            {
                float speed = 2.5f * (player.Economy ? Mathf.Max(.1f, 1 + player.Economy.MoveSpeedBonus) : 1) * player.Health.SlowMultiplier * player.Health.SpeedMultiplier;
                Vector3 step = direction * speed * Time.deltaTime;
                var grid = RiftNavGrid.Active;
                foreach (var tryStep in new[] { step, new Vector3(step.x, 0, 0), new Vector3(0, 0, step.z) })
                {
                    Vector3 next = champion + tryStep;
                    if (grid.Floor(next, out float y))
                    {
                        champion = new Vector3(next.x, y, next.z);
                        break;
                    }
                }
                facing = Quaternion.Slerp(facing, Quaternion.LookRotation(direction), 1 - Mathf.Exp(-10 * Time.deltaTime));
            }
            player.FeetOverride = champion;
            if (puppet)
            {
                puppet.transform.SetPositionAndRotation(champion, facing);
                puppet.Play(true, 0, walking ? new[] { "Run_Base", "Run", "Run1" } : new[] { player.Definition.idle ? player.Definition.idle.name : "Idle_Base", "Idle1_Base", "Idle1", "Idle" });
            }
        }

        void Enter()
        {
            champion = player.Feet;
            facing = Quaternion.LookRotation(player.PlanarDirection(player.head.transform.forward));
            if (!Vantage(champion, out var eye))
                return;
            Active = true;
            leaving = false;
            enteredAt = Time.time;
            player.ScreenFade(Fade, .08f, .06f, .2f, .9f);
            Invoke(nameof(Cut), .08f);
        }

        void Cut()
        {
            if (!Active)
                return;
            Vantage(champion, out var eye);
            player.FreezeLocomotion(true);
            Vector3 eyeAboveFeet = player.head.transform.position - player.Feet;
            player.MoveFeet(eye - eyeAboveFeet);
            player.FeetOverride = champion;
            if (player.Body)
                player.Body.Hidden = true;
            puppet = ChampionPuppet.Spawn(player, champion, facing);
            RiftMatch.Instance?.Notify("Watching from outside. Step out of the brush, attack or press Y to return.");
        }

        /// <summary>A still spot behind and above the champion with a clear line of sight; tries around if a wall is in the way.</summary>
        bool Vantage(Vector3 feet, out Vector3 eye)
        {
            Vector3 back = -player.PlanarDirection(player.head.transform.forward);
            foreach (float turn in new[] { 0f, 40, -40, 80, -80, 130, -130, 180 })
            {
                eye = feet + Quaternion.Euler(0, turn, 0) * back * Back + Vector3.up * (Up + 1.1f);
                Vector3 chest = feet + Vector3.up * 1.1f;
                if (!Physics.Linecast(chest, eye, player.worldMask, QueryTriggerInteraction.Ignore))
                    return true;
            }
            eye = feet + Vector3.up * (Up + 1.1f);
            return true;
        }

        void Leave()
        {
            if (leaving)
                return;
            leaving = true;
            player.ScreenFade(Fade, .08f, .06f, .2f, .9f);
            Invoke(nameof(Return), .08f);
        }

        void Return() => Finish(true);

        void Finish(bool moveBody)
        {
            CancelInvoke();
            player.FeetOverride = null;
            if (moveBody && player.Health && player.Health.IsAlive)
            {
                player.MoveFeet(champion);
                RiftMatch.Instance?.Notify("Back in your body.");
            }
            if (puppet)
                Destroy(puppet.gameObject);
            puppet = null;
            if (player.Body)
                player.Body.Hidden = false;
            player.FreezeLocomotion(false);
            Active = leaving = false;
            wasInBrush = InBrush(player.Feet);
        }
    }
}
