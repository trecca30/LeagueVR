using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using TMPro;

namespace LeagueVR.Match
{
    [DefaultExecutionOrder(50)]
    public class RiftItemRack : MonoBehaviour
    {
        static int usedFrame = -1;
        public static float LastUseTime { get; private set; } = -100f;
        public static bool InputConsumedThisFrame => usedFrame == Time.frameCount;
        public static bool Holding => RiftMatch.Instance && RiftMatch.Instance.economy && RiftMatch.Instance.economy.GetComponent<RiftItemRack>() is RiftItemRack rack && rack.IsHolding;

        public bool OwnsCollider(Collider collider)
        {
            if (!collider)
                return false;
            foreach (var slot in slots)
                if (slot.model && collider.transform.IsChildOf(slot.model.transform))
                    return true;
            return false;
        }

        public bool IsHandHolding(int hand) => hand >= 0 && hand < 2 && held[hand] != null;
        public bool IsHolding => held[0] != null || held[1] != null;
        public RiftEconomy economy;
        public string[] SlotNames = { "Left hip", "Right hip", "Left shoulder", "Right shoulder", "Left back", "Right back", "Back trinket" };
        readonly Vector3[] offsets = { new(-.3f, -.72f, 0), new(.3f, -.72f, 0), new(-.23f, -.23f, -.22f), new(.23f, -.23f, -.22f), new(-.20f, -.54f, -.23f), new(.20f, -.54f, -.23f), new(0, -.40f, -.34f) };

        class Slot
        {
            public int id, index;
            public Transform anchor;
            public GameObject model;
            public TMP_Text label;
        }
        readonly List<Slot> slots = new();
        readonly Slot[] held = new Slot[2];
        readonly float[] mouthTime = new float[2];
        readonly InputAction[] grips = new InputAction[2], triggers = new InputAction[2];
        Transform harness;
        string signature = "";
        GwenAvatar avatar;
        bool scissorsWereActive;

        public void Initialize(RiftEconomy owner)
        {
            economy = owner;
            Setup();
        }

        void Setup()
        {
            if (harness)
                return;
            LastUseTime = -100f;
            harness = new GameObject("Champion physical item harness").transform;
            avatar = economy.match.player.GetComponent<GwenAvatar>();
            for (int h = 0; h < 2; h++)
            {
                string hand = h == 0 ? "LeftHand" : "RightHand";
                grips[h] = new InputAction("Item grip " + hand, binding: "<XRController>{" + hand + "}/gripPressed");
                triggers[h] = new InputAction("Item use " + hand, binding: "<XRController>{" + hand + "}/triggerPressed");
                grips[h].Enable();
                triggers[h].Enable();
            }
            for (int i = 0; i < 7; i++)
            {
                var anchor = new GameObject(SlotNames[i]).transform;
                anchor.SetParent(harness, false);
                anchor.localPosition = offsets[i];
                slots.Add(new Slot { anchor = anchor, index = i });
            }
            UpdateHarness(true);
            Sync();
        }

        void OnEnable()
        {
            Application.onBeforeRender += PoseHeld;
        }

        void OnDisable()
        {
            Application.onBeforeRender -= PoseHeld;
        }

        void OnDestroy()
        {
            for (int h = 0; h < 2; h++)
            {
                grips[h]?.Dispose();
                triggers[h]?.Dispose();
            }
            for (int h = 0; h < 2; h++)
                ReturnHeld(h);
            if (harness)
                Destroy(harness.gameObject);
            if (avatar && avatar.scissorsRoot)
                avatar.scissorsRoot.gameObject.SetActive(avatar.enabled && (scissorsWereActive || !IsHolding));
        }

        Transform Hand(int h) => h == 0 ? economy.match.player.leftHand : economy.match.player.rightHand;

        void UpdateHarness(bool immediate = false)
        {
            var head = economy.match.player.head.transform;
            Vector3 front = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (front.sqrMagnitude < .1f)
                front = harness.forward;
            harness.position = head.position;
            Quaternion yaw = Quaternion.LookRotation(front);
            harness.rotation = immediate ? yaw : Quaternion.Slerp(harness.rotation, yaw, 1 - Mathf.Exp(-4 * Time.deltaTime));
        }

        void Sync()
        {
            string next = string.Join(",", economy.inventory.ConvertAll(s => s.id.ToString())) + "|" + economy.Trinket;
            if (signature == next)
                return;
            signature = next;
            for (int i = 0; i < 7; i++)
            {
                int id = i == 6 ? economy.Trinket : i < economy.inventory.Count ? economy.inventory[i].id : 0;
                var item = economy.match.catalog.Find(id);
                var slot = slots[i];
                if (slot.id == id)
                    continue;
                for (int h = 0; h < 2; h++)
                    if (held[h] == slot)
                        ReturnHeld(h);
                if (slot.model)
                    Destroy(slot.model);
                slot.id = id;
                if (item == null || item.active == ItemActive.None || !item.physicalPrefab)
                {
                    slot.model = null;
                    continue;
                }
                slot.model = Instantiate(item.physicalPrefab, slot.anchor);
                slot.model.name = item.name + " [" + SlotNames[i] + "]";
                slot.model.transform.localPosition = Vector3.zero;
                slot.model.transform.localRotation = Quaternion.identity;
                var caption = new GameObject("Physical item label");
                caption.transform.SetParent(slot.model.transform, false);
                caption.transform.localPosition = new Vector3(0, .25f, 0);
                slot.label = caption.AddComponent<TextMeshPro>();
                slot.label.fontSize = 1.1f;
                slot.label.alignment = TextAlignmentOptions.Center;
                slot.label.rectTransform.sizeDelta = new Vector2(1.5f, .5f);
                slot.label.transform.localScale = Vector3.one * .12f;
            }
        }

        void Update()
        {
            if (!economy)
                return;
            if (!harness)
                Setup();
            UpdateHarness();
            Sync();
            for (int h = 0; h < 2; h++)
            {
                if (!economy.match.Running || !economy.match.player.Health.IsAlive)
                {
                    ReturnHeld(h);
                    continue;
                }
                if (grips[h].WasPressedThisFrame() && !economy.match.ui.IsOpen)
                    TryGrab(h);
                if (held[h] != null && !grips[h].IsPressed() && !economy.match.player.DesktopMode)
                    ReturnHeld(h);
                if (held[h] != null && triggers[h].WasPressedThisFrame())
                    UseHeld(h);
                if (held[h] != null)
                {
                    var item = economy.match.catalog.Find(held[h].id);
                    bool drink = item.active == ItemActive.Potion || item.active == ItemActive.Refillable || item.active == ItemActive.ElixirIron || item.active == ItemActive.ElixirSorcery || item.active == ItemActive.ElixirWrath;
                    Vector3 mouth = economy.match.player.head.transform.position - Vector3.up * .09f;
                    mouthTime[h] = drink && CanSip(held[h].model, economy.match.player.head.transform) ? mouthTime[h] + Time.deltaTime : 0;
                    if (mouthTime[h] >= .15f)
                    {
                        mouthTime[h] = 0;
                        UseHeld(h);
                    }
                }
            }
            if (economy.match.player.DesktopMode && Keyboard.current != null && !economy.match.ui.IsOpen)
            {
                var keys = new[] { Keyboard.current.digit1Key, Keyboard.current.digit2Key, Keyboard.current.digit3Key, Keyboard.current.digit4Key, Keyboard.current.digit5Key, Keyboard.current.digit6Key, Keyboard.current.digit7Key };
                for (int i = 0; i < keys.Length; i++)
                    if (keys[i].wasPressedThisFrame)
                        EquipSlot(i, 1);
                if (IsHolding && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                    UseHeld(1);
                if (Keyboard.current.backspaceKey.wasPressedThisFrame)
                {
                    ReturnHeld(0);
                    ReturnHeld(1);
                }
            }
            foreach (var slot in slots)
            {
                if (!slot.model || !slot.label)
                    continue;
                float cooldown = economy.Effects.Cooldown(slot.id);
                string charges = economy.match.catalog.Find(slot.id).active == ItemActive.Refillable || slot.index == 6 || economy.match.catalog.Find(slot.id).active == ItemActive.SupportWard ? " · " + economy.Effects.Charges(slot.id) + " charges" : "";
                slot.label.text = economy.match.catalog.Find(slot.id).name + "\n" + (cooldown > 0 ? Mathf.CeilToInt(cooldown) + "s" : "READY") + charges;
                slot.label.transform.rotation = Quaternion.LookRotation(slot.label.transform.position - economy.match.player.head.transform.position);
                bool isHeld = held[0] == slot || held[1] == slot;
                slot.label.gameObject.SetActive(isHeld || Vector3.Distance(Hand(0).position, slot.anchor.position) < .3f || Vector3.Distance(Hand(1).position, slot.anchor.position) < .3f);
            }
        }

        void LateUpdate() => PoseHeld();

        [BeforeRenderOrder(180)]
        void PoseHeld()
        {
            for (int h = 0; h < 2; h++)
                if (held[h]?.model)
                {
                    Transform hand = Hand(h);
                    if (economy.match.player.DesktopMode)
                        hand = economy.match.player.head.transform;
                    held[h].model.transform.SetPositionAndRotation(hand.TransformPoint(economy.match.player.DesktopMode ? new Vector3(.3f, -.25f, .6f) : new Vector3(0, -.025f, .035f)), hand.rotation);
                }
        }

        public bool TryGrab(int hand)
        {
            Slot closest = null;
            float distance = .32f;
            foreach (var slot in slots)
            {
                if (!slot.model || held[1 - hand] == slot)
                    continue;
                float d = Vector3.Distance(Hand(hand).position, slot.anchor.position);
                if (d < distance)
                {
                    distance = d;
                    closest = slot;
                }
            }
            return closest != null && EquipSlot(closest.index, hand);
        }

        public Vector3 SlotPosition(int index) => slots[index].anchor.position;

        public int HeldId(int hand) => held[hand]?.id ?? 0;

        public bool EquipSlot(int index, int hand)
        {
            if (index < 0 || index >= slots.Count || !slots[index].model || economy.Stasis || !economy.match.Running || !economy.match.player.Health.IsAlive)
                return false;
            if (held[1 - hand] == slots[index])
                return false;
            ReturnHeld(hand);
            held[hand] = slots[index];
            held[hand].model.transform.SetParent(null, true);
            mouthTime[hand] = 0;
            Haptic(hand, .3f);
            if (hand == 1 && avatar && avatar.scissorsRoot)
            {
                scissorsWereActive = avatar.scissorsRoot.gameObject.activeSelf;
                avatar.scissorsRoot.gameObject.SetActive(false);
            }
            economy.match.Notify("Holding " + economy.match.catalog.Find(held[hand].id).name + " · trigger to use · release grip to holster");
            return true;
        }

        public bool UseHeld(int hand)
        {
            if (held[hand] == null)
                return false;
            usedFrame = Time.frameCount;
            LastUseTime = Time.time;
            economy.match.CancelRecall();
            int id = held[hand].id;
            Transform aim = economy.match.player.DesktopMode ? economy.match.player.head.transform : Hand(hand);
            bool success = economy.Effects.Activate(id, aim.position, aim.forward);
            if (success)
            {
                Haptic(hand, .5f);
                if (!economy.Owns(id) && economy.Trinket != id)
                    ReturnHeld(hand);
            }
            return success;
        }

        public void ReturnHeld(int hand)
        {
            var slot = held[hand];
            if (slot == null)
                return;
            if (slot.model)
            {
                slot.model.transform.SetParent(slot.anchor, false);
                slot.model.transform.localPosition = Vector3.zero;
                slot.model.transform.localRotation = Quaternion.identity;
            }
            held[hand] = null;
            mouthTime[hand] = 0;
            if (hand == 1 && avatar && avatar.scissorsRoot)
                avatar.scissorsRoot.gameObject.SetActive(scissorsWereActive && avatar.enabled);
        }

        public static bool CanSip(GameObject model, Transform head)
        {
            var tip = model.transform.Find("Drink tip");
            return tip && Vector3.Distance(tip.position, head.position - Vector3.up * .09f) < .15f && Vector3.Dot(model.transform.up, head.up) < .45f;
        }

        Vector3 DrinkPoint(GameObject model)
        {
            var tip = model.transform.Find("Drink tip");
            return tip ? tip.position : model.transform.TransformPoint(Vector3.up * .32f);
        }

        void Haptic(int hand, float strength)
        {
            var device = InputDevices.GetDeviceAtXRNode(hand == 0 ? XRNode.LeftHand : XRNode.RightHand);
            if (device.isValid)
                device.SendHapticImpulse(0, strength, .07f);
        }
    }
}
