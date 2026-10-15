using UnityEngine;
using UnityEngine.InputSystem;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Finger poses from Meta's Touch controllers (Quest Touch, Touch Plus, Touch Pro), the way Meta's own hands work
    /// ("Capsense"): the controllers' touch sensors tell where each finger is. An index finger lifted off the trigger
    /// points, a thumb lifted off the buttons, stick and thumbrest stands up, and the trigger and grip close the fingers
    /// as far as they are pulled. Touch Pro controllers also sense how far a lifted index finger is curled.
    /// Sensors are bound by usage (as XRI binds them), so any controller that reports them works; a sensor a controller
    /// lacks reads as "resting on the controller", never as pointing or thumbs-up. The result is the same for every
    /// champion: <see cref="BodyRig"/> turns it into each skeleton's own finger bends.
    /// </summary>
    public static class FingerSense
    {
        /// <summary>A fixed pose per hand (tests); null reads the controller.</summary>
        public static HandCurl? OverrideLeft, OverrideRight;

        // Where each sensor puts a finger. A finger resting on the trigger is already a little closed; a thumb resting on
        // the face buttons lies across the controller, and presses it a little further.
        const float IndexResting = .32f, ThumbResting = .5f, ThumbPressing = .7f;
        // With the grip released the three lower fingers stay loosely open, the little finger the most closed (they
        // cascade like a real hand's); pulling the grip closes them into a fist.
        const float MiddleOpen = .1f, RingOpen = .14f, PinkyOpen = .18f;
        /// <summary>How fast fingers follow their sensors (per second): quick, but a touch never snaps.</summary>
        const float Follow = 20;

        class Hand
        {
            public readonly InputAction trigger, grip, triggerTouch, triggerCurl, primaryTouch, secondaryTouch, stickTouch, thumbrest, primaryPress, secondaryPress, stickPress;
            public HandCurl value;
            public int frame = -1;
            public bool started;

            public Hand(string side)
            {
                string device = "<XRController>{" + side + "}/";
                trigger = Axis(device + "{Trigger}");
                grip = Axis(device + "{Grip}");
                // Touch Pro only: how far the index finger is curled toward the trigger before it touches it.
                triggerCurl = Axis(device + "triggerCurl");
                triggerTouch = Button(device + "{TriggerTouch}");
                primaryTouch = Button(device + "{PrimaryTouch}");
                secondaryTouch = Button(device + "{SecondaryTouch}");
                stickTouch = Button(device + "{Primary2DAxisTouch}");
                thumbrest = Button(device + "{ThumbrestTouch}");
                primaryPress = Button(device + "{PrimaryButton}");
                secondaryPress = Button(device + "{SecondaryButton}");
                stickPress = Button(device + "{Primary2DAxisClick}");
            }

            static InputAction Axis(string path)
            {
                var action = new InputAction(type: InputActionType.Value, binding: path, expectedControlType: "Axis");
                action.Enable();
                return action;
            }

            static InputAction Button(string path)
            {
                var action = new InputAction(type: InputActionType.Button, binding: path);
                action.Enable();
                return action;
            }

            static bool Has(InputAction a) => a.controls.Count > 0;

            /// <summary>Where the sensors put each finger right now.</summary>
            public HandCurl Target()
            {
                float pull = trigger.ReadValue<float>(), squeeze = grip.ReadValue<float>();
                bool indexOn = !Has(triggerTouch) || triggerTouch.IsPressed() || pull > .05f;
                float index = indexOn
                    ? Mathf.Lerp(IndexResting, 1, pull)
                    : Has(triggerCurl) ? Mathf.Lerp(0, IndexResting, triggerCurl.ReadValue<float>()) : 0;
                bool thumbSensed = Has(primaryTouch) || Has(secondaryTouch) || Has(stickTouch) || Has(thumbrest);
                bool thumbOn = !thumbSensed || primaryTouch.IsPressed() || secondaryTouch.IsPressed() || stickTouch.IsPressed() || thumbrest.IsPressed();
                bool thumbPress = primaryPress.IsPressed() || secondaryPress.IsPressed() || stickPress.IsPressed();
                float thumb = !thumbOn ? 0 : thumbPress ? ThumbPressing : ThumbResting;
                return new HandCurl(thumb, index, Mathf.Lerp(MiddleOpen, 1, squeeze), Mathf.Lerp(RingOpen, 1, squeeze), Mathf.Lerp(PinkyOpen, 1, squeeze));
            }
        }

        static Hand left, right;

        /// <summary>
        /// The hand's finger closure this frame, eased toward what its controller senses (the body is posed twice a frame;
        /// both get the same value).
        /// </summary>
        public static HandCurl Read(bool leftHand)
        {
            var overridden = leftHand ? OverrideLeft : OverrideRight;
            if (overridden.HasValue)
                return overridden.Value;
            var hand = leftHand ? left ??= new Hand("LeftHand") : right ??= new Hand("RightHand");
            if (hand.frame == Time.frameCount)
                return hand.value;
            var target = hand.Target();
            float t = hand.started ? 1 - Mathf.Exp(-Follow * Time.unscaledDeltaTime) : 1;
            hand.value = Lerp(hand.value, target, t);
            hand.started = true;
            hand.frame = Time.frameCount;
            return hand.value;
        }

        static HandCurl Lerp(HandCurl a, HandCurl b, float t) => new(
            Mathf.Lerp(a.thumb, b.thumb, t), Mathf.Lerp(a.index, b.index, t), Mathf.Lerp(a.middle, b.middle, t),
            Mathf.Lerp(a.ring, b.ring, t), Mathf.Lerp(a.pinky, b.pinky, t));
    }
}
