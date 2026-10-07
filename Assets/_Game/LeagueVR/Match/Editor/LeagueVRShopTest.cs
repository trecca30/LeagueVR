using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.Rendering.Universal;
using Newtonsoft.Json.Linq;
using LeagueVR.Match;
using Object = UnityEngine.Object;
namespace LeagueVR.Editor
{
    [InitializeOnLoad]
    public static class LeagueVRShopTest
    {
        static LeagueVRShopTest()
        {
            EditorApplication.playModeStateChanged += State;
        }

        public static void Begin()
        {
            if (EditorApplication.isPlaying)
                throw new Exception("Begin from edit mode.");
            if (SessionState.GetBool("LeagueVR.ShopOneTestUsed", false))
                throw new Exception("The requested one gameplay test has already been started. Do not run again.");
            SessionState.SetBool("LeagueVR.ShopOneTestUsed", true);
            SessionState.SetBool("LeagueVR.ShopTestPending", true);
            EditorApplication.isPlaying = true;
        }

        static void State(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("LeagueVR.ShopTestPending", false))
            {
                SessionState.SetBool("LeagueVR.ShopTestPending", false);
                new GameObject("One comprehensive shop/item/UI audit").AddComponent<LeagueVRShopProbe>();
            }
        }
    }

    public class LeagueVRShopProbe : MonoBehaviour
    {
        const string Dir = "Logs/ShopOverhaul";
        RiftMatch match;
        RiftEconomy e;
        RiftItemEffects fx;
        RiftItemRack rack;
        GwenAbilities p;
        Combatant enemy, ally;
        Camera camera;
        RenderTexture rt;
        JObject riot;
        readonly List<string> failures = new(), passes = new(), errors = new(), coverage = new();
        int items, activeItems;
        bool done;

        IEnumerator Start()
        {
            Directory.CreateDirectory(Dir);
            Application.logMessageReceived += Message;
            yield return null;
            yield return null;
            IEnumerator suite = Suite();
            while (true)
            {
                bool more;
                object yielded = null;
                try
                {
                    more = suite.MoveNext();
                    if (more)
                        yielded = suite.Current;
                }
                catch (Exception ex)
                {
                    failures.Add("Unhandled audit failure: " + ex);
                    break;
                }
                if (!more)
                    break;
                yield return yielded;
            }
            Finish();
        }

        void Message(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception)
                errors.Add(message + "\n" + trace);
        }

        void Check(bool good, string label)
        {
            (good ? passes : failures).Add(label);
        }

        void Near(float actual, float expected, string label, float eps = .025f) => Check(Mathf.Abs(actual - expected) <= eps, $"{label}: actual {actual:F3}, expected {expected:F3}");

        RiftUIButton Button(string key) => match.ui.Buttons.FirstOrDefault(b => b.key == key);

        void Reset()
        {
            match.ui.Close();
            rack.ReturnHeld(0);
            rack.ReturnHeld(1);
            e.ResetMatch();
            e.Level = 18;
            e.CombatGold = 1500;
            e.SupportGold = 1000;
            e.Gold = 100000;
            e.Recalculate();
            p.ResetPractice();
            fx.ResetEffects();
            foreach (var w in RiftVisionWard.All.ToArray())
                if (w)
                    Object.Destroy(w.gameObject);
            if (enemy)
            {
                enemy.ResetHealth();
                enemy.transform.position = p.Feet + Vector3.forward * 2.8f;
            }
            if (ally)
            {
                ally.ResetHealth();
                ally.transform.position = p.Feet + Vector3.forward * 3 + Vector3.right * .2f;
            }
            p.head.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            p.DesktopMode = true;
        }

        Combatant Probe(string name, int team, Vector3 at)
        {
            var go = new GameObject(name);
            go.layer = 8;
            go.transform.position = at;
            var h = go.AddComponent<Combatant>();
            h.team = team;
            h.countsAsChampion = true;
            h.maxHealth = 20000;
            h.armor = h.magicResistance = 0;
            h.ResetHealth();
            var col = go.AddComponent<CapsuleCollider>();
            col.height = 1.8f;
            col.radius = .3f;
            col.center = Vector3.up * .9f;
            go.AddComponent<RiftActor>();
            return h;
        }

        bool Percent(JToken raw, string name) => Regex.IsMatch((string)raw["description"], @"<attention>[\d.]+%</attention>\s*" + Regex.Escape(name) + @"(?:<|$)");

        float Stat(JToken raw, string name, bool? percent = null)
        {
            string stats = Regex.Match((string)raw["description"], "<stats>(.*?)</stats>", RegexOptions.Singleline).Groups[1].Value;
            var rows = Regex.Matches(stats, @"<attention>([\d.]+)(%)?</attention>\s*([^<]+)");
            foreach (System.Text.RegularExpressions.Match row in rows)
                if (row.Groups[3].Value.Trim() == name && (!percent.HasValue || row.Groups[2].Success == percent.Value))
                    return float.Parse(row.Groups[1].Value, CultureInfo.InvariantCulture) * (row.Groups[2].Success ? .01f : 1);
            return 0;
        }

        IEnumerator Suite()
        {
            match = Object.FindAnyObjectByType<RiftMatch>();
            p = match.player;
            e = match.economy;
            fx = e.Effects;
            rack = e.GetComponent<RiftItemRack>();
            Check(match && e && fx && rack, "Runtime match, economy, item effects and physical rack initialise");
            riot = JObject.Parse(File.ReadAllText("Assets/_Game/LeagueVR/Match/Data/riot-items-16.19.1.json"));
            var input = p.GetComponent<GwenVRInput>();
            input.enabled = false;
            p.DesktopMode = true;
            camera = new GameObject("Shop audit capture").AddComponent<Camera>();
            camera.enabled = false;
            camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
            camera.nearClipPlane = .01f;
            camera.fieldOfView = 60;
            rt = new RenderTexture(1600, 1000, 24);
            camera.targetTexture = rt;
            match.ui.OpenMenu();
            yield return null;
            Capture("01-Menu");
            Check(Button("play") && Button("play").Click(), "Menu PLAY button starts match");
            yield return null;
            Check(match.Running && match.AtShop, "PLAY starts at own fountain");
            match.enabled = false;
            e.enabled = false;
            foreach (var f in match.fountains)
                f.enabled = false;
            foreach (var s in match.structures)
                s.enabled = false;
            foreach (var o in match.objectives)
                o.enabled = false;
            enemy = Probe("Enemy champion audit probe", 1 - p.Health.team, p.Feet + Vector3.forward * 2.8f);
            ally = Probe("Allied champion audit probe", p.Health.team, p.Feet + Vector3.forward * 3 + Vector3.right * .2f);
            Reset();
            match.ui.OpenShop();
            yield return null;
            Capture("02-Shop");
            Check(match.ui.ShopIsOpen, "Own fountain opens shop");
            var pose = match.ui.Buttons.First().transform.root;
            Vector3 panelPosition = pose.position;
            p.head.transform.rotation = Quaternion.Euler(0, 30, 0);
            match.ui.SelectCatalogItem(3157);
            Near(Vector3.Distance(panelPosition, match.ui.Buttons.First().transform.root.position), 0, "Shop redraw retains world position");
            p.head.transform.rotation = Quaternion.identity;
            Vector3 origin = p.origin.transform.position;
            p.origin.transform.position += Vector3.forward * 16;
            yield return null;
            Check(!match.ui.IsOpen, "Shop automatically closes outside fountain");
            match.ui.OpenShop();
            Check(!match.ui.ShopIsOpen, "Shop cannot open outside fountain");
            Check(!e.Buy(1056), "Purchase denied outside fountain");
            p.origin.transform.position = origin;
            yield return null;
            var shopItems = match.catalog.items.Where(i => i.showInShop).ToArray();
            Check(shopItems.Length == 209, "209 current Summoner's Rift shop entries");
            Check(match.catalog.items.All(i => i.icon), "All catalog entries have original Riot icons");
            Check(match.catalog.items.Where(i => i.active != ItemActive.None).All(i => i.physicalPrefab && i.physicalPrefab.GetComponentsInChildren<MeshRenderer>().Any(r => r.sharedMaterial && r.sharedMaterial.GetTexture("_BaseMap"))), "All 36 active entries have textured 3D prefabs");
            foreach (var item in shopItems)
            {
                Reset();
                float baseHP = p.Health.maxHealth, baseAR = p.Health.armor, baseMR = p.Health.magicResistance, baseAD = p.tuning.attackDamage, baseAS = p.tuning.attackInterval, baseMP = e.MaxMana;
                if (item.active == ItemActive.SupportWard)
                {
                    e.inventory.Add(new InventorySlot(3867));
                    e.Recalculate();
                }
                match.ui.OpenShop();
                match.ui.SelectCatalogItem(item.id);
                yield return null;
                int startGold = e.Gold, cost = e.Cost(item, out var used);
                var buy = Button("buy");
                bool restricted = !string.IsNullOrEmpty(item.requiredChampion);
                Check(buy, "Buy button exists for " + item.name);
                if (restricted)
                {
                    Check(!buy.interactable && !buy.Click() && !e.Owns(item.id), item.name + " is champion restricted");
                    coverage.Add(item.id + ",\"" + item.name + "\",restricted,not applicable,not applicable");
                    items++;
                    continue;
                }
                Check(buy.interactable && buy.Click(), item.name + " can be purchased through the shop button");
                Check(item.HasTag("Trinket") ? e.Trinket == item.id : e.Owns(item.id), item.name + " is actually owned");
                Near(startGold - e.Gold, cost, item.name + " purchase gold");
                var raw = riot["data"][item.id.ToString()];
                bool consume = item.consumable;
                Near(p.Health.maxHealth - baseHP, consume ? 0 : Stat(raw, "Health"), item.name + " Health");
                Near(p.Health.armor - baseAR, Stat(raw, "Armor"), item.name + " Armor");
                Near(p.Health.magicResistance - baseMR, Stat(raw, "Magic Resist"), item.name + " Magic Resist");
                float ap = Stat(raw, "Ability Power");
                if (item.id == 4633)
                    ap += Stat(raw, "Health") * .02f;
                if (item.id == 3089)
                    ap *= 1.3f;
                if (item.id == 3003)
                    ap += (baseMP + Stat(raw, "Mana")) * .01f;
                if (item.id == 6621)
                    ap += Stat(raw, "Base Mana Regen") * 10;
                Near(e.AbilityPower, consume ? 0 : ap, item.name + " Ability Power");
                float ad = Stat(raw, "Attack Damage");
                if (item.id == 2501)
                    ad += Stat(raw, "Health") * .02f;
                if (item.id == 3004)
                    ad += (baseMP + Stat(raw, "Mana")) * .025f;
                Near(p.tuning.attackDamage - baseAD, consume ? 0 : ad, item.name + " Attack Damage");
                Near(e.MaxMana - baseMP, consume ? 0 : Stat(raw, "Mana"), item.name + " Mana");
                float expectedInterval = baseAS * (1 + .0225f * 17) / (1 + .0225f * 17 + Stat(raw, "Attack Speed"));
                Near(p.tuning.attackInterval, expectedInterval, item.name + " Attack Speed");
                Near(e.CriticalChance, Stat(raw, "Critical Strike Chance"), item.name + " Critical Chance");
                Near(e.CriticalDamage, 1.75f + Stat(raw, "Critical Strike Damage"), item.name + " Critical Damage");
                Near(e.Lifesteal, Stat(raw, "Life Steal"), item.name + " Life Steal");
                Near(e.Omnivamp, Stat(raw, "Omnivamp"), item.name + " Omnivamp");
                Near(e.Tenacity, Stat(raw, "Tenacity"), item.name + " Tenacity");
                Near(e.AbilityHaste, Stat(raw, "Ability Haste") + (item.id == 2517 ? Stat(raw, "Attack Damage") * .3f : 0), item.name + " Ability Haste");
                Near(e.HealShieldPower, Stat(raw, "Heal and Shield Power") + (item.id == 6621 ? Stat(raw, "Base Mana Regen") * .02f : 0), item.name + " Heal/Shield Power");
                Near(e.MagicPen, Stat(raw, "Magic Penetration", false), item.name + " Flat Magic Penetration");
                Near(e.MagicPenPercent, Stat(raw, "Magic Penetration", true), item.name + " Percent Magic Penetration");
                Near(e.ArmorPen, Stat(raw, "Armor Penetration"), item.name + " Armor Penetration");
                Near(e.Lethality, Stat(raw, "Lethality"), item.name + " Lethality");
                float speed = Stat(raw, "Move Speed");
                Near(e.MoveSpeedBonus, Percent(raw, "Move Speed") ? speed : speed / 340, item.name + " Move Speed");
                float healthRegen = (float)typeof(RiftEconomy).GetField("healthRegen", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(e);
                float manaRegen = (float)typeof(RiftEconomy).GetField("manaRegen", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(e);
                Near(healthRegen, 1.7f * (1 + Stat(raw, "Base Health Regen")) + Stat(raw, "Health Regen per 5 seconds") / 5, item.name + " Health Regeneration");
                Near(manaRegen, 1.5f * (1 + Stat(raw, "Base Mana Regen")) + Stat(raw, "Mana Regen per 5 seconds") / 5, item.name + " Mana Regeneration");
                Near(item.goldPer10, Stat(raw, "Gold Per 10 Seconds"), item.name + " Gold Income");
                var statFields = new[] { "magicPen", "magicPenPercent", "armorPen", "lethality", "healthRegen", "manaRegen", "flatHealthRegen", "flatManaRegen", "goldPer10", "moveSpeed", "movePercent" };
                foreach (string field in statFields)
                    Check(float.IsFinite((float)typeof(LeagueItem).GetField(field).GetValue(item)), item.name + " finite " + field);
                match.ui.Close();
                float before = enemy.Health;
                enemy.TakeDamage(new DamageHit(p.Health, p.AttackOrigin, 100, DamageKind.Physical) { isBasicAttack = true });
                e.OnAttack(enemy, 100);
                enemy.TakeDamage(new DamageHit(p.Health, p.AttackOrigin, 50, DamageKind.Magic) { ability = "Q" });
                Check(enemy.Health < before && enemy.Health >= 0, item.name + " runtime attack/spell smoke");
                if (!item.HasTag("Trinket"))
                {
                    int index = e.inventory.FindIndex(s => s.id == item.id);
                    int gold = e.Gold;
                    bool sold = e.Sell(index);
                    Check(item.sell > 0 ? sold && e.Gold == gold + item.sell : !sold, item.name + " sale rules");
                }
                coverage.Add(item.id + ",\"" + item.name + "\",purchased,current base stats,attack+spell smoke");
                items++;
                yield return null;
            }
            foreach (var item in match.catalog.items.Where(i => !i.showInShop))
            {
                Reset();
                e.inventory.Add(new InventorySlot(item.id));
                e.Recalculate();
                Check(e.Owns(item.id) && p.Health.maxHealth > 0, "Automatic form stats: " + item.name);
                Check(e.CannotBuy(item) != null, "Automatic form is not directly sold: " + item.name);
            }
            Reset();
            e.Gold = 0;
            match.ui.OpenShop();
            match.ui.SelectCatalogItem(3157);
            yield return null;
            Check(!Button("buy").interactable && !Button("buy").Click(), "Unaffordable purchase button is disabled");
            Reset();
            for (int n = 0; n < 6; n++)
                e.inventory.Add(new InventorySlot(1028));
            e.Recalculate();
            Check(!e.Buy(1036), "Six-slot inventory rejects unrelated seventh item");
            Check(e.Buy(3084) && e.inventory.Count <= 6, "Upgrade consumes owned components in full inventory");
            Reset();
            Check(e.Buy(2003) && e.Buy(2003) && e.Buy(2003) && e.Buy(2003) && e.Buy(2003) && !e.Buy(2003), "Five Health Potion stack limit");
            Check(!e.Buy(2031), "Potion families are mutually exclusive");
            Reset();
            e.Level = 1;
            e.Recalculate();
            Check(!e.Buy(3363) && !e.Buy(2138), "Level-nine Farsight and elixir unlocks");
            e.CombatGold = 0;
            Check(!e.Buy(3171), "Quest boots are gated");
            Reset();
            e.SupportGold = 0;
            e.Buy(3865);
            e.AddGold(500, true);
            Check(e.Owns(3866), "Support quest first transformation");
            e.AddGold(500, true);
            Check(e.Owns(3867), "Support quest second transformation");
            foreach (var item in match.catalog.items.Where(i => i.active != ItemActive.None && string.IsNullOrEmpty(i.requiredChampion)))
            {
                Reset();
                if (item.active == ItemActive.SupportWard)
                    e.inventory.Add(new InventorySlot(3867));
                Check(e.Buy(item.id), "Acquire active " + item.name);
                fx.ResetEffects();
                match.ui.Close();
                yield return null;
                int slot = item.HasTag("Trinket") ? 6 : e.inventory.FindIndex(s => s.id == item.id);
                Check(rack.EquipSlot(slot, 1) && rack.HeldId(1) == item.id, "Physical equip: " + item.name);
                Check(RiftUI.BlocksCombat, "Held item prevents champion attack input: " + item.name);
                var target = item.active == ItemActive.Vow || item.active == ItemActive.Mikael ? ally : enemy;
                p.head.transform.rotation = Quaternion.LookRotation(target.AimPosition - p.head.transform.position);
                p.Health.SetHealth(p.Health.maxHealth - 500);
                ally.SetHealth(ally.maxHealth - 1000);
                enemy.ResetHealth();
                p.Health.ApplySlow(.4f, 5);
                ally.ApplySlow(.4f, 5);
                yield return null;
                if (item.id == 3157)
                    Capture("03-Hourglass-held");
                if (item.id == 2003)
                    Capture("04-Potion-held");
                float hBefore = p.Health.Health, allyBefore = ally.Health, enemyBefore = enemy.Health, shieldBefore = p.Health.Shield, manaBefore = e.Mana;
                int wards = RiftVisionWard.All.Count;
                Vector3 feet = p.Feet;
                Check(rack.UseHeld(1), "Held trigger activates: " + item.name);
                rack.ReturnHeld(1);
                if (item.activeCooldown > 0 && item.id != 3340 && item.id != 3364)
                    Check(fx.Cooldown(item.id) > 0, "Active starts cooldown: " + item.name);
                if (item.active == ItemActive.Potion)
                {
                    Check(!e.Owns(2003), "Health Potion is consumed");
                    yield return new WaitForSeconds(15.2f);
                    Near(p.Health.Health - hBefore, 120, "Health Potion heals 120 over 15 seconds", 2);
                }
                else if (item.active == ItemActive.Refillable)
                {
                    Check(fx.Charges(item.id) == 1, "Refillable spends one charge");
                    yield return new WaitForSeconds(12.2f);
                    Near(p.Health.Health - hBefore, 100, "Refillable heals 100 over 12 seconds", 2);
                }
                else if (item.active == ItemActive.Stasis || item.active == ItemActive.SingleStasis)
                {
                    Check(e.Stasis, "Stasis starts");
                    Near(p.Health.TakeDamage(new DamageHit(enemy, enemy.AimPosition, 100, DamageKind.True)), 0, "Stasis blocks damage");
                    if (item.id == 2420)
                        Check(e.Owns(2421) && !e.Owns(2420), "Seeker transforms to Shattered Armguard");
                    if (item.id == 3157)
                    {
                        yield return null;
                        Capture("03-Hourglass");
                    }
                    yield return new WaitForSeconds(2.6f);
                    Check(!e.Stasis, "Stasis expires at 2.5 seconds");
                }
                else if (item.active == ItemActive.ElixirIron)
                    Check(fx.BonusHP == 300 && !e.Owns(item.id), "Iron elixir grants 300 health");
                else if (item.active == ItemActive.ElixirSorcery)
                    Check(fx.BonusAP >= 50 && !e.Owns(item.id), "Sorcery elixir grants 50 AP");
                else if (item.active == ItemActive.ElixirWrath)
                    Check(fx.BonusAD == 30 && !e.Owns(item.id), "Wrath elixir grants 30 AD");
                else if (item.active == ItemActive.Ward || item.active == ItemActive.Farsight || item.active == ItemActive.SupportWard)
                {
                    Check(RiftVisionWard.All.Count > wards, "Active places ward: " + item.name);
                    if (item.id == 3340)
                        Check(fx.Charges(item.id) == 1, "Stealth Ward charge spent");
                }
                else if (item.active == ItemActive.Oracle)
                    Check(fx.Charges(item.id) == 1, "Oracle spends scanner charge");
                else if (item.active == ItemActive.Shurelya || item.active == ItemActive.Ghostblade)
                    Check(fx.BonusMove > 0, "Speed active changes locomotion speed: " + item.name);
                else if (item.active == ItemActive.Actualizer)
                    Check(fx.EmpoweredMana, "Actualizer empowerment starts");
                else if (item.active == ItemActive.Quicksilver || item.active == ItemActive.Mercurial)
                    Check(p.Health.SlowMultiplier == 1, "Self cleanse removes slow: " + item.name);
                else if (item.active == ItemActive.Mikael)
                    Check(ally.SlowMultiplier == 1 && ally.Health > allyBefore, "Mikael heals and cleanses aimed ally");
                else if (item.active == ItemActive.Randuin)
                    Check(enemy.SlowMultiplier <= .31f, "Randuin slows nearby enemy");
                else if (item.active == ItemActive.Locket)
                    Check(p.Health.Shield > shieldBefore && ally.Shield > 0, "Locket shields player and ally");
                else if (item.active == ItemActive.Redemption)
                {
                    yield return new WaitForSeconds(2.7f);
                    Check(ally.Health > allyBefore && enemy.Health < enemyBefore, "Redemption delayed heal and enemy true damage");
                }
                else if (item.active == ItemActive.Vow)
                    Check(fx.VowedAlly == ally, "Knight's Vow links aimed allied champion");
                else if (item.active == ItemActive.Titanic)
                {
                    float before = enemy.Health;
                    e.OnAttack(enemy, 1);
                    Check(enemy.Health < before, "Titanic empowers next attack");
                }
                else if (item.active == ItemActive.Rocketbelt)
                {
                    yield return null;
                    Check(Vector3.Distance(feet, p.Feet) > .1f || enemy.Health < enemyBefore, "Rocketbelt dash or forward rockets resolve");
                    match.MoveToFountain();
                }
                else
                    Check(enemy.Health < enemyBefore, "Damage active hits enemy: " + item.name);
                if (item.activeCooldown > 0 && !e.Stasis && e.Owns(item.id))
                    Check(!fx.Activate(item.id, p.AttackOrigin, p.AttackDirection), "Cooldown prevents immediate reuse: " + item.name);
                activeItems++;
                yield return null;
            }
            Reset();
            e.Buy(3157);
            yield return null;
            var right = p.rightHand;
            Vector3 hand = right.position;
            right.position = rack.SlotPosition(0);
            Check(rack.TryGrab(1) && rack.HeldId(1) == 3157, "Reach-and-grip finds nearest shoulder/hip slot");
            rack.ReturnHeld(1);
            Check(rack.HeldId(1) == 0, "Grip release returns item to its exact holster");
            right.position = hand;
            Reset();
            e.Buy(2031);
            yield return null;
            rack.EquipSlot(0, 1);
            p.head.transform.rotation = Quaternion.identity;
            Capture("04-Potion");
            rack.ReturnHeld(1);
            Reset();
            e.inventory.Add(new InventorySlot(3020));
            e.inventory.Add(new InventorySlot(3135));
            e.Recalculate();
            enemy.magicResistance = 100;
            float beforeHP = enemy.Health;
            float damage = enemy.TakeDamage(new DamageHit(p.Health, p.AttackOrigin, 100, DamageKind.Magic) { isItemEffect = true });
            Near(damage, Combatant.Mitigate(100, 100 * .6f - match.catalog.Find(3020).magicPen), "Flat and percent magic penetration reach damage calculation");
            enemy.magicResistance = 0;
            Reset();
            e.Buy(3115);
            float h = enemy.Health;
            e.OnAttack(enemy, 100);
            Check(enemy.Health < h, "Nashor on-hit resolves magic damage");
            Reset();
            e.Buy(3072);
            p.Health.ResetHealth();
            e.OnAttack(enemy, 100);
            Check(p.Health.Shield > 0, "Bloodthirster converts excess lifesteal to shield");
            Reset();
            e.Buy(3032);
            float crit = e.CriticalChance;
            e.OnAttack(enemy, 100);
            Check(e.CriticalChance > crit, "Yun Tal gains permanent crit on attack");
            Reset();
            e.Buy(3057);
            p.CastW();
            h = enemy.Health;
            e.OnAttack(enemy, 1);
            Check(enemy.Health < h, "Spellblade triggers following a champion cast");
            Reset();
            e.Buy(3026);
            p.Health.TakeDamage(new DamageHit(enemy, enemy.AimPosition, 100000, DamageKind.True));
            Check(p.Health.IsAlive && e.Stasis, "Guardian Angel prevents lethal damage");
            yield return new WaitForSeconds(4.2f);
            Check(!e.Stasis && p.Health.Health >= p.Health.maxHealth * .49f, "Guardian Angel returns at half health");
            Reset();
            e.Buy(3109);
            p.head.transform.rotation = Quaternion.LookRotation(ally.AimPosition - p.head.transform.position);
            fx.Activate(3109, p.AttackOrigin, p.AttackDirection);
            float playerHealth = p.Health.Health;
            float friendHealth = ally.Health;
            ally.TakeDamage(new DamageHit(enemy, enemy.AimPosition, 100, DamageKind.True));
            Near(friendHealth - ally.Health, 86, "Vow reduces ally damage by 14%");
            Near(playerHealth - p.Health.Health, 14, "Vow redirects damage to owner");
            Reset();
            e.Buy(3157);
            e.Buy(2031);
            e.Buy(3340);
            yield return null;
            match.ui.OpenShop();
            Capture("05-Shop-inventory");
            Check(match.ui.Buttons.All(b => b.GetComponent<BoxCollider>()), "All UI buttons have VR ray targets");
            var buttons = match.ui.Buttons.ToArray();
            bool overlapping = false;
            for (int i = 0; i < buttons.Length; i++)
                for (int j = i + 1; j < buttons.Length; j++)
                {
                    var a = buttons[i].GetComponent<RectTransform>();
                    var b = buttons[j].GetComponent<RectTransform>();
                    if (a.parent == b.parent)
                    {
                        var ar = new Rect(a.anchoredPosition.x, a.anchoredPosition.y - a.rect.height / 2, a.rect.width, a.rect.height);
                        var br = new Rect(b.anchoredPosition.x, b.anchoredPosition.y - b.rect.height / 2, b.rect.width, b.rect.height);
                        if (ar.Overlaps(br))
                            overlapping = true;
                    }
                }
            Check(!overlapping, "Shop button hit areas do not overlap");
            match.ui.Close();
            yield return null;
            Capture("06-Wrist-and-items");
            Check(errors.Count == 0, "No runtime errors or exceptions in the single comprehensive session");
            File.WriteAllText(Dir + "/RuntimeDevice.txt", $"XRSettings.isDeviceActive={XRSettings.isDeviceActive}\nHead device valid={InputDevices.GetDeviceAtXRNode(XRNode.Head).isValid}\nHMD/controller hardware interaction was not performed by the agent. Grab, held-use and release were exercised through the same runtime adapter.\n");
        }

        void Capture(string name)
        {
            camera.transform.SetPositionAndRotation(p.head.transform.position, p.head.transform.rotation);
            camera.cullingMask = p.head.cullingMask;
            camera.clearFlags = p.head.clearFlags;
            camera.backgroundColor = p.head.backgroundColor;
            var old = RenderTexture.active;
            camera.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Dir + "/" + name + ".png", tex.EncodeToPNG());
            RenderTexture.active = old;
            Object.Destroy(tex);
        }

        void Finish()
        {
            if (done)
                return;
            done = true;
            Application.logMessageReceived -= Message;
            var report = new StringBuilder();
            report.AppendLine("# Single comprehensive shop, items and VR UI test");
            report.AppendLine("\nDate: " + DateTime.Now.ToString("O"));
            report.AppendLine($"\nShop entries exercised: {items}. Compatible active entries exercised: {activeItems}. Assertions passed: {passes.Count}; failed: {failures.Count}; runtime errors: {errors.Count}.");
            report.AppendLine("\nThis is the only gameplay test for this task. Corrections after this report must not be followed by another gameplay test.");
            report.AppendLine("\n## Findings requiring fixes\n");
            foreach (string f in failures)
                report.AppendLine("- " + f);
            if (failures.Count == 0)
                report.AppendLine("No automated assertion failures.");
            report.AppendLine("\n## Runtime errors\n");
            foreach (string error in errors)
                report.AppendLine("```\n" + error + "\n```");
            report.AppendLine("\n## Coverage and limits\n\nEvery shop item: purchase/denial through the actual buy button, ownership, price, current Riot base stats, sale rules, and attack/spell runtime smoke. Automatic item forms are covered separately. Every compatible active: physical equip, held trigger dispatch, effect and cooldown. Special checks cover inventory recipes/capacity, potion healing duration, stasis, wards, cleanse, team support, penetration, selected passive procs and Guardian Angel.\n\nThis does not certify exact parity for every passive, role quest, fog-of-war behavior, item balance parameter or real headset comfort. Role quests use explicit prototype progression; active effects use the existing combat/status hooks. Hardware comfort requires a person wearing the headset.");
            File.WriteAllText(Dir + "/Single-Test-Report.md", report.ToString());
            File.WriteAllLines(Dir + "/Passed-Assertions.txt", passes);
            File.WriteAllLines(Dir + "/Item-Coverage.csv", new[] { "id,name,transaction,stats,runtime" }.Concat(coverage));
            if (camera)
            {
                camera.targetTexture = null;
                Object.Destroy(camera.gameObject);
            }
            if (rt)
            {
                rt.Release();
                Object.Destroy(rt);
            }
            EditorApplication.isPlaying = false;
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= Message;
        }
    }
}
