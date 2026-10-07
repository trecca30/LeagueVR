from pathlib import Path
import shutil,json
P=Path(r'F:\Vr\VR quirky testing gpt astra\League of legends test\league vr imoport testing');S=Path(r'C:\Users\Kakad\AppData\Local\Temp\LeagueChampions')
R=P/'Assets/_Game/LeagueVR';dest=R/'Champions/Runtime';dest.mkdir(parents=True,exist_ok=True)
for f in S.glob('Champion*.cs'):shutil.copy2(f,dest/f.name)
def edit(rel,old,new):
    p=R/rel;s=p.read_text(encoding='utf-8-sig');assert old in s,(rel,old);p.write_text(s.replace(old,new),encoding='utf-8')
edit('Runtime/GwenAbilities.cs','public bool Busy => qCasting;','public LeagueVR.Champions.ChampionAbilities Other => GetComponent<LeagueVR.Champions.ChampionAbilities>();\n        public bool OtherActive => Other && Other.IsActive;\n        public string ChampionName => OtherActive ? Other.Definition.name : "Gwen";\n        public bool Busy => OtherActive ? Other.Busy : qCasting;')
edit('Runtime/GwenAbilities.cs','public void AdvanceBasicCooldowns(float seconds) {','public void AdvanceBasicCooldowns(float seconds) { if(OtherActive){Other.Advance(seconds);return;}')
edit('Runtime/GwenAbilities.cs','public void AdvanceUltimateCooldown(float seconds) {','public void AdvanceUltimateCooldown(float seconds) { if(OtherActive){Other.Advance(seconds,true);return;}')
edit('Runtime/GwenAbilities.cs','public void ResetAttackTimer() => attackReady=0;','public void ResetAttackTimer() { attackReady=0;if(OtherActive)Other.ResetAttack(); }')
edit('Runtime/GwenAbilities.cs','public bool MistActive => Time.time','public bool MistActive => !OtherActive && Time.time')
edit('Runtime/GwenAbilities.cs','public bool Empowered => Time.time','public bool Empowered => !OtherActive && Time.time')
edit('Runtime/GwenAbilities.cs','public float Cooldown(string key) => Mathf.Max','public float Cooldown(string key) => OtherActive ? Other.Cooldown("QWER".IndexOf(key)) : Mathf.Max')
for method in ['BasicAttack','CastQ','CastW','CastE','CastR']:
    edit('Runtime/GwenAbilities.cs',f'public bool {method}()\n        {{',f'public bool {method}()\n        {{\n            if(OtherActive)return Other.{method}();\n            if(Health.Stunned)return false;')
edit('Runtime/GwenAbilities.cs','void Signal(string name,Vector3 pos,Vector3 dir) {','public void Emit(string name,Vector3 pos,Vector3 dir) => Signal(name,pos,dir);\n        void Signal(string name,Vector3 pos,Vector3 dir) {')
edit('Runtime/GwenAbilities.cs','public void ResetPractice() { qReady=wReady=eReady=rReady=attackReady=0; Health.ResetHealth(); }','public void ResetPractice() { StopAllCoroutines();qCasting=false;respawnRoutine=null;mistUntil=empoweredUntil=0;QStacks=RStage=0;qReady=wReady=eReady=rReady=attackReady=0;Other?.ResetState();Health.ResetHealth(); }')
edit('Runtime/GwenAbilities.cs','Health.ResetHealth(); qReady=wReady=eReady=rReady=attackReady=0;','Other?.ResetState();Health.ResetHealth(); qReady=wReady=eReady=rReady=attackReady=0;')
edit('Runtime/GwenAbilities.cs','if (distance < .15f','if (Health.Rooted || distance < .15f')
edit('Runtime/GwenFeedback.cs','if (ability=="Q" || ability=="Attack1")','if(champion.OtherActive){if(ability!="Death")Haptic(XRNode.RightHand,.18f,.08f);return;}\n            if (ability=="Q" || ability=="Attack1")')
edit('Runtime/GwenAudio.cs','void OnCast(string ability,Vector3 position,Vector3 direction){switch','void OnCast(string ability,Vector3 position,Vector3 direction){if(champion.OtherActive)return;switch')
edit('Runtime/GwenVRInput.cs','if (cc && cc.enabled) cc.Move','if (cc && cc.enabled && !champion.Health.Rooted) cc.Move')
edit('Runtime/Combatant.cs','public static event Action<Combatant, DamageHit> Attacked;','public static event Action<Combatant, DamageHit> Attacked;\n        public static event Action<Combatant,DamageHit> Defeated;\n        float stunUntil,rootUntil,sleepUntil,sleepBonus; Combatant sleepSource;\n        public bool Stunned=>Time.time<stunUntil||Time.time<sleepUntil;\n        public bool Rooted=>Stunned||Time.time<rootUntil;\n        public void ApplyStun(float seconds){stunUntil=Mathf.Max(stunUntil,Time.time+seconds*(1-(GetComponent<RiftEconomy>()?.Tenacity??0)));}\n        public void ApplyRoot(float seconds){rootUntil=Mathf.Max(rootUntil,Time.time+seconds*(1-(GetComponent<RiftEconomy>()?.Tenacity??0)));}\n        public void ApplySleep(float seconds,float bonus,Combatant source){sleepUntil=Time.time+seconds;sleepBonus=bonus;sleepSource=source;}')
edit('Runtime/Combatant.cs','public float SlowMultiplier => Time.time','public float SlowMultiplier => Rooted ? 0 : Time.time')
edit('Runtime/Combatant.cs','Attacked?.Invoke(this, hit);','Attacked?.Invoke(this, hit);\n            if(Time.time<sleepUntil&&hit.ability!="Passive"){sleepUntil=0;hit.amount+=sleepBonus;sleepBonus=0;}')
edit('Runtime/Combatant.cs','deathReported = true; onDeath.Invoke();','deathReported = true; Defeated?.Invoke(this,hit);onDeath.Invoke();')
edit('Runtime/Combatant.cs','slowUntil = attackSlowUntil = 0;','stunUntil=rootUntil=sleepUntil=0;sleepBonus=0;slowUntil = attackSlowUntil = 0;')
edit('Match/Runtime/RiftMinion.cs','if(Time.time<MarchAt)','if(health.Stunned){motion?.Locomotion(false);return;}if(Time.time<MarchAt)')
edit('Match/Runtime/RiftMinion.cs','if(!health.IsAlive||!victim','if(health.Stunned||!health.IsAlive||!victim')
edit('Match/Runtime/RiftMatch.cs','StopAllCoroutines();foreach(Transform child in spawnedRoot)','player.GetComponent<LeagueVR.Champions.ChampionRoster>()?.ApplySelection();StopAllCoroutines();foreach(Transform child in spawnedRoot)')
edit('Match/Runtime/RiftEconomy.cs','initialized = true;','initialized = true;\n            var roster=GetComponent<LeagueVR.Champions.ChampionRoster>();if(roster&&roster.Active)SetChampionBase(roster.Active);')
edit('Match/Runtime/RiftEconomy.cs','public bool Owns(int id)', '''public LeagueVR.Champions.ChampionDefinition ChampionBase {get;private set;}
        public void SetChampionBase(LeagueVR.Champions.ChampionDefinition d)
        {
            ChampionBase=d;if(!initialized)return;
            baseHealth=d.health;baseArmor=d.armor;baseMR=d.magicResist;
            if(basis)Destroy(basis);basis=Instantiate(original);
            if(d.id!=LeagueVR.Champions.ChampionId.Gwen){basis.attackDamage=d.attackDamage;basis.attackInterval=1/d.attackSpeed;basis.attackReach=d.attackReach;}
            Recalculate();Mana=MaxMana;
        }
        public bool Owns(int id)''')
edit('Match/Runtime/RiftEconomy.cs','if (Mana < amount)','if(ChampionBase&&!ChampionBase.UsesMana)return true;\n            if (Mana < amount)')
edit('Match/Runtime/RiftEconomy.cs','MoveSpeedBonus = ((340 + moveFlat) * (1 + movePercent) / 340) - 1;','float baseMove=ChampionBase?ChampionBase.moveSpeed:340;MoveSpeedBonus = ((baseMove + moveFlat) * (1 + movePercent) / 340) - 1;')
edit('Match/Runtime/RiftEconomy.cs','MaxMana = 330 + 40 * (Level - 1)','MaxMana = (ChampionBase?ChampionBase.mana:330) + (ChampionBase?ChampionBase.manaGrowth:40) * (Level - 1)')
edit('Match/Runtime/RiftEconomy.cs','Mana = Mathf.Clamp(Mana + Mathf.Max(0, MaxMana - oldMana)','if(ChampionBase&&!ChampionBase.UsesMana)MaxMana=0;\n            Mana = Mathf.Clamp(Mana + Mathf.Max(0, MaxMana - oldMana)')
edit('Match/Runtime/RiftEconomy.cs','baseHealth + 109 * (Level - 1)','baseHealth + (ChampionBase?ChampionBase.healthGrowth:109) * (Level - 1)')
edit('Match/Runtime/RiftEconomy.cs','baseArmor + 4.7f * (Level - 1)','baseArmor + (ChampionBase?ChampionBase.armorGrowth:4.7f) * (Level - 1)')
edit('Match/Runtime/RiftEconomy.cs','baseMR + 2.05f * (Level - 1)','baseMR + (ChampionBase?ChampionBase.magicResistGrowth:2.05f) * (Level - 1)')
edit('Match/Runtime/RiftEconomy.cs','healthRegen = 1.7f *','healthRegen = (ChampionBase?ChampionBase.healthRegen/5:1.7f) *')
edit('Match/Runtime/RiftEconomy.cs','manaRegen = 1.5f *','manaRegen = (ChampionBase?ChampionBase.manaRegen/5:1.5f) *')
edit('Match/Runtime/RiftEconomy.cs','basis.attackDamage + 3 * (Level - 1)','basis.attackDamage + (ChampionBase?ChampionBase.attackGrowth:3) * (Level - 1)')
edit('Match/Runtime/RiftEconomy.cs','.0225f * (Level - 1)','(ChampionBase?ChampionBase.attackSpeedGrowth/100:.0225f) * (Level - 1)')
edit('Match/Runtime/RiftVRHUD.cs','vitals.text=$"GWEN','vitals.text=$"{p.ChampionName.ToUpperInvariant()}')
edit('Match/Runtime/RiftVRHUD.cs','meta.text=$"Lv','if(p.OtherActive)abilities.text=$"Q {CD(\"Q\")}    W {CD(\"W\")}\\nE {CD(\"E\")}    R {CD(\"R\")}\\n{p.Other.StateText}";\n            meta.text=$"Lv')
print('Runtime core integrated')
