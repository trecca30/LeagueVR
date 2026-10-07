using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using LeagueVR.Match;
using Object=UnityEngine.Object;
namespace LeagueVR.Editor
{
    public static class LeagueVRMatchBuilder
    {
        const string Root="Assets/_Game/LeagueVR/Match";static RiftMatch match;
        [MenuItem("Tools/League VR/Build League Match")]
        public static void Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play Mode first.");
            if(Object.FindFirstObjectByType<RiftMatch>())throw new InvalidOperationException("Match already exists. Use the existing scene.");
            var player=Object.FindFirstObjectByType<GwenAbilities>();if(!player)throw new InvalidOperationException("Gwen is not in the current scene.");
            var scene=SceneManager.GetActiveScene();Directory.CreateDirectory("PrototypeBackups");Directory.CreateDirectory("Logs/LeagueMatch");
            EditorSceneManager.SaveScene(scene,"PrototypeBackups/Before-league-match.unity",true);
            string mapBefore=SnapshotMap();File.WriteAllText("Logs/LeagueMatch/map-before.txt",mapBefore);
            foreach(var folder in new[]{"Generated","Prefabs","Data"})Directory.CreateDirectory(Root+"/"+folder);
            foreach(string obj in AssetDatabase.FindAssets("t:Model",new[]{Root+"/Art"}).Select(AssetDatabase.GUIDToAssetPath))
            {
                if(AssetImporter.GetAtPath(obj) is ModelImporter importer){importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.isReadable=true;importer.globalScale=.01f;importer.SaveAndReimport();}
            }
            var go=new GameObject("League Match");match=go.AddComponent<RiftMatch>();match.player=player;
            var rules=ScriptableObject.CreateInstance<RiftRules>();AssetDatabase.CreateAsset(rules,Root+"/Data/Rift Rules.asset");match.rules=rules;
            var imported=JsonUtility.FromJson<CatalogImport>(File.ReadAllText(Root+"/Data/catalog.json"));var catalog=ScriptableObject.CreateInstance<LeagueCatalog>();catalog.patch=imported.patch;catalog.items=imported.items;
            foreach(var item in catalog.items)item.icon=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Art/ItemIcons/"+item.id+".png");AssetDatabase.CreateAsset(catalog,Root+"/Data/League Items.asset");match.catalog=catalog;
            match.economy=player.gameObject.AddComponent<RiftEconomy>();match.economy.match=match;
            if(!player.GetComponent<RiftActor>()){var actor=player.gameObject.AddComponent<RiftActor>();actor.health=player.GetComponent<Combatant>();actor.radius=.3f;}
            match.ui=go.AddComponent<RiftUI>();match.ui.match=match;
            match.blueMaterial=ColorMaterial("Blue projectiles",new Color(.12f,.85f,1));match.redMaterial=ColorMaterial("Red projectiles",new Color(1,.16f,.3f));match.neutralMaterial=ColorMaterial("Objective projectiles",new Color(.6f,.18f,.95f));
            match.spawnedRoot=new GameObject("Minion waves").transform;match.spawnedRoot.SetParent(go.transform,false);
            var locations=new[]{new[]{new Vector2(-59,-45),new Vector2(-66,-27),new Vector2(-69,-4),new Vector2(-69,26),new Vector2(-61,51),new Vector2(-42,65),new Vector2(-13,67),new Vector2(13,65),new Vector2(36,62),new Vector2(51,61)},new[]{new Vector2(-59,-45),new Vector2(-44,-30),new Vector2(-28,-17),new Vector2(-12,-2),new Vector2(1,11),new Vector2(14,25),new Vector2(28,40),new Vector2(40,52),new Vector2(51,61)},new[]{new Vector2(-59,-45),new Vector2(-43,-53),new Vector2(-17,-57),new Vector2(10,-57),new Vector2(37,-53),new Vector2(57,-40),new Vector2(64,-18),new Vector2(64,10),new Vector2(61,36),new Vector2(51,61)}};
            match.lanes=locations.Select((points,i)=>new RiftLane{name=new[]{"Top","Mid","Bot"}[i],points=Route(points)}).ToArray();
            match.minions=new GameObject[8];
            for(int team=0;team<2;team++)for(int k=0;k<4;k++)match.minions[team*4+k]=MinionPrefab(team,(MinionKind)k);
            var structures=new List<RiftStructure>();var parent=new GameObject("Structures").transform;parent.SetParent(go.transform,false);
            // Coordinates align to the empty structure pads in this original map export.
            Vector2[][] blue={new[]{new Vector2(-65,35.9f),new Vector2(-66.4f,-1.3f),new Vector2(-67.1f,-16.1f),new Vector2(-62.8f,-22.7f)},new[]{new Vector2(-8.7f,7.7f),new Vector2(-25.3f,-8.5f),new Vector2(-38.2f,-28.5f),new Vector2(-42.8f,-23.4f)},new[]{new Vector2(27.4f,-55.2f),new Vector2(-7.7f,-50.1f),new Vector2(-31.9f,-52.2f),new Vector2(-40.9f,-52.2f)}};
            Vector2[][] red={new[]{new Vector2(-32.8f,70.4f),new Vector2(2.5f,65.4f),new Vector2(35.9f,67.8f),new Vector2(33.8f,74.2f)},new[]{new Vector2(13.5f,18),new Vector2(24.1f,31.6f),new Vector2(35.4f,46.4f),new Vector2(41,51.7f)},new[]{new Vector2(66.5f,-8.8f),new Vector2(67.1f,25.8f),new Vector2(62.6f,48),new Vector2(68.9f,39.1f)}};
            for(int team=0;team<2;team++)
            {
                var points=team==0?blue:red;
                for(int lane=0;lane<3;lane++)
                {
                    RiftStructure prior=null;
                    for(int stage=0;stage<4;stage++)
                    {
                        var kind=stage==0?StructureKind.OuterTurret:stage==1?StructureKind.InnerTurret:stage==2?StructureKind.InhibitorTurret:StructureKind.Inhibitor;
                        var structure=Structure(parent,team,lane,kind,points[lane][stage]);structure.prerequisite=prior;prior=structure;structures.Add(structure);
                    }
                }
                foreach(var point in team==0?new[]{new Vector2(-60.3f,-42.5f),new Vector2(-53.1f,-52.1f)}:new[]{new Vector2(52.4f,66.8f),new Vector2(46.8f,57.8f)})structures.Add(Structure(parent,team,-1,StructureKind.NexusTurret,point));
                structures.Add(Structure(parent,team,-1,StructureKind.Nexus,team==0?new Vector2(-59.7f,-48):new Vector2(52.9f,62.5f)));
            }
            match.structures=structures.ToArray();
            var fountains=new List<RiftFountain>();
            for(int team=0;team<2;team++)
            {
                var fountain=new GameObject(team==0?"Blue Fountain & Shop":"Red Fountain & Shop");fountain.transform.SetParent(go.transform,false);fountain.transform.position=Surface(team==0?new Vector2(-70,-59):new Vector2(66,72));var f=fountain.AddComponent<RiftFountain>();f.team=team;
                var ring=new GameObject("Fountain healing boundary").AddComponent<LineRenderer>();ring.transform.SetParent(fountain.transform,false);ring.sharedMaterial=match.TeamMaterial(team);ring.useWorldSpace=false;ring.loop=true;ring.startWidth=ring.endWidth=.035f;ring.positionCount=64;
                for(int i=0;i<64;i++){float a=i*Mathf.PI*2/64;ring.SetPosition(i,new Vector3(Mathf.Sin(a)*rules.fountainRadius,.15f,Mathf.Cos(a)*rules.fountainRadius));}
                var label=Label(fountain.transform,team==0?"FOUNTAIN  •  ITEM SHOP":"ENEMY FOUNTAIN",2.8f);label.text=team==0?"FOUNTAIN  •  ITEM SHOP\nLeft secondary / P to open":"ENEMY FOUNTAIN";
                fountains.Add(f);
            }
            match.fountains=fountains.ToArray();
            var objectives=new List<RiftObjective>();var camps=new GameObject("Jungle & Objectives").transform;camps.SetParent(go.transform,false);
            objectives.Add(Objective(camps,"Dragon","sru_dragon_fire",new Vector2(40,-23),300,300,5500,80,100,"sru_dragon_fire_tx_cm.png",.008f));
            objectives.Add(Objective(camps,"Baron Nashor","sru_baron",new Vector2(-29,33),1200,360,12600,350,300,"baron_base_tx_cm.png",.008f));
            objectives.Add(Objective(camps,"Rift Herald","sru_riftherald",new Vector2(-29,33),840,0,11700,100,200,"sru_riftherald_base_main_tx_cm.png",.008f));
            for(int side=0;side<2;side++)
            {
                Vector2 Point(float x,float z)=>side==0?new Vector2(x,z):new Vector2(-x-6, -z+15);
                objectives.Add(Objective(camps,"Blue Sentinel","sru_blue",Point(-30,5),55,270,2300,95,90,"sru_blue_base_tx_cm.png",.008f));
                objectives.Add(Objective(camps,"Red Brambleback","sru_red",Point(0,-31),55,270,2300,95,90,"sru_red.png",.012f));
                objectives.Add(Objective(camps,"Gromp","sru_gromp",Point(-48,10),67,120,2050,78,90,"sru_gromp_base_tx_cm.png",.008f));
                objectives.Add(Objective(camps,"Raptors","sru_razorbeak",Point(-5,-18),55,120,1200,20,60,"sru_razorbeak_tx_cm.png",.009f));
                objectives.Add(Objective(camps,"Krugs","sru_krug",Point(11,-45),67,120,1600,80,70,"sru_krug_base_tx_cm.png",.011f));
            }
            match.objectives=objectives.ToArray();
            foreach(string name in new[]{"Rift Practice","Gwen2 and Gwen3 Practice"}){var dummy=GameObject.Find(name);if(dummy)Object.DestroyImmediate(dummy);}
            if(SnapshotMap()!=mapBefore)throw new InvalidOperationException("Map hierarchy/geometry changed unexpectedly.");
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            File.WriteAllText("Logs/LeagueMatch/build.txt",$"Built {match.lanes.Length} lanes, {match.minions.Length} minion prefabs, {structures.Count} structures, {objectives.Count} neutral camps/objectives, {catalog.items.Length} catalog items.\nOriginal map and XR configuration preserved.\nDummies removed from the scene; source assets preserved.");Debug.Log("League match scene integrated.");
        }
        static string SnapshotMap()
        {
            var map=GameObject.Find("Summoner's Rift");return string.Join("\n",map.GetComponentsInChildren<Transform>(true).Select(t=>$"{t.name}|{t.localPosition:R}|{t.localRotation:R}|{t.localScale:R}|"+(t.GetComponent<MeshFilter>()?t.GetComponent<MeshFilter>().sharedMesh.GetEntityId().ToString():"")));
        }
        static Vector3 Surface(Vector2 p)
        {
            if(match.Ground(new Vector3(p.x,0,p.y),out var result))return result;
            for(float radius=.5f;radius<=4;radius+=.5f)for(int n=0;n<16;n++){float a=n*Mathf.PI/8;if(match.Ground(new Vector3(p.x+Mathf.Cos(a)*radius,0,p.y+Mathf.Sin(a)*radius),out result))return new Vector3(p.x,result.y,p.y);}
            throw new InvalidOperationException("No existing ground around "+p);
        }
        static Vector3[] Route(Vector2[] points)
        {
            var result=new List<Vector3>();
            for(int i=0;i<points.Length-1;i++){float distance=Vector2.Distance(points[i],points[i+1]);int count=Mathf.CeilToInt(distance/1.5f);for(int n=0;n<count;n++){var point=Vector2.Lerp(points[i],points[i+1],n/(float)count);result.Add(Surface(point));}}
            result.Add(Surface(points.Last()));return result.ToArray();
        }
        static Material ColorMaterial(string name,Color color){var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));material.SetColor("_BaseColor",color);AssetDatabase.CreateAsset(material,Root+"/Generated/"+name+".mat");return material;}
        static Material ModelMaterial(string unit,string texture)
        {
            string path=Root+"/Generated/"+unit+"-"+texture+".mat";var prior=AssetDatabase.LoadAssetAtPath<Material>(path);if(prior)return prior;
            var source=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Art/Models/"+unit+"/"+texture);if(!source)throw new InvalidOperationException("Missing source texture "+unit+"/"+texture);
            var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));material.SetTexture("_BaseMap",source);material.SetFloat("_Cull",0);AssetDatabase.CreateAsset(material,path);return material;
        }
        static Transform Visual(Transform parent,string unit,string texture,float scale,Func<string,bool> accept=null)
        {
            var visual=new GameObject("Original model").transform;visual.SetParent(parent,false);var material=ModelMaterial(unit,texture);
            foreach(var file in AssetDatabase.FindAssets("t:Model",new[]{Root+"/Art/Models/"+unit}).Select(AssetDatabase.GUIDToAssetPath).Where(p=>p.EndsWith(".obj") && (accept==null || accept(p))))
            {
                var model=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(file),visual);model.name=System.IO.Path.GetFileNameWithoutExtension(file);model.transform.localScale=Vector3.one*(scale/.01f);
                foreach(var renderer in model.GetComponentsInChildren<Renderer>()){renderer.sharedMaterials=Enumerable.Repeat(material,renderer.sharedMaterials.Length).ToArray();renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;}
            }
            if(visual.GetComponentsInChildren<Renderer>().Length==0)throw new InvalidOperationException("No original model for "+unit);return visual;
        }
        static TMP_Text Label(Transform parent,string text,float height)
        {
            var go=new GameObject("Health label");go.transform.SetParent(parent,false);go.transform.localPosition=Vector3.up*height;go.transform.localScale=Vector3.one*.04f;var label=go.AddComponent<TextMeshPro>();label.fontSize=3;label.alignment=TextAlignmentOptions.Center;label.color=new Color(.8f,.9f,1);label.rectTransform.sizeDelta=new Vector2(35,12);label.text=text;return label;
        }
        static Combatant Hitbox(GameObject go,int team,float hp,float radius,float height)
        {
            go.layer=LayerMask.NameToLayer("LeagueCombat");var health=go.AddComponent<Combatant>();health.team=team;health.countsAsChampion=false;health.maxHealth=hp;health.armor=health.magicResistance=0;
            var collider=go.AddComponent<CapsuleCollider>();collider.radius=radius;collider.height=height;collider.center=Vector3.up*height*.5f;collider.isTrigger=true;
            var aim=new GameObject("Aim");aim.transform.SetParent(go.transform,false);aim.transform.localPosition=Vector3.up*Mathf.Min(height*.55f,1.4f);health.aimPoint=aim.transform;return health;
        }
        static GameObject MinionPrefab(int team,MinionKind kind)
        {
            string[] units={"sru_orderminionmelee","sru_orderminionranged","sru_orderminionsiege","sru_orderminionsuper"};string[] textures={"orderminion_melee_tx_cm","orderminion_caster_tx_cm","order_minion_siege_tx_cm","blue_superminion_tx_cm"};
            var go=new GameObject((team==0?"Blue ":"Red ")+kind);var health=Hitbox(go,team,500,kind==MinionKind.Super?.65f:.4f,kind==MinionKind.Super?1.6f:1.1f);var minion=go.AddComponent<RiftMinion>();minion.health=health;minion.kind=kind;minion.visual=Visual(go.transform,units[(int)kind],textures[(int)kind]+(team==1?"_red":"")+".png",.01f);minion.label=Label(go.transform,go.name,kind==MinionKind.Super?2:1.5f);
            var prefab=PrefabUtility.SaveAsPrefabAsset(go,Root+"/Prefabs/"+go.name+".prefab");Object.DestroyImmediate(go);return prefab;
        }
        static RiftStructure Structure(Transform parent,int team,int lane,StructureKind kind,Vector2 point)
        {
            var go=new GameObject((team==0?"Blue ":"Red ")+(lane>=0?new[]{"Top ","Mid ","Bot "}[lane]:"")+kind);go.transform.SetParent(parent,false);go.transform.position=Surface(point);
            bool turret=(int)kind<=(int)StructureKind.NexusTurret;float height=turret?5:kind==StructureKind.Nexus?3.6f:2.6f,radius=kind==StructureKind.Nexus?2.3f:1;
            var health=Hitbox(go,team,match.rules.StructureHealth(kind),radius,height);health.armor=health.magicResistance=turret?60:kind==StructureKind.Nexus?20:0;
            var structure=go.AddComponent<RiftStructure>();structure.health=health;structure.structure=true;structure.radius=radius;structure.kind=kind;structure.lane=lane;
            if(turret)structure.visual=Visual(go.transform,"turret",team==0?"turret_base_tx_cm.png":"turret_skin01_tx_cm.png",.008f,p=>p.EndsWith("_Base.obj"));
            else if(kind==StructureKind.Nexus)structure.visual=Visual(go.transform,"nexus",team==0?"nexus_tx_cm.png":"nexus_tx_cm_red.png",.01f,p=>p.EndsWith("_SRUAP_OrderNexus_Mat.obj"));
            else structure.visual=Visual(go.transform,"inhibitor",team==0?"inhibitor_tx_cm.png":"inhibitor_tx_cm_red.png",.008f,p=>p.EndsWith("_pally_inhib_texture.obj"));
            var muzzle=new GameObject("Turret muzzle");muzzle.transform.SetParent(go.transform,false);muzzle.transform.localPosition=Vector3.up*(turret?4:1.5f);structure.muzzle=muzzle.transform;structure.label=Label(go.transform,go.name,height+.5f);return structure;
        }
        static RiftObjective Objective(Transform parent,string name,string unit,Vector2 point,float first,float respawn,float hp,float damage,int reward,string texture,float scale)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.position=Surface(point);var health=Hitbox(go,2,hp,unit=="sru_baron"?2:1,unit=="sru_baron"?6:2.5f);health.armor=30;health.magicResistance=30;
            var objective=go.AddComponent<RiftObjective>();objective.health=health;objective.neutral=true;objective.objective=name;objective.spawnTime=first;objective.respawnDelay=respawn;objective.damage=damage;objective.reward=reward;
            objective.visual=Visual(go.transform,unit,texture,scale,p=>!p.Contains("Eye")&&!p.Contains("Horn")&&!p.Contains("Shield")&&!p.Contains("Spawn")&&!p.Contains("mini_mat"));objective.label=Label(go.transform,name,unit=="sru_baron"?7:3);objective.visual.gameObject.SetActive(false);return objective;
        }
    }
}
