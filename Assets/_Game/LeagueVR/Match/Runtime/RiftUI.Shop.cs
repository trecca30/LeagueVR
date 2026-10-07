using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using LeagueVR.Champions;

namespace LeagueVR.Match
{
    /// <summary>
    /// League's item shop, rebuilt for VR after the live client: RECOMMENDED and ALL ITEMS tabs, role filters, the item
    /// grid in League's tiers (Starter, Consumables and Trinkets, Boots, Basic, Epic, Legendary), search with an
    /// on-panel keyboard, a detail panel with "builds into", the recipe tree (owned parts framed green), stats and
    /// passives, and the inventory bar with gold, Undo, Sell and Equip. Trigger selects; selecting an item twice buys it.
    /// </summary>
    public partial class RiftUI
    {
        const float ShopW = 1840, ShopH = 1120, Tile = 66, Pitch = 78, GridLeft = -712, GridTop = 446, GridBottom = -330, KeyboardTop = 100;
        const int Columns = 12;
        const float GridWidth = Columns * Pitch - (Pitch - Tile);
        const float DetailLeft = 300, DetailWidth = 600;
        static readonly Color ShopBack = new(.004f, .03f, .055f, 1), ShopLine = new(.47f, .38f, .2f, 1);
        static readonly Color ShopGold = new(.78f, .67f, .43f), ShopBright = new(.95f, .84f, .55f), ShopCream = new(.93f, .9f, .82f);
        static readonly Color ShopDim = new(.45f, .51f, .53f), ShopRed = new(.88f, .36f, .32f), ShopGreen = new(.36f, .86f, .5f);
        static readonly Color PurchaseColor = new(.40f, .31f, .12f, 1);

        enum ShopTab { Recommended, All }
        enum ItemRole { All, Fighter, Marksman, Assassin, Mage, Tank, Support }
        enum ItemTier { Starter, Consumable, Boots, Basic, Epic, Legendary }

        static readonly string[] TierNames = { "STARTER ITEMS", "CONSUMABLES & TRINKETS", "BOOTS", "BASIC ITEMS", "EPIC ITEMS", "LEGENDARY ITEMS" };
        static readonly string[] TierShort = { "STARTER", "CONSUMABLES", "BOOTS", "BASIC", "EPIC", "LEGENDARY" };
        static readonly int[] Consumables = { 2003, 2031, 2055, 3340, 3364, 3363 };
        static readonly ItemRole[] AllRoles = { ItemRole.Fighter, ItemRole.Marksman, ItemRole.Assassin, ItemRole.Mage, ItemRole.Tank, ItemRole.Support };

        /// <summary>League's starter items and the roles the shop lists them under.</summary>
        static readonly Dictionary<int, ItemRole[]> Starters = new()
        {
            [1054] = new[] { ItemRole.Fighter, ItemRole.Tank },
            [1055] = new[] { ItemRole.Fighter, ItemRole.Marksman, ItemRole.Assassin },
            [1056] = new[] { ItemRole.Mage, ItemRole.Support },
            [1082] = new[] { ItemRole.Mage, ItemRole.Assassin },
            [1083] = new[] { ItemRole.Marksman, ItemRole.Fighter },
            [1086] = new[] { ItemRole.Marksman },
            [1101] = AllRoles,
            [1102] = AllRoles,
            [1103] = AllRoles,
            [1120] = new[] { ItemRole.Tank, ItemRole.Fighter },
            [3865] = new[] { ItemRole.Support },
        };

        /// <summary>Per-champion recommendations in League's layout: starter, core build in order, boots, situational.</summary>
        static readonly Dictionary<ChampionId, (string title, int[] items, bool path)[]> Recommendations = new()
        {
            [ChampionId.Gwen] = new[] { ("STARTER", new[] { 1055, 2003 }, false), ("CORE BUILD", new[] { 3115, 4633, 3089 }, true), ("BOOTS", new[] { 3020, 3047, 3111 }, false), ("SITUATIONAL", new[] { 3157, 3135, 6653, 3102, 3065, 3053 }, false) },
            [ChampionId.Zoe] = new[] { ("STARTER", new[] { 1056, 2003 }, false), ("CORE BUILD", new[] { 6655, 4645, 3089 }, true), ("BOOTS", new[] { 3020, 3158 }, false), ("SITUATIONAL", new[] { 3157, 3135, 3102, 4646, 4628, 3100 }, false) },
            [ChampionId.Pantheon] = new[] { ("STARTER", new[] { 1055, 1054, 2003 }, false), ("CORE BUILD", new[] { 6692, 3071, 6694 }, true), ("BOOTS", new[] { 3047, 3111, 3158 }, false), ("SITUATIONAL", new[] { 3142, 3814, 3053, 6333, 3156, 3026 }, false) },
            [ChampionId.Aatrox] = new[] { ("STARTER", new[] { 1055, 2003 }, false), ("CORE BUILD", new[] { 6692, 6610, 3071 }, true), ("BOOTS", new[] { 3047, 3111 }, false), ("SITUATIONAL", new[] { 3053, 6333, 3065, 3156, 3026, 2501 }, false) },
            [ChampionId.Akshan] = new[] { ("STARTER", new[] { 1055, 2003 }, false), ("CORE BUILD", new[] { 6672, 3031, 3087 }, true), ("BOOTS", new[] { 3006 }, false), ("SITUATIONAL", new[] { 3036, 3072, 3139, 3026, 3095 }, false) },
            [ChampionId.Brand] = new[] { ("STARTER", new[] { 1056, 2003 }, false), ("CORE BUILD", new[] { 6653, 2503, 3116 }, true), ("BOOTS", new[] { 3020 }, false), ("SITUATIONAL", new[] { 3157, 3135, 3089, 3165 }, false) },
            [ChampionId.Yunara] = new[] { ("STARTER", new[] { 1055, 2003 }, false), ("CORE BUILD", new[] { 3153, 3124, 3302 }, true), ("BOOTS", new[] { 3006 }, false), ("SITUATIONAL", new[] { 3091, 3085, 3036, 3026 }, false) },
        };

        class ShopRow
        {
            public string header;
            public LeagueItem[] items;
            public bool path;
        }

        /// <summary>A grid tile kept so prices and affordability update in place when gold ticks.</summary>
        class ShopTile
        {
            public LeagueItem item;
            public TMP_Text price;
            public RawImage icon;
        }

        ShopTab shopTab = ShopTab.Recommended;
        ItemRole shopRole = ItemRole.All;
        string search = "";
        bool keyboardOpen;
        int scrollRow, selected, detailPage, inventorySelection = -1, lastClickId;
        float lastClickTime;
        string inventorySignature = "";
        int pricedGold = -1;
        RectTransform gridRoot, detailRoot, barRoot, keyboardRoot;
        TMP_Text shopGold, shopTooltip, buyReason, searchLabel;
        RiftUIButton buyButton;
        readonly List<ShopRow> shopRows = new();
        readonly List<ShopTile> tiles = new();
        readonly Dictionary<int, ItemRole[]> roleCache = new();
        Dictionary<int, LeagueItem[]> parentsOf;

        public int SelectedItem => selected;

        public void OpenShop()
        {
            if (!CanShop())
            {
                match.Notify("Recall to your own fountain to open the shop.");
                return;
            }
            var rack = match.economy.GetComponent<RiftItemRack>();
            if (rack)
            {
                rack.ReturnHeld(0);
                rack.ReturnHeld(1);
            }
            shopScreen = true;
            inventorySelection = -1;
            keyboardOpen = false;
            preservePose = false;
            if (match.catalog.Find(selected) == null)
                selected = NextRecommended();
            RenderShop();
        }

        /// <summary>Opens the shop on a given item.</summary>
        public void SelectCatalogItem(int id)
        {
            if (!CanShop())
                return;
            selected = id;
            detailPage = 0;
            inventorySelection = -1;
            RenderShop();
        }

        void RenderShop()
        {
            if (!CanShop())
            {
                Close();
                return;
            }
            Screen("Item shop", shopScreen && preservePose, false, ShopW, ShopH);
            shopScreen = true;
            inventorySignature = InventorySignature();
            pricedGold = match.economy.Gold;
            IndexCatalog();

            // Header: tabs, search, close.
            TabButton("RECOMMENDED", -900, ShopTab.Recommended, 250);
            TabButton("ALL ITEMS", -640, ShopTab.All, 210);
            var searchBox = Button(root, SearchText(), -410, 515, 430, 56, () =>
            {
                keyboardOpen = !keyboardOpen;
                RenderShop();
            }, "search");
            searchLabel = searchBox.label;
            searchLabel.fontSize = 21;
            searchLabel.alignment = TextAlignmentOptions.MidlineLeft;
            searchBox.selected = keyboardOpen || search.Length > 0;
            searchBox.Hover(false);
            if (search.Length > 0)
                Button(root, "×", 30, 515, 56, 56, () =>
                {
                    search = "";
                    scrollRow = 0;
                    RenderShop();
                }, "search-clear").label.fontSize = 34;
            Text(root, ChampionTitle(), 120, 515, 560, 50, 22, ShopDim);
            Button(root, "CLOSE", 770, 515, 130, 56, Close, "close").label.fontSize = 22;
            Fill(root, -920, 480, ShopW, 2, ShopLine);

            BuildShopRows();
            RenderLeftColumn();
            RenderGrid();
            RenderDetail();
            RenderBar();
            if (keyboardOpen)
                RenderKeyboard();
        }

        void TabButton(string label, float x, ShopTab tab, float width)
        {
            var b = Button(root, label, x, 515, width, 56, () =>
            {
                shopTab = tab;
                scrollRow = 0;
                search = "";
                keyboardOpen = false;
                RenderShop();
            }, "tab-" + tab);
            b.label.fontSize = 23;
            b.selected = shopTab == tab && search.Length == 0;
            b.Hover(false);
        }

        string SearchText() => search.Length > 0 ? "SEARCH:  " + search.ToUpperInvariant() + (keyboardOpen ? "_" : "") : keyboardOpen ? "TYPE TO SEARCH_" : "SEARCH  (name, ad, ap, crit, hp...)";

        ChampionDefinition ActiveChampion => match.player.GetComponent<ChampionRoster>()?.Active;

        string ChampionTitle()
        {
            var champion = ActiveChampion;
            return champion ? "ITEM SHOP  ·  " + champion.name.ToUpperInvariant() : "ITEM SHOP";
        }

        /// <summary>Role filters and tier jumps on All Items; the champion card on Recommended.</summary>
        void RenderLeftColumn()
        {
            if (search.Length > 0)
                return;
            if (shopTab == ShopTab.Recommended)
            {
                var champion = ActiveChampion;
                if (!champion)
                    return;
                Icon(root, champion.portrait, -900, 380, 120);
                Text(root, champion.name.ToUpperInvariant(), -900, 296, 172, 40, 26, champion.color);
                Text(root, champion.role, -900, 262, 172, 34, 19, ShopDim);
                Text(root, "Recommended items for your champion. The core build is shown in order.", -900, 170, 172, 140, 18, ShopDim);
                return;
            }
            var roles = (ItemRole[])Enum.GetValues(typeof(ItemRole));
            for (int r = 0; r < roles.Length; r++)
            {
                var value = roles[r];
                var b = Button(root, value.ToString().ToUpperInvariant(), -900, 430 - r * 64, 172, 56, () =>
                {
                    shopRole = value;
                    scrollRow = 0;
                    RenderShop();
                }, "role-" + value);
                b.label.fontSize = 21;
                b.selected = shopRole == value;
                b.Hover(false);
            }
            Text(root, "JUMP TO", -900, -12, 172, 30, 18, ShopDim);
            for (int t = 0; t < TierNames.Length; t++)
            {
                int row = shopRows.FindIndex(s => s.header == TierNames[t]);
                var b = Button(root, TierShort[t], -900, -52 - t * 46, 172, 40, () =>
                {
                    scrollRow = row;
                    RenderGrid();
                }, "jump-" + t, row >= 0);
                b.label.fontSize = 17;
            }
        }

        // ---------- Catalog organisation ----------

        void IndexCatalog()
        {
            if (parentsOf != null)
                return;
            var shopItems = match.catalog.items.Where(i => i.showInShop && i.recipe != null).ToArray();
            parentsOf = match.catalog.items.ToDictionary(i => i.id, i => shopItems.Where(p => p.recipe.Contains(i.id)).ToArray());
        }

        LeagueItem[] Parents(LeagueItem item) => parentsOf != null && parentsOf.TryGetValue(item.id, out var p) ? p : Array.Empty<LeagueItem>();

        /// <summary>Shop-visible for this player: not another champion's item, and quest upgrades only once unlocked.</summary>
        bool Allowed(LeagueItem item)
        {
            if (!item.showInShop || item.price <= 0 && !item.HasTag("Trinket"))
                return false;
            if (!string.IsNullOrEmpty(item.requiredChampion) && !string.Equals(item.requiredChampion, match.player.ChampionName, StringComparison.OrdinalIgnoreCase))
                return false;
            if ((item.questUpgrade || item.active == ItemActive.SupportWard) && !(item.recipe?.Any(match.economy.Owns) ?? false))
                return false;
            return true;
        }

        ItemTier TierOf(LeagueItem item)
        {
            if (Starters.ContainsKey(item.id) || item.active == ItemActive.SupportWard)
                return ItemTier.Starter;
            if (item.consumable || item.HasTag("Consumable") || item.HasTag("Trinket"))
                return ItemTier.Consumable;
            if (item.HasTag("Boots"))
                return ItemTier.Boots;
            if (item.recipe == null || item.recipe.Length == 0)
                return ItemTier.Basic;
            return Parents(item).Length > 0 ? ItemTier.Epic : ItemTier.Legendary;
        }

        /// <summary>
        /// League's shop roles. Finished items are classed by their stats; components are listed under every role
        /// their finished items serve, as in the client.
        /// </summary>
        ItemRole[] RolesOf(LeagueItem item)
        {
            if (roleCache.TryGetValue(item.id, out var cached))
                return cached;
            roleCache[item.id] = Array.Empty<ItemRole>();
            ItemRole[] result;
            if (Starters.TryGetValue(item.id, out var starter))
                result = starter;
            else if (TierOf(item) == ItemTier.Consumable)
                result = AllRoles;
            else if (Parents(item).Length > 0)
                result = Parents(item).SelectMany(p => RolesOf(p)).Distinct().OrderBy(r => r).ToArray();
            else
                result = ClassifyFinished(item);
            roleCache[item.id] = result;
            return result;
        }

        static ItemRole[] ClassifyFinished(LeagueItem item)
        {
            bool ad = item.HasTag("Damage"), ap = item.HasTag("SpellDamage"), crit = item.HasTag("CriticalStrike"), speed = item.HasTag("AttackSpeed");
            bool onHit = item.HasTag("OnHit"), lethality = item.HasTag("ArmorPenetration"), magicPen = item.HasTag("MagicPenetration"), move = item.HasTag("NonbootsMovement");
            bool health = item.HasTag("Health"), armor = item.HasTag("Armor"), resist = item.HasTag("SpellBlock") || item.HasTag("MagicResist");
            bool haste = item.HasTag("AbilityHaste") || item.HasTag("CooldownReduction"), sustain = item.HasTag("LifeSteal") || item.HasTag("SpellVamp");
            bool manaRegen = item.HasTag("ManaRegen"), mana = item.HasTag("Mana");
            bool enchanter = item.active is ItemActive.Redemption or ItemActive.Mikael or ItemActive.Locket or ItemActive.Shurelya or ItemActive.Vow or ItemActive.SupportWard
                || item.HasTag("Aura") && item.HasTag("Active")
                || item.HasTag("GoldPer") && !ad && !ap
                || manaRegen && ap && !mana && !magicPen && !ad;
            var roles = new List<ItemRole>();
            if (ad && !crit && (health || haste || sustain || onHit && !speed))
                roles.Add(ItemRole.Fighter);
            if (crit || speed && (ad || onHit))
                roles.Add(ItemRole.Marksman);
            if (!speed && !crit && (lethality || ad && move && !health))
                roles.Add(ItemRole.Assassin);
            if ((ap || magicPen) && !ad && !enchanter)
                roles.Add(ItemRole.Mage);
            if ((health || armor || resist) && !ad && !ap && !crit && !speed && !manaRegen)
                roles.Add(ItemRole.Tank);
            if (enchanter)
                roles.Add(ItemRole.Support);
            if (roles.Count == 0)
            {
                if (ad)
                    roles.Add(ItemRole.Fighter);
                if (ap || mana || manaRegen)
                    roles.Add(ItemRole.Mage);
                if (health || armor || resist)
                    roles.Add(ItemRole.Tank);
            }
            return roles.ToArray();
        }

        /// <summary>Search by name, or by stat shorthand like League's search box (ad, ap, crit, as, armor, mr, hp, ah...).</summary>
        bool Matches(LeagueItem item)
        {
            string q = search.Trim().ToLowerInvariant();
            if (q.Length == 0)
                return true;
            string name = item.name.ToLowerInvariant();
            if (name.Contains(q) || name.Replace("'", "").Contains(q))
                return true;
            string tag = q switch
            {
                "ad" or "attack" or "damage" => "Damage",
                "ap" or "ability power" => "SpellDamage",
                "crit" => "CriticalStrike",
                "as" or "attack speed" => "AttackSpeed",
                "armor" or "ar" => "Armor",
                "mr" or "magic resist" => "SpellBlock",
                "hp" or "health" => "Health",
                "ah" or "haste" or "cdr" => "AbilityHaste",
                "mana" => "Mana",
                "lethality" or "pen" or "arpen" => "ArmorPenetration",
                "magic pen" or "mpen" => "MagicPenetration",
                "lifesteal" or "ls" or "vamp" => "LifeSteal",
                "ms" or "speed" or "move" => "NonbootsMovement",
                "onhit" or "on hit" => "OnHit",
                "boots" => "Boots",
                "ward" or "vision" => "Vision",
                _ => null,
            };
            return tag != null && item.HasTag(tag);
        }

        void BuildShopRows()
        {
            shopRows.Clear();
            var all = match.catalog.items.Where(Allowed).ToArray();
            void Section(string title, IEnumerable<LeagueItem> items, bool path = false)
            {
                var list = items.ToArray();
                if (list.Length == 0)
                    return;
                if (path)
                    title += $"   <color=#7D8A8C>{list.Sum(i => i.price):N0}g total</color>";
                shopRows.Add(new ShopRow { header = title });
                for (int i = 0; i < list.Length; i += Columns)
                    shopRows.Add(new ShopRow { items = list.Skip(i).Take(Columns).ToArray(), path = path });
            }
            if (search.Length > 0)
            {
                Section("RESULTS FOR \"" + search.ToUpperInvariant() + "\"", all.Where(Matches).OrderBy(TierOf).ThenBy(i => i.price).ThenBy(i => i.name));
                return;
            }
            if (shopTab == ShopTab.Recommended)
            {
                var champion = ActiveChampion;
                if (champion && Recommendations.TryGetValue(champion.id, out var sets))
                    foreach (var (title, ids, path) in sets)
                        Section(title, ids.Select(match.catalog.Find).Where(i => i != null && Allowed(i)), path);
                Section("CONSUMABLES & TRINKETS", Consumables.Select(match.catalog.Find).Where(i => i != null && Allowed(i)));
                return;
            }
            foreach (ItemTier tier in Enum.GetValues(typeof(ItemTier)))
                Section(TierNames[(int)tier], all.Where(i => TierOf(i) == tier && (shopRole == ItemRole.All || RolesOf(i).Contains(shopRole))).OrderBy(i => i.price).ThenBy(i => i.name));
        }

        /// <summary>The first item of the champion's core build (then situational) the player does not own yet.</summary>
        int NextRecommended()
        {
            var champion = ActiveChampion;
            if (!champion || !Recommendations.TryGetValue(champion.id, out var sets))
                return 0;
            foreach (var (_, ids, _) in sets.Where(s => s.path).Concat(sets.Where(s => !s.path)))
                foreach (int id in ids)
                    if (!match.economy.Owns(id) && match.catalog.Find(id) != null)
                        return id;
            return 0;
        }

        // ---------- Item grid ----------

        static float RowHeight(ShopRow row) => row.header != null ? 42 : 104;

        float GridBottomEdge => keyboardOpen ? KeyboardTop + 10 : GridBottom;

        int VisibleRows(int from)
        {
            float height = 0;
            int count = 0;
            for (int r = from; r < shopRows.Count; r++)
            {
                height += RowHeight(shopRows[r]);
                if (height > GridTop - GridBottomEdge)
                    break;
                count++;
            }
            // A section title is never left alone at the bottom of the view.
            if (count > 1 && shopRows[from + count - 1].header != null)
                count--;
            return count;
        }

        void RenderGrid()
        {
            if (gridRoot)
                Destroy(gridRoot.gameObject);
            gridRoot = RectChild("Item grid", root);
            tiles.Clear();
            scrollRow = Mathf.Clamp(scrollRow, 0, Mathf.Max(0, shopRows.Count - 1));
            int visible = VisibleRows(scrollRow);
            float y = GridTop;
            for (int r = scrollRow; r < scrollRow + visible; r++)
            {
                var row = shopRows[r];
                if (row.header != null)
                {
                    Text(gridRoot, row.header, GridLeft, y - 20, GridWidth, 36, 22, ShopGold);
                    Fill(gridRoot, GridLeft, y - 40, GridWidth, 1, ShopLine);
                    y -= 42;
                    continue;
                }
                for (int c = 0; c < row.items.Length; c++)
                {
                    float x = GridLeft + c * Pitch;
                    ItemTile(gridRoot, row.items[c], x, y - Tile * .5f - 4, Tile);
                    if (row.path && c < row.items.Length - 1)
                        Text(gridRoot, "›", x + Tile, y - Tile * .5f - 4, Pitch - Tile, 40, 30, ShopGold).alignment = TextAlignmentOptions.Center;
                }
                y -= 104;
            }
            if (shopRows.Count == 0)
                Text(gridRoot, "No items match.", GridLeft, GridTop - 60, GridWidth, 50, 24, ShopDim);
            // Paging arrows beside the grid (the thumbsticks stay free for movement).
            float bottom = GridBottomEdge;
            bool more = scrollRow + visible < shopRows.Count;
            if (scrollRow > 0 || more)
                ScrollControls(visible, more, bottom);
            if (keyboardRoot)
                keyboardRoot.SetAsLastSibling();
        }

        void ScrollControls(int visible, bool more, float bottom)
        {
            Button(gridRoot, "▲", GridLeft + GridWidth + 10, GridTop - 50, 48, 100, () => ScrollShop(-Mathf.Max(1, visible - 1)), "scroll-up", scrollRow > 0);
            Button(gridRoot, "▼", GridLeft + GridWidth + 10, bottom + 50, 48, 100, () => ScrollShop(Mathf.Max(1, visible - 1)), "scroll-down", more);
            // A thin scroll bar between the arrows shows where the view is.
            float trackTop = GridTop - 110, trackBottom = bottom + 110, track = trackTop - trackBottom;
            if (track > 40)
            {
                float start = scrollRow / (float)shopRows.Count, end = Mathf.Min(1, (scrollRow + visible) / (float)shopRows.Count);
                Fill(gridRoot, GridLeft + GridWidth + 32, (trackTop + trackBottom) * .5f, 4, track, new Color(.1f, .14f, .17f, 1));
                Fill(gridRoot, GridLeft + GridWidth + 31, trackTop - track * (start + end) * .5f, 6, Mathf.Max(12, track * (end - start)), ShopGold);
            }
        }

        void ScrollShop(int amount)
        {
            // Never scroll past the point where the last row sits at the bottom of the view.
            int last = shopRows.Count - 1;
            while (last > 0 && last - 1 + VisibleRows(last - 1) >= shopRows.Count)
                last--;
            scrollRow = Mathf.Clamp(scrollRow + amount, 0, Mathf.Max(0, last));
            RenderGrid();
        }

        /// <summary>One item: its icon in a frame (gold when selected, green when owned), the price under it (red when unaffordable).</summary>
        void ItemTile(Transform parent, LeagueItem item, float x, float centreY, float size)
        {
            bool owned = match.economy.Owns(item.id) || match.economy.Trinket == item.id;
            if (owned)
                Fill(parent, x - 3, centreY, size + 6, size + 6, ShopGreen);
            var b = Button(parent, "", x, centreY, size, size, () => ClickItem(item.id), "item-" + item.id);
            b.selectedColor = ShopBright;
            b.selected = selected == item.id;
            b.Hover(false);
            var icon = TileIcon(b.transform, item.icon, size - 10);
            var price = Text(parent, "", x - 6, centreY - size * .5f - 13, size + 12, 24, 19, ShopBright);
            price.alignment = TextAlignmentOptions.Center;
            var tile = new ShopTile { item = item, price = price, icon = icon };
            Tint(tile, match.economy.Cost(item, out _));
            tiles.Add(tile);
        }

        void Tint(ShopTile tile, int cost)
        {
            bool affordable = match.economy.Gold >= cost;
            tile.price.text = cost.ToString();
            tile.price.color = affordable ? ShopBright : ShopRed;
            if (tile.icon)
                tile.icon.color = affordable ? Color.white : new Color(.5f, .5f, .5f, 1);
        }

        void ClickItem(int id)
        {
            // Selecting the same item twice in quick succession buys it (League buys on a right or double click).
            if (id == lastClickId && Time.time - lastClickTime < .5f && match.economy.CannotBuy(match.catalog.Find(id)) == null)
            {
                lastClickId = 0;
                match.economy.Buy(id);
                return;
            }
            lastClickId = id;
            lastClickTime = Time.time;
            selected = id;
            detailPage = 0;
            inventorySelection = -1;
            RenderDetail();
            RenderBar();
            if (gridRoot)
                foreach (var b in gridRoot.GetComponentsInChildren<RiftUIButton>())
                    if (b.key.StartsWith("item-"))
                    {
                        b.selected = b.key == "item-" + id;
                        b.Hover(b == hovered);
                    }
        }

        // ---------- Detail panel: builds into, recipe tree, stats, passives, purchase ----------

        void RenderDetail()
        {
            if (detailRoot)
                Destroy(detailRoot.gameObject);
            detailRoot = RectChild("Item detail", root);
            const float left = DetailLeft, width = DetailWidth, centre = left + width * .5f;
            Fill(detailRoot, left - 18, 25, 2, 830, ShopLine);
            var item = match.catalog.Find(selected);
            buyButton = null;
            buyReason = null;
            if (item == null)
            {
                Text(detailRoot, "Select an item to see what it does and how it builds.", left, 300, width, 120, 24, ShopDim);
                return;
            }
            // Builds into.
            var into = Parents(item).Where(Allowed).OrderBy(i => i.price).Take(9).ToArray();
            Text(detailRoot, into.Length > 0 ? "BUILDS INTO" : item.recipe?.Length > 0 ? "FINISHED ITEM" : "", left, 446, width, 30, 19, ShopDim);
            for (int n = 0; n < into.Length; n++)
                NodeButton(into[n], left + n * 64, 400, 50, false);
            // Recipe tree; components the player owns are framed green and count toward the price.
            var pool = match.economy.inventory.GroupBy(s => s.id).ToDictionary(g => g.Key, g => g.Sum(s => s.count));
            float lowest = TreeNode(item, centre, into.Length > 0 ? 300 : 380, width, 0, pool);
            // Name, cost, stats and passive text.
            float nameY = Mathf.Min(220, lowest - 30);
            int cost = match.economy.Cost(item, out _);
            Text(detailRoot, item.name, left, nameY, width, 46, 31, ShopBright);
            Text(detailRoot, cost < item.price ? $"{cost}g  <color=#7D8A8C>({item.price}g total, owned parts used)</color>" : $"{item.price}g" + (item.sell > 0 ? $"  <color=#7D8A8C>·  sells for {item.sell}g</color>" : ""), left, nameY - 38, width, 32, 22, ShopGold);
            float bodyTop = nameY - 62, bodyBottom = -232;
            var body = Text(detailRoot, "", left, (bodyTop + bodyBottom) * .5f, width, bodyTop - bodyBottom, 20, ShopCream);
            body.alignment = TextAlignmentOptions.TopLeft;
            body.overflowMode = TextOverflowModes.Truncate;
            var pages = Pages(FormatItemText(item), body, width, bodyTop - bodyBottom);
            detailPage = Mathf.Clamp(detailPage, 0, pages.Length - 1);
            body.text = pages[detailPage];
            if (pages.Length > 1)
                Button(detailRoot, $"MORE  {detailPage + 1}/{pages.Length}", left + width - 170, -250, 170, 34, () =>
                {
                    detailPage = (detailPage + 1) % pages.Length;
                    RenderDetail();
                }, "detail-more").label.fontSize = 18;
            buyButton = Button(detailRoot, "PURCHASE", left, -302, width, 60, () => match.economy.Buy(selected), "buy");
            buyButton.label.fontSize = 26;
            buyButton.selectedColor = PurchaseColor;
            buyReason = Text(detailRoot, "", left, -352, width, 32, 19, ShopGold);
            buyReason.alignment = TextAlignmentOptions.Center;
            UpdateBuyState();
        }

        /// <summary>Stats in blue, then each passive with its name in gold (League's tooltip layout), then the VR adaptation.</summary>
        static string FormatItemText(LeagueItem item)
        {
            string stats = (item.statText ?? "").Trim();
            string passive = (item.description ?? "").Trim();
            if (stats.Length > 0 && passive.StartsWith(stats, StringComparison.Ordinal))
                passive = passive.Substring(stats.Length).Trim();
            var text = new System.Text.StringBuilder();
            foreach (var line in stats.Split('\n'))
                if (line.Trim().Length > 0)
                    text.Append("<color=#9ED6E5>").Append(line.Trim()).Append("</color>\n");
            foreach (var block in passive.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var lines = block.Trim().Split('\n');
                if (lines[0].Trim().Length == 0)
                    continue;
                text.Append('\n');
                if (lines.Length > 1 && lines[0].Length < 40)
                    text.Append("<color=#E8CC8A>").Append(lines[0].Trim()).Append("</color>\n").Append(string.Join("\n", lines.Skip(1)).Trim()).Append('\n');
                else
                    text.Append(block.Trim()).Append('\n');
            }
            if (!string.IsNullOrWhiteSpace(item.effectNotes))
                text.Append("\n<color=#6FB7BF>VR: ").Append(item.effectNotes.Trim()).Append("</color>");
            return text.ToString().Trim();
        }

        /// <summary>A recipe tree node and its components below it, with connectors. Returns the lowest y it drew.</summary>
        float TreeNode(LeagueItem item, float x, float y, float span, int depth, Dictionary<int, int> pool)
        {
            bool owned = depth > 0 && pool.TryGetValue(item.id, out int count) && count > 0;
            if (owned)
                pool[item.id]--;
            float size = depth == 0 ? 60 : depth == 1 ? 50 : 44;
            NodeButton(item, x - size * .5f, y, size, owned);
            float lowest = y - size * .5f - 22;
            if (owned || depth >= 2 || item.recipe == null || item.recipe.Length == 0)
                return lowest;
            var parts = item.recipe.Select(match.catalog.Find).Where(p => p != null).ToArray();
            if (parts.Length == 0)
                return lowest;
            float childY = y - (depth == 0 ? 100 : 88);
            float childSize = depth == 0 ? 50 : 44;
            float slot = span / parts.Length;
            float firstX = x - span * .5f + slot * .5f, lastX = x + span * .5f - slot * .5f;
            // Connectors: down from under the price, across, and down to each part.
            float from = y - size * .5f - 22, to = childY + childSize * .5f, mid = (from + to) * .5f;
            Fill(detailRoot, x - 1, (from + mid) * .5f, 2, from - mid, ShopLine);
            if (parts.Length > 1)
                Fill(detailRoot, firstX - 1, mid, lastX - firstX + 2, 2, ShopLine);
            for (int p = 0; p < parts.Length; p++)
            {
                float px = firstX + slot * p;
                Fill(detailRoot, px - 1, (mid + to) * .5f, 2, mid - to, ShopLine);
                lowest = Mathf.Min(lowest, TreeNode(parts[p], px, childY, slot, depth + 1, pool));
            }
            return lowest;
        }

        void NodeButton(LeagueItem item, float x, float y, float size, bool owned)
        {
            if (owned)
                Fill(detailRoot, x - 3, y, size + 6, size + 6, ShopGreen);
            var b = Button(detailRoot, "", x, y, size, size, () => ClickItem(item.id), "node-" + item.id);
            b.selectedColor = ShopBright;
            b.selected = item.id == selected;
            b.Hover(false);
            TileIcon(b.transform, item.icon, size - 8);
            var price = Text(detailRoot, owned ? "OWNED" : match.economy.Cost(item, out _).ToString(), x - 10, y - size * .5f - 11, size + 20, 20, owned ? 14 : 16, owned ? ShopGreen : ShopGold);
            price.alignment = TextAlignmentOptions.Center;
        }

        void UpdateBuyState()
        {
            if (!buyButton)
                return;
            var item = match.catalog.Find(selected);
            string reason = match.economy.CannotBuy(item);
            int cost = item != null ? match.economy.Cost(item, out _) : 0;
            buyButton.interactable = reason == null;
            buyButton.selected = reason == null;
            buyButton.label.text = reason == null ? $"PURCHASE  ·  {cost}g" : "PURCHASE";
            buyButton.Hover(hovered == buyButton);
            if (buyReason)
                buyReason.text = reason ?? "Tip: select an item twice to buy it.";
        }

        // ---------- Bottom bar: inventory, undo, sell, equip, gold ----------

        void RenderBar()
        {
            if (barRoot)
                Destroy(barRoot.gameObject);
            barRoot = RectChild("Inventory bar", root);
            Fill(barRoot, -920, -392, ShopW, 2, ShopLine);
            var economy = match.economy;
            for (int n = 0; n < 7; n++)
            {
                int index = n;
                bool trinket = n == 6;
                var slot = !trinket && n < economy.inventory.Count ? economy.inventory[n] : null;
                int id = trinket ? economy.Trinket : slot?.id ?? 0;
                var item = match.catalog.Find(id);
                float x = -900 + n * 84 + (trinket ? 14 : 0);
                var b = Button(barRoot, "", x, -455, 72, 72, () =>
                {
                    inventorySelection = index;
                    selected = id;
                    detailPage = 0;
                    RenderDetail();
                    RenderBar();
                }, "slot-" + n, item != null);
                b.selectedColor = ShopBright;
                b.selected = inventorySelection == n;
                b.Hover(false);
                if (item != null)
                    TileIcon(b.transform, item.icon, 64);
                if (slot != null && slot.count > 1)
                    Text(b.transform, slot.count.ToString(), 2, -22, 32, 24, 19, ShopCream).alignment = TextAlignmentOptions.Right;
            }
            Text(barRoot, "TRINKET", -900 + 6 * 84 + 14 - 6, -505, 84, 22, 14, ShopDim).alignment = TextAlignmentOptions.Center;
            int selectedId = inventorySelection == 6 ? economy.Trinket : inventorySelection >= 0 && inventorySelection < economy.inventory.Count ? economy.inventory[inventorySelection].id : 0;
            var selectedSlot = match.catalog.Find(selectedId);
            Button(barRoot, "UNDO", -280, -455, 170, 64, () => match.economy.Undo(), "undo", economy.CanUndo).label.fontSize = 23;
            bool sellable = selectedSlot != null && inventorySelection < 6 && selectedSlot.sell > 0;
            Button(barRoot, sellable ? $"SELL  ·  {selectedSlot.sell}g" : "SELL", -96, -455, 250, 64, () =>
            {
                if (inventorySelection >= 0 && match.economy.Sell(inventorySelection))
                    inventorySelection = -1;
            }, "sell", sellable).label.fontSize = 23;
            // Items are worn on the body. In VR they are taken by reaching to the slot and holding grip (a held item
            // is holstered as soon as grip is released), so the bar says where it is; on desktop EQUIP puts it in hand.
            var rack = match.economy.GetComponent<RiftItemRack>();
            int equipIndex = inventorySelection;
            if (match.player.DesktopMode)
                Button(barRoot, "EQUIP", 168, -455, 180, 64, () =>
                {
                    Close();
                    if (rack)
                        rack.EquipSlot(equipIndex, 1);
                }, "equip", selectedSlot != null && selectedSlot.active != ItemActive.None).label.fontSize = 23;
            else if (selectedSlot != null && rack && equipIndex < rack.SlotNames.Length)
                Text(barRoot, $"Worn on your <color=#E8CC8A>{rack.SlotNames[equipIndex].ToLowerInvariant()}</color>\nreach there and hold grip", 168, -455, 260, 64, 18, ShopDim);
            shopGold = Text(barRoot, "", 420, -455, 480, 64, 40, ShopBright);
            shopGold.alignment = TextAlignmentOptions.MidlineRight;
            shopTooltip = Text(barRoot, "", -900, -530, 1800, 30, 19, ShopDim);
            UpdateGold();
        }

        void UpdateGold()
        {
            if (shopGold)
                shopGold.text = $"<color=#C8AA6E><voffset=2>●</voffset></color> {match.economy.Gold:N0}";
        }

        // ---------- On-panel keyboard for search ----------

        void RenderKeyboard()
        {
            keyboardRoot = RectChild("Search keyboard", root);
            Fill(keyboardRoot, GridLeft - 10, -110, GridWidth + 20, 420, new Color(.01f, .05f, .08f, .98f));
            Fill(keyboardRoot, GridLeft - 10, KeyboardTop, GridWidth + 20, 2, ShopLine);
            string[] keys = { "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM" };
            for (int r = 0; r < keys.Length; r++)
                for (int k = 0; k < keys[r].Length; k++)
                {
                    char key = keys[r][k];
                    Button(keyboardRoot, key.ToString(), GridLeft + 20 + r * 40 + k * 90, 40 - r * 86, 80, 76, () => TypeKey(char.ToLowerInvariant(key)), "key-" + key).label.fontSize = 30;
                }
            Button(keyboardRoot, "SPACE", GridLeft + 160, -218, 360, 70, () => TypeKey(' '), "key-space");
            Button(keyboardRoot, "← DEL", GridLeft + 540, -218, 140, 70, () => TypeKey('\b'), "key-back");
            Button(keyboardRoot, "DONE", GridLeft + 700, -218, 200, 70, () =>
            {
                keyboardOpen = false;
                RenderShop();
            }, "key-done");
        }

        /// <summary>Types into the search box. Only the results and the box redraw, so typing stays smooth.</summary>
        void TypeKey(char key)
        {
            bool wasEmpty = search.Length == 0;
            if (key == '\b')
                search = search.Length > 0 ? search.Substring(0, search.Length - 1) : search;
            else if (key == ' ' ? search.Length > 0 && !search.EndsWith(" ") && search.Length < 20 : search.Length < 20)
                search += key;
            scrollRow = 0;
            if (wasEmpty != (search.Length == 0))
            {
                RenderShop();
                return;
            }
            BuildShopRows();
            RenderGrid();
            if (searchLabel)
                searchLabel.text = SearchText();
        }

        // ---------- Per frame ----------

        string InventorySignature() => string.Join(",", match.economy.inventory.Select(s => s.id + "x" + s.count)) + "|" + match.economy.Trinket + "|" + match.economy.CanUndo;

        /// <summary>
        /// Keeps the shop current without rebuilding it on every passive gold tick: gold, prices and the purchase button
        /// update in place; only a changed inventory (buy, sell, undo) redraws the panel.
        /// </summary>
        void ShopTick()
        {
            if (InventorySignature() != inventorySignature)
            {
                if (inventorySelection >= match.economy.inventory.Count && inventorySelection != 6)
                    inventorySelection = -1;
                RenderShop();
                return;
            }
            if (pricedGold != match.economy.Gold)
            {
                pricedGold = match.economy.Gold;
                UpdateGold();
                foreach (var tile in tiles)
                    if (tile.price)
                        Tint(tile, match.economy.Cost(tile.item, out _));
                UpdateBuyState();
            }
            if (shopTooltip)
            {
                LeagueItem hoveredItem = null;
                if (hovered && (hovered.key.StartsWith("item-") || hovered.key.StartsWith("node-")) && int.TryParse(hovered.key.Substring(5), out int id))
                    hoveredItem = match.catalog.Find(id);
                shopTooltip.text = hoveredItem != null ? $"<color=#E8CC8A>{hoveredItem.name}</color>   {match.economy.Cost(hoveredItem, out _)}g   <color=#9ED6E5>{StatLine(hoveredItem.statText)}</color>" : Notice;
            }
            var mouse = Mouse.current;
            if (match.player.DesktopMode && mouse != null)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > .1f)
                    ScrollShop(wheel > 0 ? -1 : 1);
            }
        }

        static string StatLine(string text) => string.IsNullOrEmpty(text) ? "" : string.Join("  ·  ", text.Split('\n').Where(l => l.Trim().Length > 0).Select(l => l.Trim()));

        void ShopClosed()
        {
            gridRoot = detailRoot = barRoot = keyboardRoot = null;
            shopGold = shopTooltip = buyReason = searchLabel = null;
            buyButton = null;
            tiles.Clear();
        }

        // ---------- Small helpers ----------

        static RectTransform RectChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        /// <summary>An item icon centred on a button.</summary>
        static RawImage TileIcon(Transform button, Texture texture, float size)
        {
            var rect = new GameObject("Original Riot item icon", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5;
            rect.SetParent(button, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = Vector2.zero;
            var image = rect.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            RiftUIVisuals.Graphic(image);
            return image;
        }
    }
}
