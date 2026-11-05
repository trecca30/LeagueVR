using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Hwei's floating spell panel. It hangs a little below the line of sight and follows the head with a slight lag (no
    /// roll), drawn over the world like the rest of the HUD. Without a subject it is a slim strip: his three subjects and
    /// the ultimate with their buttons (B, X, A, left trigger) and cooldowns, plus Stirring Lights' lights. Opening a subject
    /// unfolds it into the subject's panel in its colour: three cards with each spell's icon, name and the button that
    /// casts it; holding a button lights its card up while the others dim, and Wash Brush (left trigger) is spelled out.
    /// </summary>
    [DefaultExecutionOrder(320)]
    public class HweiPanel : MonoBehaviour
    {
        const float BigScale = .00046f, StripScale = .0004f;
        /// <summary>
        /// Where the panels hang from the eyes (right, up, forward in metres), following the head's look: left of the
        /// middle and below it, clear of the brush (which reaches into the lower right) and of what he aims at.
        /// </summary>
        static readonly Vector3 BigOffset = new(-.17f, -.17f, .62f), StripOffset = new(-.14f, -.255f, .64f);

        static readonly Color Back = new(.035f, .028f, .07f, .9f), CardBack = new(.075f, .06f, .13f, .96f);
        static readonly Color Text = new(.94f, .91f, .98f), Dim = new(.68f, .63f, .78f), BadgeFill = new(.11f, .09f, .17f, 1f);

        PlayerChampion player;
        HweiKit kit;
        GameObject bigRoot, stripRoot;
        CanvasGroup bigGroup, stripGroup;
        RectTransform bigPanel;
        Image frame, divider, titleGlow;
        RawImage subjectIcon;
        TMP_Text title, wash, footer;
        readonly Card[] cards = new Card[3];
        readonly Chip[] chips = new Chip[4];
        readonly Image[] lightDots = new Image[3];
        TMP_Text lightsTime;
        Quaternion smooth;
        bool haveSmooth;
        float open, cast;
        int shownMood = -1, lastAimed = -1;
        Color subjectColor = HweiKit.Disaster;

        class Card
        {
            public RectTransform root;
            public Image back, outline, glow, flash, badgeRing;
            public RawImage icon;
            public TMP_Text name, code, badge;
            public float scale = 1, light;
        }

        class Chip
        {
            public RectTransform root;
            public Image ring, shade, glow;
            public RawImage icon;
            public TMP_Text badge, time;
        }

        public static HweiPanel Create(PlayerChampion player, HweiKit kit)
        {
            var go = new GameObject("Hwei spell panel");
            var panel = go.AddComponent<HweiPanel>();
            panel.player = player;
            panel.kit = kit;
            panel.Build();
            return panel;
        }

        void OnEnable() => Application.onBeforeRender += Pose;

        void OnDisable() => Application.onBeforeRender -= Pose;

        void OnDestroy()
        {
            if (bigRoot)
                Destroy(bigRoot);
            if (stripRoot)
                Destroy(stripRoot);
        }

        // ---------- Sprites drawn once: rounded panels, their outlines, soft glows and discs ----------

        static Sprite rounded, outlineSprite, softSprite, disc, ringSprite;

        static Sprite MakeSprite(string name, int size, float border, System.Func<float, float, float> alpha)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(alpha(x + .5f, y + .5f))));
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        /// <summary>Signed distance from a pixel to a rounded square's edge (negative inside).</summary>
        static float RoundedDistance(float x, float y, int size, float radius)
        {
            float half = size / 2f;
            float dx = Mathf.Max(Mathf.Abs(x - half) - (half - radius), 0), dy = Mathf.Max(Mathf.Abs(y - half) - (half - radius), 0);
            return Mathf.Sqrt(dx * dx + dy * dy) - radius;
        }

        static void MakeSprites()
        {
            if (rounded)
                return;
            const int Size = 64;
            const float Radius = 22;
            rounded = MakeSprite("Hwei panel rounded", Size, Radius + 2, (x, y) => .5f - RoundedDistance(x, y, Size, Radius));
            outlineSprite = MakeSprite("Hwei panel outline", Size, Radius + 2, (x, y) =>
            {
                float d = RoundedDistance(x, y, Size, Radius);
                return Mathf.Clamp01(.5f - d) - Mathf.Clamp01(.5f - (d + 3));
            });
            softSprite = MakeSprite("Hwei panel glow", Size, 28, (x, y) =>
            {
                float d = RoundedDistance(x, y, Size, 28);
                return d > 0 ? 0 : Mathf.Pow(Mathf.Clamp01(-d / 26f), 1.5f);
            });
            disc = MakeSprite("Hwei panel disc", Size, 0, (x, y) => .5f - (Vector2.Distance(new Vector2(x, y), new Vector2(Size / 2f, Size / 2f)) - (Size / 2f - 1)));
            ringSprite = MakeSprite("Hwei panel ring", Size, 0, (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(Size / 2f, Size / 2f)) - (Size / 2f - 1);
                return Mathf.Clamp01(.5f - d) - Mathf.Clamp01(.5f - (d + 4));
            });
        }

        // ---------- Building ----------

        static RectTransform Box(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var r = go.GetComponent<RectTransform>();
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, h);
            return r;
        }

        static Image Shape(Transform parent, string name, Sprite sprite, float x, float y, float w, float h, Color color, bool sliced = true)
        {
            var image = Box(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = sliced && sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            image.pixelsPerUnitMultiplier = 1;
            image.color = color;
            image.raycastTarget = false;
            RiftUIVisuals.Graphic(image);
            return image;
        }

        static RawImage Picture(Transform parent, string name, Texture texture, float x, float y, float size)
        {
            var image = Box(parent, name, x, y, size, size).gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            RiftUIVisuals.Graphic(image);
            return image;
        }

        static TMP_Text Label(Transform parent, string name, string value, float x, float y, float w, float h, float size, Color color, TextAlignmentOptions align, bool bold = false)
        {
            var text = Box(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            if (bold)
                text.fontStyle = FontStyles.Bold;
            RiftUIVisuals.Text(text);
            return text;
        }

        static Texture2D Icon(string name) => Resources.Load<Texture2D>("HweiFx/Icons/" + name);

        void Build()
        {
            MakeSprites();
            // The subject's panel: header, three spell cards, a footer.
            bigRoot = RiftVRHUD.Canvas("Hwei subject panel", 800, 330, BigScale);
            bigGroup = bigRoot.AddComponent<CanvasGroup>();
            bigPanel = Box(bigRoot.transform, "Panel", 0, 0, 800, 330);
            titleGlow = Shape(bigPanel, "Glow", softSprite, 0, 0, 830, 360, new Color(1, 1, 1, .18f));
            Shape(bigPanel, "Back", rounded, 0, 0, 800, 330, Back);
            frame = Shape(bigPanel, "Frame", outlineSprite, 0, 0, 800, 330, Color.white);
            subjectIcon = Picture(bigPanel, "Subject icon", null, -358, 128, 50);
            title = Label(bigPanel, "Subject", "", -95, 128, 470, 50, 34, Color.white, TextAlignmentOptions.MidlineLeft, true);
            title.characterSpacing = 8;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            wash = Label(bigPanel, "Wash", "<b>LT</b>  Wash Brush", 290, 128, 190, 40, 19, Dim, TextAlignmentOptions.MidlineRight);
            divider = Shape(bigPanel, "Divider", disc, 0, 98, 740, 3, Color.white, false);
            for (int i = 0; i < 3; i++)
                cards[i] = MakeCard(bigPanel, i, -250 + i * 250, -28);
            footer = Label(bigPanel, "Footer", "Hold to aim with your brush  ·  release to paint", 0, -146, 740, 30, 17, Dim, TextAlignmentOptions.Center);
            // The strip without a subject: Q, W, E subjects and R, with Stirring Lights' lights at its end.
            stripRoot = RiftVRHUD.Canvas("Hwei subject strip", 560, 120, StripScale);
            stripGroup = stripRoot.AddComponent<CanvasGroup>();
            Shape(stripRoot.transform, "Back", rounded, 0, 0, 560, 112, new Color(Back.r, Back.g, Back.b, .72f));
            string[] chipIcons = { "HweiQ", "HweiW", "HweiE", "HweiR" };
            for (int i = 0; i < 4; i++)
                chips[i] = MakeChip(stripRoot.transform, i, -210 + i * 112, 0, chipIcons[i]);
            for (int i = 0; i < 3; i++)
                lightDots[i] = Shape(stripRoot.transform, "Light", disc, 222 + (i - 1) * 22, 14, 16, 16, new Color(.45f, .92f, 1f, 1f), false);
            lightsTime = Label(stripRoot.transform, "Lights time", "", 222, -18, 90, 30, 18, new Color(.6f, .95f, 1f), TextAlignmentOptions.Center);
            bigRoot.SetActive(false);
            stripRoot.SetActive(false);
        }

        Card MakeCard(Transform parent, int index, float x, float y)
        {
            var card = new Card { root = Box(parent, "Card " + index, x, y, 228, 206) };
            card.glow = Shape(card.root, "Glow", softSprite, 0, 0, 268, 246, new Color(1, 1, 1, 0));
            card.back = Shape(card.root, "Back", rounded, 0, 0, 228, 206, CardBack);
            card.outline = Shape(card.root, "Outline", outlineSprite, 0, 0, 228, 206, Color.white);
            card.icon = Picture(card.root, "Icon", null, 0, 30, 100);
            Shape(card.root, "Icon frame", outlineSprite, 0, 30, 108, 108, new Color(1f, .82f, .45f, .55f));
            card.name = Label(card.root, "Name", "", 0, -58, 214, 56, 24, Text, TextAlignmentOptions.Center, true);
            card.name.lineSpacing = -18;
            card.code = Label(card.root, "Code", "", 84, 86, 60, 26, 16, Dim, TextAlignmentOptions.Center);
            // The button that casts it, a gold-ringed badge over the card's corner.
            Shape(card.root, "Badge", disc, -98, 88, 58, 58, BadgeFill, false);
            card.badgeRing = Shape(card.root, "Badge ring", ringSprite, -98, 88, 58, 58, new Color(1f, .8f, .42f, 1f), false);
            card.badge = Label(card.root, "Button", HweiKit.Buttons[index], -98, 89, 58, 58, 32, Color.white, TextAlignmentOptions.Center, true);
            card.flash = Shape(card.root, "Flash", rounded, 0, 0, 228, 206, new Color(1, 1, 1, 0));
            return card;
        }

        Chip MakeChip(Transform parent, int slot, float x, float y, string icon)
        {
            var chip = new Chip { root = Box(parent, "Chip " + slot, x, y, 100, 100) };
            chip.glow = Shape(chip.root, "Glow", softSprite, 0, 0, 124, 124, new Color(1, 1, 1, 0));
            chip.icon = Picture(chip.root, "Icon", Icon(icon), 0, 4, 70);
            chip.shade = Shape(chip.root, "Cooldown", rounded, 0, 4, 70, 70, new Color(0, 0, 0, .68f), false);
            chip.shade.type = Image.Type.Filled;
            chip.shade.fillMethod = Image.FillMethod.Radial360;
            chip.shade.fillOrigin = (int)Image.Origin360.Top;
            chip.shade.fillClockwise = false;
            chip.ring = Shape(chip.root, "Ring", outlineSprite, 0, 4, 78, 78, HweiKit.SubjectColor(slot < 3 ? slot : 3));
            chip.time = Label(chip.root, "Time", "", 0, 4, 70, 40, 24, Color.white, TextAlignmentOptions.Center, true);
            Shape(chip.root, "Badge", disc, 32, -30, 34, 34, BadgeFill, false);
            Shape(chip.root, "Badge ring", ringSprite, 32, -30, 34, 34, new Color(1f, .8f, .42f, 1f), false);
            chip.badge = Label(chip.root, "Button", HweiKit.Buttons[slot], 32, -29, 34, 34, slot == 3 ? 14 : 19, Color.white, TextAlignmentOptions.Center, true);
            return chip;
        }

        // ---------- Every frame ----------

        bool Allowed
        {
            get
            {
                if (!player || player.Kit != kit || !player.Health || !player.Health.IsAlive || player.CombatLocked)
                    return false;
                if (player.BrushView && player.BrushView.Active)
                    return false;
                var match = RiftMatch.Instance;
                return !match || match.Running;
            }
        }

        void LateUpdate()
        {
            if (!bigRoot)
                return;
            bool allowed = Allowed;
            int mood = allowed ? kit.Mood : -1;
            float dt = Time.unscaledDeltaTime;
            if (mood >= 0)
            {
                if (mood != shownMood)
                    ShowSubject(mood);
                shownMood = mood;
                lastAimed = kit.Aiming >= 0 ? kit.Aiming : lastAimed;
                cast = 0;
            }
            else if (shownMood >= 0)
            {
                // The subject closed: a spell was cast (its card flashes) or the brush was washed.
                // Only a cast flashes its card; Wash Brush just closes the panel.
                cast = lastAimed >= 0 && Time.time - kit.LastCast < .2f ? 1 : 0;
                shownMood = -1;
            }
            open = Mathf.MoveTowards(open, mood >= 0 ? 1 : 0, dt / (mood >= 0 ? .12f : .2f));
            cast = Mathf.MoveTowards(cast, 0, dt / .2f);
            bool big = open > 0 && allowed;
            bigRoot.SetActive(big);
            stripRoot.SetActive(allowed && open < 1);
            if (big)
                UpdateSubject(dt);
            if (allowed && open < 1)
                UpdateStrip();
            if (mood < 0 && open <= 0)
                lastAimed = -1;
        }

        void ShowSubject(int mood)
        {
            var c = subjectColor = HweiKit.SubjectColor(mood);
            title.text = HweiKit.SubjectNames[mood].ToUpperInvariant();
            title.color = Color.Lerp(c, Color.white, .25f);
            subjectIcon.texture = Icon("Hwei" + "QWE"[mood]);
            frame.color = new Color(c.r, c.g, c.b, .9f);
            divider.color = new Color(c.r, c.g, c.b, .55f);
            titleGlow.color = new Color(c.r, c.g, c.b, .22f);
            for (int i = 0; i < 3; i++)
            {
                var card = cards[i];
                card.icon.texture = Icon("Hwei" + HweiKit.SpellCodes[mood * 3 + i]);
                card.name.text = HweiKit.SpellName(mood, i);
                card.code.text = HweiKit.SpellCodes[mood * 3 + i];
                card.scale = 1;
                card.light = 0;
            }
            lastAimed = -1;
        }

        void UpdateSubject(float dt)
        {
            float ease = 1 - (1 - open) * (1 - open);
            bigGroup.alpha = ease;
            bigPanel.localScale = Vector3.one * Mathf.Lerp(.9f, 1, ease);
            int aimed = kit.Mood >= 0 ? kit.Aiming : -1;
            var c = subjectColor;
            for (int i = 0; i < 3; i++)
            {
                var card = cards[i];
                bool lit = i == aimed || (kit.Mood < 0 && i == lastAimed);
                float targetScale = aimed < 0 ? 1 : lit ? 1.07f : .95f;
                card.scale = Mathf.MoveTowards(card.scale, targetScale, dt * 1.6f);
                card.light = Mathf.MoveTowards(card.light, lit ? 1 : 0, dt * 8);
                card.root.localScale = Vector3.one * card.scale;
                float dim = aimed >= 0 && !lit ? .5f : 1;
                card.back.color = new Color(CardBack.r, CardBack.g, CardBack.b, CardBack.a * Mathf.Lerp(1, .8f, 1 - dim));
                card.outline.color = new Color(c.r, c.g, c.b, Mathf.Lerp(.5f, 1, card.light) * dim);
                card.glow.color = new Color(c.r, c.g, c.b, .45f * card.light);
                card.icon.color = new Color(1, 1, 1, dim);
                card.name.color = new Color(Text.r, Text.g, Text.b, dim);
                card.badgeRing.color = Color.Lerp(new Color(1f, .8f, .42f, dim), Color.white, card.light);
                card.badge.color = new Color(1, 1, 1, dim);
                card.flash.color = new Color(c.r, c.g, c.b, i == lastAimed ? .55f * cast : 0);
            }
        }

        void UpdateStrip()
        {
            stripGroup.alpha = (1 - open) * .92f;
            for (int slot = 0; slot < 4; slot++)
            {
                var chip = chips[slot];
                float cooldown = player.Cooldown(slot);
                float total = Mathf.Max(.01f, player.BaseCooldown(slot) * player.Stats.CooldownMultiplier);
                bool locked = slot == 3 && player.Definition && player.Definition.Rank(3, player.Level) == 0;
                chip.shade.fillAmount = locked ? 1 : Mathf.Clamp01(cooldown / total);
                chip.time.text = locked ? "LV6" : cooldown > 0 ? (cooldown >= 10 ? cooldown.ToString("0") : cooldown.ToString("0.0")) : "";
                chip.time.fontSize = locked ? 19 : 24;
                bool ready = !locked && cooldown <= 0;
                chip.icon.color = ready ? Color.white : new Color(.75f, .75f, .8f, 1);
                var c = HweiKit.SubjectColor(slot < 3 ? slot : 3);
                chip.ring.color = new Color(c.r, c.g, c.b, ready ? .95f : .35f);
                bool glowing = slot == 3 && kit.AimingUltimate;
                chip.glow.color = new Color(c.r, c.g, c.b, glowing ? .55f + .15f * Mathf.Sin(Time.time * 10) : 0);
            }
            int lights = kit.Lights;
            for (int i = 0; i < 3; i++)
                lightDots[i].enabled = i < lights;
            lightsTime.text = lights > 0 ? kit.LightsLeft.ToString("0.0") + "s" : "";
        }

        /// <summary>Hangs the panels below the line of sight, turning with the head with a slight lag and no roll.</summary>
        [BeforeRenderOrder(195)]
        void Pose()
        {
            if (!bigRoot || !player || !player.head)
                return;
            var head = player.head.transform;
            Vector3 forward = head.forward;
            if (Mathf.Abs(forward.y) > .98f)
                forward = head.up * -Mathf.Sign(forward.y);
            var target = Quaternion.LookRotation(forward, Vector3.up);
            smooth = haveSmooth ? Quaternion.Slerp(smooth, target, 1 - Mathf.Exp(-14 * Time.unscaledDeltaTime)) : target;
            haveSmooth = true;
            Place(bigRoot.transform, head.position, BigOffset);
            Place(stripRoot.transform, head.position, StripOffset);
        }

        void Place(Transform panel, Vector3 eyes, Vector3 offset)
        {
            Vector3 at = eyes + smooth * offset;
            panel.SetPositionAndRotation(at, Quaternion.LookRotation(at - eyes, smooth * Vector3.up));
        }
    }
}
