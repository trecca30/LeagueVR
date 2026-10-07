from pathlib import Path
P=Path(r'F:\Vr\VR quirky testing gpt astra\League of legends test\league vr imoport testing/Assets/_Game/LeagueVR/Match/Runtime/RiftUI.cs')
s=P.read_text(encoding='utf-8');a=s.index('        public void OpenMenu()');b=s.index('        void ConfirmNewMatch()',a)
s=s[:a]+'''        public void OpenMenu()=>ChampionMenu(false);
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
            Text(root,"Aim with the casting hand. Head and wrist tracking stay under your control.\\nAbilities are available from the start in this prototype.",-790,-345,1560,85,24,muted);
            Button(root,"BACK",-220,-430,440,65,()=>ChampionMenu(true),"back");
        }
'''+s[b:]
s=s.replace("This resets this match's inventory, gold and structures.","This starts as your selected champion and resets inventory, gold and structures.")
s=s.replace("SUMMONER'S RIFT  /  THE HALLOWED SEAMSTRESS","SUMMONER'S RIFT  /  CHAMPION VR")
s=s.replace('string[] categories={"Gwen"','string[] categories={"Recommended"')
s=s.replace('Text(root,"Trigger / grip swing   Basic attack\\nSecondary button   Q: Snip Snip!\\nPrimary button   E: Skip \'n Slash\\nStick press   Menu"','Text(root,"Trigger / grip swing   Basic attack\\nSecondary button   Q: "+AbilityName(0)+"\\nPrimary button   E: "+AbilityName(2)+"\\nStick press   Menu"')
s=s.replace('Text(root,"Trigger   R: Needlework\\nPrimary button   W: Hallowed Mist\\nSecondary button   Shop at your fountain\\nStick press   Recall / cancel"','Text(root,"Trigger   R: "+AbilityName(3)+"\\nPrimary button   W: "+AbilityName(1)+"\\nSecondary button   Shop at your fountain\\nStick press   Recall / cancel"')
s=s.replace('        void Controls()','        string AbilityName(int slot){var roster=match.player.GetComponent<LeagueVR.Champions.ChampionRoster>();return roster&&roster.Selected?roster.Selected.spells[slot].name:"QWER"[slot].ToString();}\n        void Controls()')
P.write_text(s,encoding='utf-8');print('Champion menu installed')
