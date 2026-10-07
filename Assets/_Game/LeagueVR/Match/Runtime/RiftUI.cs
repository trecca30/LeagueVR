using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using LeagueVR.Champions;
namespace LeagueVR.Match
{
    public class RiftUIButton : MonoBehaviour
    {
        public Action action;
        public Image image;
        public TMP_Text label;
        public bool interactable = true, selected;
        public string key;
        /// <summary>Optional colour for the selected state (the shop's gold item frame).</summary>
        public Color? selectedColor;

        public void Hover(bool hover)
        {
            if (image)
                image.color = !interactable ? new Color(.065f, .08f, .095f, .95f) : hover ? new Color(.13f, .35f, .39f, 1) : selected ? selectedColor ?? new Color(.10f, .24f, .28f, 1) : new Color(.045f, .09f, .13f, .98f);
            if (label)
                label.color = interactable ? new Color(.86f, .93f, .94f) : new Color(.46f, .52f, .55f);
        }

        public bool Click()
        {
            if (!interactable)
                return false;
            action?.Invoke();
            return true;
        }
    }

    public partial class RiftUI : MonoBehaviour
    {
        public RiftMatch match;
        public Texture2D gwenPortrait;
        public bool IsOpen => panel;
        public bool ShopIsOpen => IsOpen && shopScreen;
        public string CurrentScreen { get; private set; }
        public IEnumerable<RiftUIButton> Buttons => panel ? panel.GetComponentsInChildren<RiftUIButton>() : Array.Empty<RiftUIButton>();
        /// <summary>Menus, stasis and a stopped match block combat. Held items only occupy their own hand (see ChampionInput).</summary>
        public static bool BlocksCombat => RiftMatch.Instance && (!RiftMatch.Instance.Running || RiftMatch.Instance.ui.IsOpen || RiftMatch.Instance.economy.Stasis);
        GameObject panel;
        RectTransform root;
        TMP_Text status;
        RiftUIButton hovered;
        LineRenderer ray;
        InputAction shop, menu, trigger, recall;
        bool shopScreen;
        string notice = "";
        float noticeUntil;
        bool weaponHidden;
        Vector3 lastPosition;
        Quaternion lastRotation;
        bool preservePose;
        readonly Color gold = new(.90f, .77f, .46f), white = new(.85f, .92f, .94f), muted = new(.55f, .68f, .72f), cyan = new(.30f, .90f, .91f);

        void Awake()
        {
            shop = new InputAction("Shop", binding: "<XRController>{LeftHand}/secondaryButton");
            menu = new InputAction("Menu", binding: "<XRController>{RightHand}/primary2DAxisClick");
            trigger = new InputAction("Select", binding: "<XRController>{RightHand}/triggerPressed");
            recall = new InputAction("Recall", binding: "<XRController>{LeftHand}/primary2DAxisClick");
            shop.Enable();
            menu.Enable();
            trigger.Enable();
            recall.Enable();
        }

        void Start()
        {
            match.Announcement += Announce;
            var go = new GameObject("VR menu pointer");
            ray = go.AddComponent<LineRenderer>();
            ray.sharedMaterial = match.blueMaterial;
            ray.startWidth = .003f;
            ray.endWidth = .0015f;
            ray.positionCount = 2;
            ray.enabled = false;
            if (!GetComponent<RiftVRHUD>())
                gameObject.AddComponent<RiftVRHUD>().match = match;
            ComfortSettings.ApplyVignette();
        }

        void Announce(string value)
        {
            notice = value;
            noticeUntil = Time.time + 4;
        }
        public string Notice => Time.time < noticeUntil ? notice : "";

        void OnDestroy()
        {
            if (match)
                match.Announcement -= Announce;
            shop?.Dispose();
            menu?.Dispose();
            trigger?.Dispose();
            recall?.Dispose();
            Close();
            if (ray)
                Destroy(ray.gameObject);
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            bool desktop = match.player.DesktopMode;
            if (shop.WasPressedThisFrame() || (desktop && keyboard != null && keyboard.pKey.wasPressedThisFrame))
            {
                if (ShopIsOpen)
                    Close();
                else
                    OpenShop();
            }
            if (menu.WasPressedThisFrame() || (desktop && keyboard != null && keyboard.escapeKey.wasPressedThisFrame))
            {
                if (IsOpen && match.Running)
                    Close();
                else
                    OpenMenu();
            }
            if (recall.WasPressedThisFrame() || (desktop && keyboard != null && keyboard.bKey.wasPressedThisFrame))
            {
                Close();
                match.Recall();
            }
            if (ShopIsOpen && !CanShop())
            {
                Close();
                match.Notify("Shop closed: return to your own fountain.");
            }
            if (ray)
                ray.enabled = IsOpen && !desktop;
            if (!IsOpen)
                return;
            if (status)
                status.text = Notice;
            if (shopScreen)
            {
                ShopTick();
                if (!IsOpen)
                    return;
            }
            Ray pointer = desktop && Mouse.current != null ? match.player.head.ScreenPointToRay(Mouse.current.position.ReadValue()) : new Ray(match.player.rightHand.position, match.player.rightHand.forward);
            var hit = Physics.RaycastAll(pointer, 8, 1 << 5, QueryTriggerInteraction.Collide).OrderBy(h => h.distance).Select(h => new { hit = h, button = h.collider.GetComponent<RiftUIButton>() }).FirstOrDefault(h => h.button && h.button.gameObject.activeInHierarchy);
            var button = hit?.button;
            if (hovered && hovered != button)
                hovered.Hover(false);
            hovered = button;
            if (hovered)
                hovered.Hover(true);
            if (ray)
            {
                ray.SetPosition(0, pointer.origin);
                ray.SetPosition(1, hit != null ? hit.hit.point : pointer.GetPoint(2.3f));
            }
            bool pressed = desktop ? Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame : trigger.WasPressedThisFrame();
            if (pressed && button)
                button.Click();
        }

        bool CanShop() => match.Running && match.player.Health.IsAlive && !match.economy.Stasis && match.AtShop;

        /// <summary>Opens a fresh menu panel. Without chrome the screen draws its own header (the item shop).</summary>
        void Screen(string title, bool stable = false, bool chrome = true, float width = 1700, float height = 1100)
        {
            bool retained = stable && panel;
            Vector3 position = retained ? panel.transform.position : lastPosition;
            Quaternion rotation = retained ? panel.transform.rotation : lastRotation;
            bool reuse = retained || (stable && preservePose);
            Close();
            CurrentScreen = title;
            var rack = match.economy.GetComponent<RiftItemRack>();
            if (rack)
            {
                rack.ReturnHeld(0);
                rack.ReturnHeld(1);
            }
            // Weapons and shields are put away while a menu is open.
            match.player.Stow(this, false, true);
            match.player.Stow(this, true, true);
            weaponHidden = true;
            GetComponent<RiftVRHUD>()?.HideForMenu();
            panel = new GameObject(title, typeof(RectTransform), typeof(Canvas));
            root = panel.GetComponent<RectTransform>();
            root.sizeDelta = new Vector2(width, height);
            root.localScale = Vector3.one * .0012f;
            var forward = Vector3.ProjectOnPlane(match.player.head.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f)
                forward = Vector3.forward;
            if (!reuse)
            {
                position = match.player.head.transform.position + forward * 2.1f - Vector3.up * .08f;
                rotation = Quaternion.LookRotation(forward);
            }
            panel.transform.SetPositionAndRotation(position, rotation);
            lastPosition = position;
            lastRotation = rotation;
            preservePose = true;
            var canvas = panel.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = match.player.head;
            canvas.sortingOrder = 40;
            if (!chrome)
            {
                // League's shop frame: deep navy with a thin gold border.
                Fill(root, -width * .5f - 3, 0, width + 6, height + 6, ShopLine);
                Fill(root, -width * .5f, 0, width, height, ShopBack);
                return;
            }
            Fill(root, -width * .5f, 0, width, height, new Color(.009f, .018f, .031f, 1));
            Fill(root, -width * .5f, height * .5f - 7, width, 5, cyan);
            Text(root, title.ToUpperInvariant(), -790, 475, 1450, 75, 42, gold);
            Text(root, "SUMMONER'S RIFT  /  CHAMPION VR", -790, 420, 1490, 35, 19, muted);
            status = Text(root, Notice, -790, -493, 1560, 65, 25, gold);
        }

        public void OpenMenu() => ChampionMenu(false);

        void ChampionMenu(bool stable)
        {
            shopScreen = false;
            if (!stable)
                preservePose = false;
            Screen("League VR", stable);
            var roster = match.player.GetComponent<ChampionRoster>();
            if (!roster || roster.champions == null || roster.champions.Length == 0)
            {
                Button(root, "PLAY", -300, 0, 600, 100, () => match.Play(), "play");
                return;
            }
            var chosen = roster.Selected;
            Text(root, "CHOOSE YOUR CHAMPION", -790, 367, 770, 45, 27, cyan);
            for (int i = 0; i < roster.champions.Length; i++)
            {
                int index = i;
                var d = roster.champions[i];
                float x = -790 + (i % 3) * 255, y = 252 - (i / 3) * 150;
                var card = Button(root, d.name.ToUpperInvariant(), x, y, 238, 135, () =>
{
    roster.Select(index);
    ChampionMenu(true);
}, "champion-" + d.id);
                card.selected = i == roster.selected;
                card.Hover(false);
                card.label.rectTransform.anchoredPosition = new Vector2(7, -43);
                card.label.rectTransform.sizeDelta = new Vector2(224, 37);
                card.label.fontSize = 24;
                Icon(card.transform, d.portrait, 77, 21, 84);
                if (card.selected)
                    Fill(card.transform, -119, 64, 238, 4, d.color);
            }
            Icon(root, chosen.portrait, 55, 320, 106);
            Text(root, chosen.name.ToUpperInvariant(), 187, 353, 560, 65, 45, chosen.color);
            Text(root, chosen.title + "  /  " + chosen.role, 187, 295, 560, 52, 23, muted);
            Text(root, chosen.passiveName, 55, 234, 700, 36, 26, gold);
            // Passives run to four lines (Pantheon, Zoe, Gwen): top-aligned under the name so they never overlap it.
            var passive = Text(root, chosen.passiveDescription, 55, 162, 700, 104, 20, white);
            passive.alignment = TextAlignmentOptions.TopLeft;
            passive.overflowMode = TextOverflowModes.Ellipsis;
            for (int i = 0; i < 4; i++)
            {
                float y = 84 - i * 63;
                var spell = chosen.spells[i];
                Icon(root, spell.icon, 55, y, 45);
                Text(root, "QWER"[i] + "  " + spell.name, 121, y, 625, 55, 24, white);
            }
            Button(root, "ABILITY GUIDE", 55, -191, 700, 63, ChampionGuide, "champion-guide");
            Text(root, match.Running ? "Current match: " + match.player.ChampionName + ". Selection applies to your next match." : "10,000 starting gold  /  Physical items  /  OpenXR VR", -790, -225, 775, 77, 23, muted);
            Button(root, match.Running ? "RESUME " + match.player.ChampionName.ToUpperInvariant() : "PLAY AS " + chosen.name.ToUpperInvariant(), 55, -313, 700, 85, () =>
{
    if (match.Running)
        Close();
    else
        match.Play();
}, "play");
            Button(root, "CONTROLS", -790, -315, 365, 78, Controls, "controls");
            Button(root, "COMFORT & AUDIO", -400, -315, 365, 78, Settings, "settings");
            Button(root, "ITEM SHOP", -790, -416, 365, 76, OpenShop, "shop", CanShop());
            Button(root, "NEW MATCH", -400, -416, 365, 76, ConfirmNewMatch, "new", match.Running);
            Text(root, "Right-hand ray + trigger to select. Left stick: recall. Right stick: menu.", 55, -413, 700, 80, 23, muted);
        }

        void ChampionGuide()
        {
            shopScreen = false;
            Screen("Champion ability guide", true);
            var d = match.player.GetComponent<ChampionRoster>().Selected;
            Text(root, d.name + "  /  " + d.passiveName + " — " + d.passiveDescription, -790, 334, 1560, 89, 25, d.color);
            for (int i = 0; i < 4; i++)
            {
                float x = -790 + (i % 2) * 800, y = 181 - (i / 2) * 245;
                Icon(root, d.spells[i].icon, x, y + 41, 55);
                Text(root, "QWER"[i] + "  " + d.spells[i].name, x + 73, y + 41, 675, 68, 28, gold);
                Text(root, d.spells[i].vrDescription, x, y - 61, 749, 149, 25, white);
            }
            Text(root, "Aim with the casting hand. Head and wrist tracking stay under your control.\nAbilities are available from the start in this prototype.", -790, -345, 1560, 85, 24, muted);
            Button(root, "BACK", -220, -430, 440, 65, () => ChampionMenu(true), "back");
        }

        void ConfirmNewMatch()
        {
            shopScreen = false;
            Screen("Start a new match?", true);
            Text(root, "This starts as your selected champion and resets inventory, gold and structures.", -730, 180, 1430, 160, 32, white);
            Button(root, "START NEW MATCH", -590, -100, 530, 90, () => match.Play());
            Button(root, "KEEP PLAYING", 80, -100, 530, 90, OpenMenu);
        }

        void Settings()
        {
            shopScreen = false;
            Screen("Comfort & audio", true);
            Text(root, "GAME VOLUME", -720, 340, 1400, 55, 30, cyan);
            Text(root, $"{AudioListener.volume * 100:0}%  ·  {(!AudioListener.pause ? "Audio enabled" : "Audio paused")}", -720, 280, 1400, 50, 26, white);
            Button(root, "QUIETER", -720, 200, 400, 72, () =>
            {
                AudioListener.pause = false;
                AudioListener.volume = Mathf.Max(0, AudioListener.volume - .1f);
                Settings();
            });
            Button(root, "LOUDER", -270, 200, 400, 72, () =>
            {
                AudioListener.pause = false;
                AudioListener.volume = Mathf.Min(1, AudioListener.volume + .1f);
                Settings();
            });
            Button(root, AudioListener.volume > 0 ? "MUTE" : "UNMUTE", 180, 200, 400, 72, () =>
            {
                AudioListener.pause = false;
                AudioListener.volume = AudioListener.volume > 0 ? 0 : .7f;
                Settings();
            });

            // Comfort vignette: the dark ring that narrows the view while moving or turning.
            int vignette = ComfortSettings.VignetteLevel;
            Text(root, "COMFORT VIGNETTE", -720, 100, 1400, 55, 30, cyan);
            Text(root, $"Strength: <color=#E5C478>{ComfortSettings.VignetteName}</color>  ·  the dark ring that narrows your view while you move or turn", -720, 40, 1400, 50, 24, white);
            Button(root, "LESS", -720, -40, 400, 72, () =>
            {
                ComfortSettings.VignetteLevel = vignette - 1;
                Settings();
            }, "vignette-less", vignette > 0);
            Button(root, "MORE", -270, -40, 400, 72, () =>
            {
                ComfortSettings.VignetteLevel = vignette + 1;
                Settings();
            }, "vignette-more", vignette < ComfortSettings.VignetteNames.Length - 1);
            Button(root, vignette > 0 ? "TURN OFF" : "TURN ON", 180, -40, 400, 72, () =>
            {
                ComfortSettings.VignetteLevel = vignette > 0 ? 0 : ComfortSettings.DefaultVignetteLevel;
                Settings();
            }, "vignette-toggle");

            // Big leaps: Grand Starfall can show the landing from the sky or simply fade out.
            bool sky = ComfortSettings.SkyView;
            Text(root, "BIG LEAPS", -720, -125, 1400, 55, 30, cyan);
            Text(root, $"Grand Starfall: <color=#E5C478>{(sky ? "SKY VIEW" : "FADE")}</color>  ·  watch your landing from high above, or fade out during the leap", -720, -180, 1400, 50, 24, white);
            Button(root, sky ? "USE FADE" : "USE SKY VIEW", -720, -255, 400, 72, () =>
            {
                ComfortSettings.SkyView = !sky;
                Settings();
            }, "skyview-toggle");

            Text(root, "WRIST DISPLAY", -720, -330, 1400, 55, 30, cyan);
            Text(root, "Raise your left wrist and look at it. Menus stay where you open them; reopen to recenter.\nHeadset sound uses the playback device selected by your VR runtime.", -720, -390, 1410, 90, 23, white);
            Button(root, "BACK", -240, -470, 480, 75, OpenMenu);
        }

        string AbilityName(int slot)
        {
            var roster = match.player.GetComponent<ChampionRoster>();
            return roster && roster.Selected ? roster.Selected.spells[slot].name : "QWER"[slot].ToString();
        }

        void Controls()
        {
            shopScreen = false;
            Screen("VR controls", true);
            Text(root, "RIGHT HAND", -735, 295, 700, 50, 32, cyan);
            Text(root, "Swing your weapon through enemies   Attack\nHold trigger   Auto-attack in front\nB   Q: " + AbilityName(0) + "\nA   E: " + AbilityName(2) + "\nStick press   Menu", -735, 160, 705, 220, 24, white);
            Text(root, "LEFT HAND", 40, 295, 700, 50, 32, cyan);
            Text(root, "Trigger (hold, throw to release)   R: " + AbilityName(3) + "\nX   W: " + AbilityName(1) + "\nY   Shop at your fountain\nStick press   Recall / cancel\nStick   Move (abilities dash where you walk)", 40, 160, 705, 220, 24, white);
            Text(root, "WEARABLE ITEMS", -735, -10, 1470, 50, 31, gold);
            Text(root, "Reach to a hip, shoulder or back slot and hold grip to grab.\nAim with the item and press trigger to activate. Release grip to holster.\nLift a potion to your mouth to drink. Trinket: centre of your back.", -735, -120, 1470, 170, 26, white);
            Text(root, "Desktop: hold Tab to inspect wrist / WASD / right mouse look / Q F E R abilities / P shop / B recall\n1–6: equip items  ·  7: equip trinket  ·  left mouse: use held item  ·  Backspace: return", -735, -285, 1470, 100, 21, muted);
            Button(root, "BACK", -210, -410, 420, 75, OpenMenu);
        }

        public void OpenResult(bool victory)
        {
            shopScreen = false;
            Screen(victory ? "Victory" : "Defeat");
            Text(root, victory ? "The enemy Nexus has fallen." : "Your Nexus has fallen.", -690, 100, 1380, 180, 42, white);
            Button(root, "PLAY AGAIN", -300, -140, 600, 100, () => match.Play());
        }

        static string[] Pages(string value, TMP_Text layout, float width, float height)
        {
            var pages = new List<string>();
            string current = "";
            foreach (System.Text.RegularExpressions.Match part in Regex.Matches(value ?? "", @"\S+|\n"))
            {
                string word = part.Value;
                string candidate = current + (current.Length == 0 || current.EndsWith("\n") || word == "\n" ? "" : " ") + word;
                if (current.Length > 0 && layout.GetPreferredValues(candidate, width, float.PositiveInfinity).y > height)
                {
                    pages.Add(current.Trim());
                    current = word;
                }
                else
                    current = candidate;
            }
            if (current.Trim().Length > 0 || pages.Count == 0)
                pages.Add(current.Trim());
            return pages.ToArray();
        }

        public void Close()
        {
            if (weaponHidden && match && match.player)
            {
                match.player.Stow(this, false, false);
                match.player.Stow(this, true, false);
                weaponHidden = false;
            }
            if (panel)
            {
                lastPosition = panel.transform.position;
                lastRotation = panel.transform.rotation;
                panel.SetActive(false);
                Destroy(panel);
            }
            panel = null;
            root = null;
            status = null;
            ShopClosed();
            if (hovered)
                hovered.Hover(false);
            hovered = null;
        }

        static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(0, .5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        static void Fill(Transform parent, float x, float y, float w, float h, Color color)
        {
            var r = Rect("Panel surface", parent, x, y, w, h);
            var i = r.gameObject.AddComponent<Image>();
            i.color = color;
            i.raycastTarget = false;
            RiftUIVisuals.Graphic(i);
        }

        TMP_Text Text(Transform parent, string value, float x, float y, float width, float height, float size, Color color)
        {
            var r = Rect("Text", parent, x, y, width, height);
            var text = r.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            RiftUIVisuals.Text(text);
            return text;
        }

        RiftUIButton Button(Transform parent, string value, float x, float y, float width, float height, Action action, string key = "", bool enabled = true)
        {
            var r = Rect(value, parent, x, y, width, height);
            var image = r.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            RiftUIVisuals.Graphic(image);
            var collider = r.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(width, height, 3);
            collider.center = new Vector3(width * .5f, 0, 0);
            collider.isTrigger = true;
            var button = r.gameObject.AddComponent<RiftUIButton>();
            button.image = image;
            button.action = action;
            button.interactable = enabled;
            button.key = key;
            var label = Text(r, value, 12, 0, width - 24, height - 8, 25, white);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0, .5f);
            label.alignment = TextAlignmentOptions.Center;
            button.label = label;
            button.Hover(false);
            return button;
        }

        static void Icon(Transform parent, Texture texture, float x, float y, float size)
        {
            var rect = Rect("Original Riot item icon", parent, x, y, size, size);
            rect.anchorMin = rect.anchorMax = new Vector2(parent.GetComponent<RiftUIButton>() ? 0 : .5f, .5f);
            var image = rect.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            RiftUIVisuals.Graphic(image);
        }
    }
}
