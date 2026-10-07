using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
namespace LeagueVR.Match
{
    public class RiftUIButton : MonoBehaviour
    {
        public Action action; public Image image; public TMP_Text label; public bool interactable=true, selected;
        public string key;
        public void Hover(bool hover)
        {
            if(image) image.color=!interactable?new Color(.065f,.08f,.095f,.95f):hover?new Color(.13f,.35f,.39f,1):selected?new Color(.10f,.24f,.28f,1):new Color(.045f,.09f,.13f,.98f);
            if(label)label.color=interactable?new Color(.86f,.93f,.94f):new Color(.46f,.52f,.55f);
        }
        public bool Click(){if(!interactable)return false;action?.Invoke();return true;}
    }
    public class RiftUI : MonoBehaviour
    {
        public RiftMatch match;
        public Texture2D gwenPortrait;
        public bool IsOpen=>panel;
        public bool ShopIsOpen=>IsOpen&&shopScreen;
        public string CurrentScreen {get;private set;}
        public int SelectedItem=>selected;
        public IEnumerable<RiftUIButton> Buttons=>panel?panel.GetComponentsInChildren<RiftUIButton>():Array.Empty<RiftUIButton>();
        public static bool BlocksCombat=>RiftMatch.Instance&&(!RiftMatch.Instance.Running||RiftMatch.Instance.ui.IsOpen||RiftMatch.Instance.economy.Stasis||RiftItemRack.Holding||RiftItemRack.InputConsumedThisFrame);
        GameObject panel;RectTransform root;TMP_Text status,goldText,buyReason;RiftUIButton hovered,buyButton;LineRenderer ray;InputAction shop,menu,trigger,recall;
        bool shopScreen;int category,page,selected,detailPage,inventorySelection=-1;
        string[] categories={"Recommended","All","Attack","Magic","Defense","Boots","Vision","Actives"};
        string notice="";float noticeUntil;int renderedVersion;float refreshAt;
        GwenAvatar menuAvatar;bool weaponHidden,weaponWasActive;
        Vector3 lastPosition;Quaternion lastRotation;bool preservePose;
        readonly Color gold=new(.90f,.77f,.46f),white=new(.85f,.92f,.94f),muted=new(.55f,.68f,.72f),cyan=new(.30f,.90f,.91f);
        void Awake()
        {
            shop=new InputAction("Shop",binding:"<XRController>{LeftHand}/secondaryButton");menu=new InputAction("Menu",binding:"<XRController>{RightHand}/primary2DAxisClick");trigger=new InputAction("Select",binding:"<XRController>{RightHand}/triggerPressed");recall=new InputAction("Recall",binding:"<XRController>{LeftHand}/primary2DAxisClick");
            shop.Enable();menu.Enable();trigger.Enable();recall.Enable();
        }
        void Start()
        {
            match.Announcement+=Announce;
            var go=new GameObject("VR menu pointer");ray=go.AddComponent<LineRenderer>();ray.sharedMaterial=match.blueMaterial;ray.startWidth=.003f;ray.endWidth=.0015f;ray.positionCount=2;ray.enabled=false;
            if(!GetComponent<RiftVRHUD>())gameObject.AddComponent<RiftVRHUD>().match=match;
        }
        void Announce(string value){notice=value;noticeUntil=Time.time+4;}
        public string Notice=>Time.time<noticeUntil?notice:"";
        void OnDestroy(){if(match)match.Announcement-=Announce;shop?.Dispose();menu?.Dispose();trigger?.Dispose();recall?.Dispose();Close();if(ray)Destroy(ray.gameObject);}
        void Update()
        {
            var keyboard=Keyboard.current;bool desktop=match.player.DesktopMode;
            if(shop.WasPressedThisFrame()||(desktop&&keyboard!=null&&keyboard.pKey.wasPressedThisFrame)){if(ShopIsOpen)Close();else OpenShop();}
            if(menu.WasPressedThisFrame()||(desktop&&keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame)){if(IsOpen&&match.Running)Close();else OpenMenu();}
            if(recall.WasPressedThisFrame()||(desktop&&keyboard!=null&&keyboard.bKey.wasPressedThisFrame)){Close();match.Recall();}
            if(ShopIsOpen&&!CanShop()){Close();match.Notify("Shop closed: return to your own fountain.");}
            if(ray)ray.enabled=IsOpen&&!desktop;
            if(!IsOpen)return;
            if(goldText)goldText.text=$"{match.economy.Gold:N0} GOLD     LV {match.economy.Level}     OWN FOUNTAIN";
            if(status)status.text=Notice;
            if(shopScreen&&inventorySelection<0)
            {
                var item=match.catalog.Find(selected);string reason=match.economy.CannotBuy(item);
                if(buyReason)buyReason.text=reason??"Ready to purchase. Owned components reduce the price.";
                if(buyButton){buyButton.interactable=reason==null;buyButton.label.text="BUY  "+match.economy.Cost(item,out var used)+"g";buyButton.Hover(hovered==buyButton);}
                if(renderedVersion!=match.economy.Version&&Time.time>=refreshAt){refreshAt=Time.time+.35f;RenderShop();return;}
            }
            Ray pointer=desktop&&Mouse.current!=null?match.player.head.ScreenPointToRay(Mouse.current.position.ReadValue()):new Ray(match.player.rightHand.position,match.player.rightHand.forward);
            var hit=Physics.RaycastAll(pointer,8,1<<5,QueryTriggerInteraction.Collide).OrderBy(h=>h.distance).Select(h=>new{hit=h,button=h.collider.GetComponent<RiftUIButton>()}).FirstOrDefault(h=>h.button&&h.button.gameObject.activeInHierarchy);
            var button=hit?.button;if(hovered&&hovered!=button)hovered.Hover(false);hovered=button;if(hovered)hovered.Hover(true);
            if(ray){ray.SetPosition(0,pointer.origin);ray.SetPosition(1,hit!=null?hit.hit.point:pointer.GetPoint(2.3f));}
            bool pressed=desktop?Mouse.current!=null&&Mouse.current.leftButton.wasPressedThisFrame:trigger.WasPressedThisFrame();if(pressed&&button)button.Click();
        }
        bool CanShop()=>match.Running&&match.player.Health.IsAlive&&!match.economy.Stasis&&match.AtShop;
        void Screen(string title,bool stable=false)
        {
            bool retained=stable&&panel;
            Vector3 position=retained?panel.transform.position:lastPosition;Quaternion rotation=retained?panel.transform.rotation:lastRotation;
            bool reuse=retained||(stable&&preservePose);
            Close();CurrentScreen=title;
            var rack=match.economy.GetComponent<RiftItemRack>();if(rack){rack.ReturnHeld(0);rack.ReturnHeld(1);}
            menuAvatar=match.player.GetComponent<GwenAvatar>();if(menuAvatar&&menuAvatar.scissorsRoot){weaponWasActive=menuAvatar.scissorsRoot.gameObject.activeSelf;menuAvatar.scissorsRoot.gameObject.SetActive(false);weaponHidden=true;}
            GetComponent<RiftVRHUD>()?.HideForMenu();
            panel=new GameObject(title,typeof(RectTransform),typeof(Canvas));root=panel.GetComponent<RectTransform>();root.sizeDelta=new Vector2(1700,1100);root.localScale=Vector3.one*.0012f;
            var forward=Vector3.ProjectOnPlane(match.player.head.transform.forward,Vector3.up).normalized;if(forward.sqrMagnitude<.1f)forward=Vector3.forward;
            if(!reuse){position=match.player.head.transform.position+forward*2.1f-Vector3.up*.08f;rotation=Quaternion.LookRotation(forward);}
            panel.transform.SetPositionAndRotation(position,rotation);lastPosition=position;lastRotation=rotation;preservePose=true;
            var canvas=panel.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=match.player.head;canvas.sortingOrder=40;
            Fill(root,-850,0,1700,1100,new Color(.009f,.018f,.031f,1));Fill(root,-850,543,1700,5,cyan);
            Text(root,title.ToUpperInvariant(),-790,475,1450,75,42,gold);
            Text(root,"SUMMONER'S RIFT  /  CHAMPION VR",-790,420,1490,35,19,muted);
            status=Text(root,Notice,-790,-493,1560,65,25,gold);
        }
        public void OpenMenu()=>ChampionMenu(false);
        void ChampionMenu(bool stable)
        {
            shopScreen=false;if(!stable)preservePose=false;Screen("League VR",stable);
            var roster=match.player.GetComponent<LeagueVR.Champions.ChampionRoster>();
            if(!roster||roster.champions==null||roster.champions.Length==0){Button(root,"PLAY",-300,0,600,100,()=>match.Play(),"play");return;}
            var chosen=roster.Selected;
            Text(root,"CHOOSE YOUR CHAMPION",-790,367,770,45,27,cyan);
            for(int i=0;i<roster.champions.Length;i++)
            {
                int index=i;var d=roster.champions[i];float x=-790+(i%3)*255,y=252-(i/3)*150;
                var card=Button(root,d.name.ToUpperInvariant(),x,y,238,135,()=>{roster.Select(index);ChampionMenu(true);},"champion-"+d.id);
                card.selected=i==roster.selected;card.Hover(false);card.label.rectTransform.anchoredPosition=new Vector2(7,-43);card.label.rectTransform.sizeDelta=new Vector2(224,37);card.label.fontSize=24;
                Icon(card.transform,d.portrait,77,21,84);
                if(card.selected)Fill(card.transform,-119,64,238,4,d.color);
            }
            Icon(root,chosen.portrait,55,320,106);
            Text(root,chosen.name.ToUpperInvariant(),187,353,560,65,45,chosen.color);
            Text(root,chosen.title+"  /  "+chosen.role,187,295,560,52,23,muted);
            Text(root,chosen.passiveName,55,222,700,40,27,gold);
            Text(root,chosen.passiveDescription,55,174,700,63,23,white);
            for(int i=0;i<4;i++)
            {
                float y=84-i*63;var spell=chosen.spells[i];Icon(root,spell.icon,55,y,45);
                Text(root,"QWER"[i]+"  "+spell.name,121,y,625,55,24,white);
            }
            Button(root,"ABILITY GUIDE",55,-191,700,63,ChampionGuide,"champion-guide");
            Text(root,match.Running?"Current match: "+match.player.ChampionName+". Selection applies to your next match.":"10,000 starting gold  /  Physical items  /  OpenXR VR",-790,-225,775,77,23,muted);
            Button(root,match.Running?"RESUME "+match.player.ChampionName.ToUpperInvariant():"PLAY AS "+chosen.name.ToUpperInvariant(),55,-313,700,85,()=>{if(match.Running)Close();else match.Play();},"play");
            Button(root,"CONTROLS",-790,-315,365,78,Controls,"controls");Button(root,"COMFORT & AUDIO",-400,-315,365,78,Settings,"settings");
            Button(root,"ITEM SHOP",-790,-416,365,76,OpenShop,"shop",CanShop());Button(root,"NEW MATCH",-400,-416,365,76,ConfirmNewMatch,"new",match.Running);
            Text(root,"Right-hand ray + trigger to select. Left stick: recall. Right stick: menu.",55,-413,700,80,23,muted);
        }
        void ChampionGuide()
        {
            shopScreen=false;Screen("Champion ability guide",true);var d=match.player.GetComponent<LeagueVR.Champions.ChampionRoster>().Selected;
            Text(root,d.name+"  /  "+d.passiveName+" — "+d.passiveDescription,-790,334,1560,89,25,d.color);
            for(int i=0;i<4;i++)
            {
                float x=-790+(i%2)*800,y=181-(i/2)*245;Icon(root,d.spells[i].icon,x,y+41,55);
                Text(root,"QWER"[i]+"  "+d.spells[i].name,x+73,y+41,675,68,28,gold);
                Text(root,d.spells[i].vrDescription,x,y-61,749,149,25,white);
            }
            Text(root,"Aim with the casting hand. Head and wrist tracking stay under your control.\nAbilities are available from the start in this prototype.",-790,-345,1560,85,24,muted);
            Button(root,"BACK",-220,-430,440,65,()=>ChampionMenu(true),"back");
        }
        void ConfirmNewMatch()
        {
            shopScreen=false;Screen("Start a new match?",true);Text(root,"This starts as your selected champion and resets inventory, gold and structures.",-730,180,1430,160,32,white);
            Button(root,"START NEW MATCH",-590,-100,530,90,()=>match.Play());Button(root,"KEEP PLAYING",80,-100,530,90,OpenMenu);
        }
        void Settings()
        {
            shopScreen=false;Screen("Comfort & audio",true);
            Text(root,"GAME VOLUME",-720,270,1400,65,32,cyan);Text(root,$"{AudioListener.volume*100:0}%  ·  {(!AudioListener.pause?"Audio enabled":"Audio paused")}",-720,190,1400,60,27,white);
            Button(root,"QUIETER",-720,90,400,80,()=>{AudioListener.pause=false;AudioListener.volume=Mathf.Max(0,AudioListener.volume-.1f);Settings();});Button(root,"LOUDER",-270,90,400,80,()=>{AudioListener.pause=false;AudioListener.volume=Mathf.Min(1,AudioListener.volume+.1f);Settings();});Button(root,AudioListener.volume>0?"MUTE":"UNMUTE",180,90,400,80,()=>{AudioListener.pause=false;AudioListener.volume=AudioListener.volume>0?0:.7f;Settings();});
            Text(root,"WRIST DISPLAY",-720,-50,1400,60,32,cyan);Text(root,"Raise your left wrist, turn its face toward you and look directly at it.\nMenus stay where you open them. Close and reopen to recenter.\nHeadset sound uses the playback device selected by your VR runtime.",-720,-165,1410,160,26,white);
            Button(root,"BACK",-240,-360,480,80,OpenMenu);
        }
        string AbilityName(int slot){var roster=match.player.GetComponent<LeagueVR.Champions.ChampionRoster>();return roster&&roster.Selected?roster.Selected.spells[slot].name:"QWER"[slot].ToString();}
        void Controls()
        {
            shopScreen=false;Screen("VR controls",true);
            Text(root,"RIGHT HAND",-735,295,700,50,32,cyan);Text(root,"Trigger / grip swing   Basic attack\nSecondary button   Q: "+AbilityName(0)+"\nPrimary button   E: "+AbilityName(2)+"\nStick press   Menu",-735,170,705,190,26,white);
            Text(root,"LEFT HAND",40,295,700,50,32,cyan);Text(root,"Trigger   R: "+AbilityName(3)+"\nPrimary button   W: "+AbilityName(1)+"\nSecondary button   Shop at your fountain\nStick press   Recall / cancel",40,170,705,190,26,white);
            Text(root,"WEARABLE ITEMS",-735,-10,1470,50,31,gold);Text(root,"Reach to a hip, shoulder or back slot and hold grip to grab.\nAim with the item and press trigger to activate. Release grip to holster.\nLift a potion to your mouth to drink. Trinket: centre of your back.",-735,-120,1470,170,26,white);
            Text(root,"Desktop: hold Tab to inspect wrist / WASD / right mouse look / Q F E R abilities / P shop / B recall\n1–6: equip items  ·  7: equip trinket  ·  left mouse: use held item  ·  Backspace: return",-735,-285,1470,100,21,muted);
            Button(root,"BACK",-210,-410,420,75,OpenMenu);
        }
        public void OpenResult(bool victory){shopScreen=false;Screen(victory?"Victory":"Defeat");Text(root,victory?"The enemy Nexus has fallen.":"Your Nexus has fallen.",-690,100,1380,180,42,white);Button(root,"PLAY AGAIN",-300,-140,600,100,()=>match.Play());}
        public void OpenShop()
        {
            if(!CanShop()){match.Notify("Recall to your own fountain to open the shop.");return;}
            var rack=match.economy.GetComponent<RiftItemRack>();if(rack){rack.ReturnHeld(0);rack.ReturnHeld(1);}
            shopScreen=true;inventorySelection=-1;preservePose=false;RenderShop();
        }
        public void SelectCatalogItem(int id)
        {
            if(!CanShop())return;
            category=1;selected=id;detailPage=0;inventorySelection=-1;
            var all=match.catalog.items.Where(i=>i.showInShop).OrderBy(i=>i.name).ThenBy(i=>i.id).ToArray();int index=Array.FindIndex(all,i=>i.id==id);page=Mathf.Max(0,index/12);RenderShop();
        }
        void RenderShop()
        {
            if(!CanShop()){Close();return;}
            Screen("Item shop",shopScreen&&preservePose);shopScreen=true;inventorySelection=-1;renderedVersion=match.economy.Version;
            goldText=Text(root,$"{match.economy.Gold:N0} GOLD     LV {match.economy.Level}     OWN FOUNTAIN",-790,365,1570,40,27,gold);
            for(int c=0;c<categories.Length;c++){int index=c;var b=Button(root,categories[c],-790+c*197,300,180,66,()=>{category=index;page=0;selected=0;detailPage=0;RenderShop();});b.selected=c==category;b.Hover(false);}
            IEnumerable<LeagueItem> list=match.catalog.items.Where(i=>i.showInShop);
            if(category==0){int[] recommend={1056,2003,1001,2031,3340,3115,4633,3089,3157,3152,3135,3100,3020,3158,4630,3916};var active=match.player.GetComponent<LeagueVR.Champions.ChampionRoster>()?.Active; if(active&&(active.id==LeagueVR.Champions.ChampionId.Aatrox||active.id==LeagueVR.Champions.ChampionId.Pantheon))recommend=new[]{1054,2003,1001,2031,3340,3071,3161,3053,3074,6333,3142,3158};else if(active&&(active.id==LeagueVR.Champions.ChampionId.Akshan||active.id==LeagueVR.Champions.ChampionId.Yunara))recommend=new[]{1055,2003,1001,2031,3340,6672,3031,3085,3094,3036,3072,3006};list=list.Where(i=>recommend.Contains(i.id));}
            else if(category==2)list=list.Where(i=>i.attackDamage>0||i.attackSpeed>0||i.criticalChance>0);
            else if(category==3)list=list.Where(i=>i.abilityPower>0||i.mana>0||i.abilityHaste>0);
            else if(category==4)list=list.Where(i=>i.armor>0||i.magicResistance>0||i.health>0);
            else if(category==5)list=list.Where(i=>i.HasTag("Boots"));
            else if(category==6)list=list.Where(i=>i.HasTag("Consumable")||i.HasTag("Trinket")||i.HasTag("Vision"));
            else if(category==7)list=list.Where(i=>i.active!=ItemActive.None);
            var shown=list.OrderBy(i=>i.name).ThenBy(i=>i.id).ToArray();int pages=Mathf.Max(1,Mathf.CeilToInt(shown.Length/12f));page=Mathf.Clamp(page,0,pages-1);var visible=shown.Skip(page*12).Take(12).ToArray();
            if(!shown.Any(i=>i.id==selected)){selected=visible.FirstOrDefault()?.id??0;detailPage=0;}
            for(int n=0;n<visible.Length;n++)
            {
                var item=visible[n];float x=-790+(n%3)*298,y=197-(n/3)*111;
                int cost=match.economy.Cost(item,out var used);var b=Button(root,item.name+"\n"+cost+"g",x,y,282,99,()=>{selected=item.id;detailPage=0;RenderShop();},"item-"+item.id);
                b.label.fontSize=24;b.label.rectTransform.anchoredPosition=new Vector2(80,0);b.label.rectTransform.sizeDelta=new Vector2(190,91);b.label.alignment=TextAlignmentOptions.MidlineLeft;b.selected=selected==item.id;b.Hover(false);Icon(b.transform,item.icon,10,0,60);
            }
            Button(root,"‹ PREV",-790,-228,210,62,()=>{page--;selected=0;RenderShop();},"prev",page>0);Text(root,$"{page+1} / {pages}   ·   {shown.Length} items",-560,-228,440,60,22,muted);Button(root,"NEXT ›",-190,-228,260,62,()=>{page++;selected=0;RenderShop();},"next",page<pages-1);
            Fill(root,115,-12,3,487,new Color(.12f,.25f,.29f));
            var chosen=match.catalog.Find(selected);
            if(chosen!=null)
            {
                Icon(root,chosen.icon,148,198,70);Text(root,chosen.name,235,207,535,95,29,gold);
                Text(root,$"{chosen.price}g total  ·  {chosen.sell}g sell",235,139,520,36,21,muted);
                string description=chosen.description+(string.IsNullOrWhiteSpace(chosen.effectNotes)?"":"\n\nVR adaptation: "+chosen.effectNotes);
                var text=Text(root,"",148,-23,627,279,26,white);text.alignment=TextAlignmentOptions.TopLeft;text.overflowMode=TextOverflowModes.Truncate;
                var chunks=Pages(description,text,627,279);detailPage=Mathf.Clamp(detailPage,0,chunks.Length-1);text.text=chunks[detailPage];
                if(chunks.Length>1){Button(root,"DETAILS "+(detailPage+1)+" / "+chunks.Length+" ›",148,-191,627,45,()=>{detailPage=(detailPage+1)%chunks.Length;RenderShop();});}
                buyButton=Button(root,"BUY",148,-270,290,70,()=>{match.economy.Buy(selected);RenderShop();},"buy");Button(root,"CLOSE",475,-270,300,70,Close,"close");
                string reason=match.economy.CannotBuy(chosen);buyButton.interactable=reason==null;buyButton.label.text="BUY  "+match.economy.Cost(chosen,out var consumed)+"g";buyButton.Hover(false);
                buyReason=Text(root,reason??"Ready to purchase. Owned components reduce the price.",148,-329,627,64,24,gold);
                var recipe=chosen.recipe??Array.Empty<int>();Text(root,recipe.Length>0?"Recipe: "+string.Join(" + ",recipe.Select(id=>match.catalog.Find(id)?.name??id.ToString())):"",-790,-291,890,70,19,muted);
            }
            Text(root,"INVENTORY  ·  Select a slot to inspect, equip or sell",-790,-356,1565,40,21,cyan);
            for(int n=0;n<7;n++)
            {
                int index=n;var slot=n<match.economy.inventory.Count?match.economy.inventory[n]:null;int id=n==6?match.economy.Trinket:slot?.id??0;var item=match.catalog.Find(id);
                var b=Button(root,item==null?(n==6?"TRINKET":"EMPTY"):(n==6?"TRINKET":(n+1).ToString())+"  "+item.name+(slot!=null&&slot.count>1?" ×"+slot.count:""),-790+n*225,-422,211,75,()=>InventoryActions(index),"slot-"+n,item!=null);
                b.label.fontSize=21;b.label.rectTransform.anchoredPosition=new Vector2(55,0);b.label.rectTransform.sizeDelta=new Vector2(145,69);if(item!=null)Icon(b.transform,item.icon,7,0,41);
            }
        }
        void InventoryActions(int index)
        {
            int id=index==6?match.economy.Trinket:index<match.economy.inventory.Count?match.economy.inventory[index].id:0;var item=match.catalog.Find(id);if(item==null)return;
            Screen(item.name,true);shopScreen=true;inventorySelection=index;Icon(root,item.icon,-720,180,150);
            Text(root,item.statText+"\n\n"+(item.active==ItemActive.None?"Passive item: its stats and combat effects apply while owned.":"Physical active: aim with the held item and press trigger.\n"+(item.activeCooldown>0?"Base active cooldown: "+item.activeCooldown+" seconds.":"Uses charges or consumes the item.")),-510,130,1260,350,28,white);
            string slot=index==6?"Back trinket":match.economy.GetComponent<RiftItemRack>().SlotNames[index];Text(root,"Wearable location: "+slot+"  ·  Grab with grip, use with trigger",-720,-205,1450,65,25,cyan);
            Button(root,"FULL ITEM DETAILS",-720,-220,1420,62,()=>SelectCatalogItem(id),"details");
            Button(root,"EQUIP",-720,-360,440,90,()=>{Close();match.economy.GetComponent<RiftItemRack>().EquipSlot(index,1);},"equip",item.active!=ItemActive.None);
            Button(root,"SELL  "+item.sell+"g",-230,-360,440,90,()=>{match.economy.Sell(index);RenderShop();},"sell",index<6&&item.sell>0);Button(root,"BACK",260,-360,440,90,RenderShop,"back");
        }
        static string[] Pages(string value,TMP_Text layout,float width,float height)
        {
            var pages=new List<string>();string current="";
            foreach(System.Text.RegularExpressions.Match part in Regex.Matches(value??"",@"\S+|\n"))
            {
                string word=part.Value;string candidate=current+(current.Length==0||current.EndsWith("\n")||word=="\n"?"":" ")+word;
                if(current.Length>0&&layout.GetPreferredValues(candidate,width,float.PositiveInfinity).y>height){pages.Add(current.Trim());current=word;}else current=candidate;
            }
            if(current.Trim().Length>0||pages.Count==0)pages.Add(current.Trim());return pages.ToArray();
        }
        public void Close()
        {
            if(weaponHidden&&menuAvatar&&menuAvatar.scissorsRoot){menuAvatar.scissorsRoot.gameObject.SetActive(weaponWasActive&&!match.player.OtherActive);weaponHidden=false;}
            if(panel){lastPosition=panel.transform.position;lastRotation=panel.transform.rotation;panel.SetActive(false);Destroy(panel);}panel=null;root=null;status=goldText=buyReason=null;buyButton=null;if(hovered)hovered.Hover(false);hovered=null;
        }
        static RectTransform Rect(string name,Transform parent,float x,float y,float width,float height)
        {
            var go=new GameObject(name,typeof(RectTransform));go.layer=5;var rect=go.GetComponent<RectTransform>();rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=new Vector2(0,.5f);rect.anchoredPosition=new Vector2(x,y);rect.sizeDelta=new Vector2(width,height);return rect;
        }
        static void Fill(Transform parent,float x,float y,float w,float h,Color color){var r=Rect("Panel surface",parent,x,y,w,h);var i=r.gameObject.AddComponent<Image>();i.color=color;i.raycastTarget=false;RiftUIVisuals.Graphic(i);}
        TMP_Text Text(Transform parent,string value,float x,float y,float width,float height,float size,Color color)
        {
            var r=Rect("Text",parent,x,y,width,height);var text=r.gameObject.AddComponent<TextMeshProUGUI>();text.text=value;text.fontSize=size;text.color=color;text.alignment=TextAlignmentOptions.MidlineLeft;text.raycastTarget=false;text.textWrappingMode=TextWrappingModes.Normal;RiftUIVisuals.Text(text);return text;
        }
        RiftUIButton Button(Transform parent,string value,float x,float y,float width,float height,Action action,string key="",bool enabled=true)
        {
            var r=Rect(value,parent,x,y,width,height);var image=r.gameObject.AddComponent<Image>();image.raycastTarget=false;RiftUIVisuals.Graphic(image);var collider=r.gameObject.AddComponent<BoxCollider>();collider.size=new Vector3(width,height,3);collider.center=new Vector3(width*.5f,0,0);collider.isTrigger=true;
            var button=r.gameObject.AddComponent<RiftUIButton>();button.image=image;button.action=action;button.interactable=enabled;button.key=key;
            var label=Text(r,value,12,0,width-24,height-8,25,white);label.rectTransform.anchorMin=label.rectTransform.anchorMax=new Vector2(0,.5f);label.alignment=TextAlignmentOptions.Center;button.label=label;button.Hover(false);return button;
        }
        static void Icon(Transform parent,Texture texture,float x,float y,float size)
        {
            var rect=Rect("Original Riot item icon",parent,x,y,size,size);rect.anchorMin=rect.anchorMax=new Vector2(parent.GetComponent<RiftUIButton>()?0:.5f,.5f);var image=rect.gameObject.AddComponent<RawImage>();image.texture=texture;image.raycastTarget=false;RiftUIVisuals.Graphic(image);
        }
    }
}
