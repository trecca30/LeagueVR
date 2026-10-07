using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
namespace LeagueVR.Match
{
    [DefaultExecutionOrder(310)]
    public class RiftVRHUD : MonoBehaviour
    {
        public RiftMatch match;
        public bool WristVisible { get; private set; }
        public bool TargetVisible => targetRoot && targetRoot.activeSelf;
        public bool RecallVisible => recallRoot && recallRoot.activeSelf;
        public Combatant AttackTarget { get; private set; }
        GameObject wrist, noticeRoot, recallRoot, targetRoot;
        TMP_Text vitals, abilities, meta, notice, recallTime, targetName, targetNumbers;
        Image hp, mp, shield, recallFill, targetFill;
        float next, watchDwell, watchHide, lastAttack = -10;

        void OnEnable()
        {
            Application.onBeforeRender += Pose;
            Combatant.Attacked += OnAttacked;
        }

        void OnDisable()
        {
            Application.onBeforeRender -= Pose;
            Combatant.Attacked -= OnAttacked;
            HideForMenu();
        }

        void Start()
        {
            var feedback = match.player.GetComponent<GwenFeedback>();
            if (feedback && feedback.status)
                feedback.status.gameObject.SetActive(false);
            wrist = Canvas("Gwen wrist interface", 500, 264, .00038f);
            Surface(wrist.transform, -250, 0, 500, 264, new Color(.015f, .027f, .04f, .94f));
            Surface(wrist.transform, -250, 130, 500, 4, new Color(.3f, .9f, .92f));
            vitals = Text(wrist.transform, "", -227, 91, 454, 48, 25, new Color(.87f, .96f, .96f));
            Surface(wrist.transform, -227, 47, 454, 15, new Color(.06f, .16f, .13f));
            hp = Surface(wrist.transform, -227, 47, 454, 15, new Color(.27f, .79f, .53f));
            shield = Surface(wrist.transform, -227, 47, 454, 15, new Color(.7f, .91f, .99f, .85f));
            Surface(wrist.transform, -227, 22, 454, 9, new Color(.06f, .12f, .20f));
            mp = Surface(wrist.transform, -227, 22, 454, 9, new Color(.25f, .55f, .92f));
            abilities = Text(wrist.transform, "", -227, -34, 454, 89, 19, new Color(.45f, .94f, .95f));
            meta = Text(wrist.transform, "", -227, -102, 454, 43, 22, new Color(.9f, .77f, .46f));
            noticeRoot = Canvas("Transient match notice", 850, 95, .00065f);
            Surface(noticeRoot.transform, -425, 0, 850, 95, new Color(.015f, .027f, .04f, .88f));
            notice = Text(noticeRoot.transform, "", -399, 0, 798, 89, 25, new Color(.94f, .85f, .63f));
            notice.alignment = TextAlignmentOptions.Center;
            recallRoot = Canvas("Recall progress", 520, 80, .00065f);
            Surface(recallRoot.transform, -260, 0, 520, 80, new Color(.015f, .027f, .04f, .90f));
            Surface(recallRoot.transform, -238, -20, 476, 13, new Color(.04f, .12f, .18f));
            recallFill = Surface(recallRoot.transform, -238, -20, 476, 13, new Color(.3f, .86f, .97f));
            recallTime = Text(recallRoot.transform, "", -238, 14, 476, 43, 45, Color.white);
            recallTime.alignment = TextAlignmentOptions.Center;
            targetRoot = Canvas("Attacked target health", 520, 110, .00065f);
            Surface(targetRoot.transform, -260, 0, 520, 110, new Color(.015f, .027f, .04f, .91f));
            targetName = Text(targetRoot.transform, "", -238, 29, 330, 44, 25, Color.white);
            targetName.textWrappingMode = TextWrappingModes.NoWrap;
            targetName.enableAutoSizing = true;
            targetName.fontSizeMin = 22;
            targetName.fontSizeMax = 29;
            targetName.overflowMode = TextOverflowModes.Ellipsis;
            targetNumbers = Text(targetRoot.transform, "", 96, 29, 142, 44, 27, new Color(.85f, .94f, .96f));
            targetNumbers.alignment = TextAlignmentOptions.MidlineRight;
            Surface(targetRoot.transform, -238, -21, 476, 17, new Color(.19f, .055f, .07f));
            targetFill = Surface(targetRoot.transform, -238, -21, 476, 17, new Color(.96f, .32f, .4f));
            foreach (var root in new[] { wrist, noticeRoot, recallRoot, targetRoot })
            {
                foreach (var g in root.GetComponentsInChildren<Graphic>())
                    if (g is not TMP_Text)
                        RiftUIVisuals.Graphic(g);
                foreach (var t in root.GetComponentsInChildren<TMP_Text>())
                    RiftUIVisuals.Text(t);
                root.SetActive(false);
            }
        }

        void OnAttacked(Combatant target, DamageHit hit)
        {
            if (!match || !match.player || hit.source != match.player.Health || (hit.isItemEffect && Time.time - RiftItemRack.LastUseTime > .3f) || hit.ability == "Passive")
                return;
            AttackTarget = target;
            lastAttack = Time.time;
        }

        public void HideForMenu()
        {
            foreach (var root in new[] { wrist, noticeRoot, recallRoot, targetRoot })
                if (root)
                    root.SetActive(false);
            WristVisible = false;
            watchDwell = watchHide = 0;
        }

        void OnDestroy()
        {
            foreach (var root in new[] { wrist, noticeRoot, recallRoot, targetRoot })
                if (root)
                    Destroy(root);
        }

        public static bool LookingAtWatch(Transform head, Vector3 position, Vector3 outward, bool alreadyVisible = false)
        {
            var delta = position - head.position;
            float distance = delta.magnitude;
            return distance > .22f && distance < .85f && Vector3.Angle(head.forward, delta) < (alreadyVisible ? 24 : 18) && Vector3.Dot(outward.normalized, -delta.normalized) > .45f;
        }

        void LateUpdate()
        {
            if (!wrist || !match.player)
                return;
            var head = match.player.head.transform;
            var hand = match.player.leftHand;
            bool looking = match.player.DesktopMode ? Keyboard.current != null && Keyboard.current.tabKey.isPressed : LookingAtWatch(head, hand.TransformPoint(new Vector3(-.015f, .04f, -.095f)), hand.up, WristVisible);
            if (looking)
            {
                watchDwell += Time.unscaledDeltaTime;
                watchHide = 0;
            }
            else
            {
                watchDwell = 0;
                watchHide += Time.unscaledDeltaTime;
            }
            bool allowed = match.Running && !match.ui.IsOpen && match.player.Health.IsAlive;
            WristVisible = allowed && (WristVisible ? watchHide < .12f : watchDwell >= .18f);
            Pose();
            if (Time.time < next)
                return;
            next = Time.time + .05f;
            var p = match.player;
            var e = match.economy;
            var h = p.Health;
            vitals.text = $"{p.ChampionName.ToUpperInvariant()}   <color=#93E4B0>{h.Health:0}/{h.maxHealth:0}</color>   <color=#80B6F2>{e.Mana:0} MP</color>";
            hp.fillAmount = h.Health / h.maxHealth;
            mp.fillAmount = e.MaxMana > 0 ? e.Mana / e.MaxMana : 0;
            shield.fillAmount = Mathf.Clamp01(h.Shield / h.maxHealth);
            string CD(string k) => p.Cooldown(k) > 0 ? $"<color=#829399>{p.Cooldown(k):0.0}s</color>" : "READY";
            abilities.text = $"Q  {CD("Q")}   <color=#E5C478>{p.QStacks}/4</color>    W  {(p.MistActive ? "MIST" : CD("W"))}\nE  {CD("E")}       R  {(p.RStage > 0 ? "RECAST " + (p.RStage + 1) : CD("R"))}";
            if (p.OtherActive)
                abilities.text = $"Q {CD("Q")}    W {CD("W")}\nE {CD("E")}    R {CD("R")}\n{p.Other.StateText}";
            meta.text = $"Lv {e.Level}    {e.Gold:N0}g    {(int)match.Seconds / 60:00}:{(int)match.Seconds % 60:00}";
            recallTime.text = $"{match.RecallRemaining:0.0} s";
            recallFill.fillAmount = 1 - match.RecallRemaining / 8;
            notice.text = match.ui.Notice;
            if (AttackTarget)
            {
                var s = AttackTarget.GetComponent<RiftStructure>();
                targetName.text = AttackTarget.name + (s && !s.Vulnerable ? " · SHIELDED" : "");
                targetNumbers.text = $"{AttackTarget.Health:0}/{AttackTarget.maxHealth:0}";
                targetFill.fillAmount = AttackTarget.Health / AttackTarget.maxHealth;
            }
        }

        [BeforeRenderOrder(190)]
        void Pose()
        {
            if (!wrist || !match.player)
                return;
            var head = match.player.head.transform;
            var hand = match.player.leftHand;
            if (match.player.DesktopMode)
            {
                var p = head.TransformPoint(new Vector3(-.2f, -.16f, .6f));
                wrist.transform.SetPositionAndRotation(p, head.rotation);
            }
            else
                wrist.transform.SetPositionAndRotation(hand.TransformPoint(new Vector3(-.015f, .04f, -.095f)), Quaternion.LookRotation(-hand.up, hand.forward));
            bool allowed = match.Running && !match.ui.IsOpen && match.player.Health.IsAlive;
            wrist.SetActive(allowed && WristVisible);
            noticeRoot.transform.SetPositionAndRotation(head.TransformPoint(new Vector3(0, .21f, 1.65f)), head.rotation);
            noticeRoot.SetActive(allowed && !match.IsRecalling && !string.IsNullOrEmpty(match.ui.Notice));
            recallRoot.transform.SetPositionAndRotation(head.TransformPoint(new Vector3(0, -.24f, 1.4f)), head.rotation);
            recallRoot.SetActive(allowed && match.IsRecalling);
            targetRoot.transform.SetPositionAndRotation(head.TransformPoint(new Vector3(0, -.27f, 1.5f)), head.rotation);
            targetRoot.SetActive(allowed && !match.IsRecalling && AttackTarget && Time.time - lastAttack < 1.05f);
        }

        internal static GameObject Canvas(string name, float w, float h, float scale)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            go.layer = 5;
            var r = go.GetComponent<RectTransform>();
            r.sizeDelta = new Vector2(w, h);
            r.localScale = Vector3.one * scale;
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            return go;
        }

        internal static RectTransform Rect(Transform p, float x, float y, float w, float h)
        {
            var go = new GameObject("HUD element", typeof(RectTransform));
            go.layer = 5;
            var r = go.GetComponent<RectTransform>();
            r.SetParent(p, false);
            r.anchorMin = r.anchorMax = new Vector2(.5f, .5f);
            r.pivot = new Vector2(0, .5f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, h);
            return r;
        }

        internal static TMP_Text Text(Transform p, string value, float x, float y, float w, float h, float size, Color col)
        {
            var t = Rect(p, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            t.text = value;
            t.fontSize = size;
            t.color = col;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.raycastTarget = false;
            return t;
        }
        static Sprite solid;

        internal static Image Surface(Transform p, float x, float y, float w, float h, Color col)
        {
            var i = Rect(p, x, y, w, h).gameObject.AddComponent<Image>();
            if (!solid)
                solid = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height), new Vector2(.5f, .5f));
            i.sprite = solid;
            i.color = col;
            i.raycastTarget = false;
            i.type = Image.Type.Filled;
            i.fillMethod = Image.FillMethod.Horizontal;
            i.fillOrigin = 0;
            return i;
        }
    }

    public class RiftWorldHealthBar : MonoBehaviour
    {
        RiftActor actor;
        GameObject canvas;
        Image fill;
        TMP_Text caption;
        float next;
        public Vector3 BarPosition => canvas ? canvas.transform.position : Vector3.zero;
        public float StructureTop { get; private set; }

        public void Initialize(RiftActor value)
        {
            actor = value;
            if (actor.label)
                actor.label.gameObject.SetActive(false);
            canvas = RiftVRHUD.Canvas("World health bar", 300, 88, actor.structure ? .005f : .002f);
            canvas.transform.SetParent(actor.transform, false);
            RiftVRHUD.Surface(canvas.transform, -150, 0, 300, actor.structure ? 26 : 15, new Color(.015f, .026f, .037f));
            fill = RiftVRHUD.Surface(canvas.transform, -146, 0, 292, actor.structure ? 20 : 10, Color.white);
            caption = RiftVRHUD.Text(canvas.transform, "", -180, 35, 360, 45, 24, new Color(.86f, .94f, .96f));
            caption.alignment = TextAlignmentOptions.Center;
            var s = actor.GetComponent<RiftStructure>();
            StructureTop = actor.transform.position.y;
            if (s && s.visual)
                foreach (var r in s.visual.GetComponentsInChildren<Renderer>())
                    if (r is SkinnedMeshRenderer || r is MeshRenderer && r.GetComponent<MeshFilter>())
                        StructureTop = Mathf.Max(StructureTop, r.bounds.max.y);
        }

        void LateUpdate()
        {
            var match = RiftMatch.Instance;
            if (!actor || !canvas || !match || !match.player)
                return;
            var head = match.player.head.transform;
            canvas.transform.position = actor.structure ? new Vector3(actor.transform.position.x, StructureTop + .65f, actor.transform.position.z) : actor.health.AimPosition + Vector3.up * .55f;
            canvas.transform.rotation = Quaternion.LookRotation(canvas.transform.position - head.position);
            bool visible = match.Running && !match.ui.IsOpen && actor.health != match.player.Health && actor.health.IsAlive && actor.health.IsTargetable && Vector3.Distance(head.position, canvas.transform.position) < (actor.structure ? 48 : 18) && (!actor.neutral || actor.Targetable) && Vector3.Angle(head.forward, canvas.transform.position - head.position) < 80;
            canvas.SetActive(visible);
            if (!visible || Time.time < next)
                return;
            next = Time.time + .1f;
            fill.fillAmount = actor.health.Health / actor.health.maxHealth;
            fill.color = actor.health.team == match.player.Health.team ? new Color(.26f, .69f, .97f) : actor.neutral ? new Color(.8f, .67f, .39f) : new Color(.92f, .34f, .42f);
            var structure = actor.GetComponent<RiftStructure>();
            caption.text = structure ? actor.name + (structure.Vulnerable ? "" : " · SHIELDED") : actor.health.countsAsChampion || actor.neutral ? actor.name : "";
        }
    }
}
