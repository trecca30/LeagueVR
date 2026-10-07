using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using Unity.XR.CoreUtils;
using Object=UnityEngine.Object;

namespace LeagueVR.Editor
{
    public static class LeagueVRBuilder
    {
        const string Root="Assets/_Game/LeagueVR";
        public const string ScenePath="Assets/Scenes/LeagueVR.unity";
        static Material cyan, rose, dark;
        static readonly List<MeshCollider> groundColliders=new List<MeshCollider>();
        [MenuItem("Tools/League VR/Build Playable Prototype")]
        public static void Build()
        {
            try { BuildInternal(); }
            catch(Exception e) { File.WriteAllText("Logs/LeagueVR-build-error.txt",e.ToString()); Debug.LogException(e); }
        }
        [MenuItem("Tools/League VR/Rebuild From Preserved Source")]
        public static void RebuildSource() { EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity"); Build(); }
        static void BuildInternal()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            if(Object.FindFirstObjectByType<GwenAbilities>()) throw new InvalidOperationException("This scene is already integrated. Open the original SampleScene to build a fresh copy.");
            var scene=SceneManager.GetActiveScene();
            var origin=Object.FindFirstObjectByType<XROrigin>();
            if(!origin || !origin.Camera) throw new InvalidOperationException("The current scene must contain its existing XR Origin and camera.");
            var gwenPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Gwen/gwen.glb");
            var mapPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/SummonersRIft/summoner_rift_3d_export.glb");
            if(!gwenPrefab || !mapPrefab) throw new InvalidOperationException("Both GLBs must finish importing before integration.");
            Directory.CreateDirectory(Root+"/Generated"); Directory.CreateDirectory(Root+"/Data");
            // Preserve the user's live, unsaved scene before making a playable derivative.
            EditorSceneManager.SaveScene(scene);
            Directory.CreateDirectory("PrototypeBackups");
            File.Copy(scene.path,"PrototypeBackups/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Path.GetFileName(scene.path));
            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Integrate League VR prototype");
            var map=scene.GetRootGameObjects().FirstOrDefault(g=>PrefabUtility.GetCorrespondingObjectFromSource(g)==mapPrefab || g.name=="summoner_rift_3d_export");
            if(!map) map=(GameObject)PrefabUtility.InstantiatePrefab(mapPrefab,scene);
            map.name="Summoner's Rift";
            string[] demoRoots={"Interactables","UI","Environment","Teleport Area Setup"};
            foreach(var root in scene.GetRootGameObjects().Where(r=>demoRoots.Contains(r.name)).ToArray())
            {
                foreach(var manager in root.GetComponentsInChildren<XRInteractionManager>(true))
                {
                    if(manager.gameObject==root)
                    { root.name="XR Interaction Manager"; foreach(Transform child in root.transform.Cast<Transform>().ToArray()) Undo.DestroyObjectImmediate(child.gameObject); }
                    else manager.transform.SetParent(null,true);
                }
                if(root && demoRoots.Contains(root.name)) Undo.DestroyObjectImmediate(root);
            }
            foreach(var t in origin.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Affordance Callouts")).ToArray()) Undo.DestroyObjectImmediate(t.gameObject);
            if(!Object.FindFirstObjectByType<XRInteractionManager>()) new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
            int worldLayer=Layer("LeagueWorld"),combatLayer=Layer("LeagueCombat");
            int worldMask=1<<worldLayer;
            groundColliders.Clear();
            var provider=origin.GetComponentInChildren<TeleportationProvider>(true);
            foreach(var f in map.GetComponentsInChildren<MeshFilter>(true))
            {
                f.gameObject.layer=worldLayer;
                var collider=f.GetComponent<MeshCollider>(); if(!collider) collider=f.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh=f.sharedMesh;
                var r=f.GetComponent<Renderer>();
                bool floor=r && r.sharedMaterials.Any(m=>m && (m.name.Contains("Terrain") || m.name=="Merged_materials"));
                if(!floor) continue;
                groundColliders.Add(collider);
                var area=f.GetComponent<TeleportationArea>(); if(!area) area=f.gameObject.AddComponent<TeleportationArea>();
                area.teleportationProvider=provider; area.interactionLayers=unchecked((int)0x80000000); area.matchOrientation=MatchOrientation.WorldSpaceUp;
                area.filterSelectionByHitNormal=true; area.upNormalToleranceDegrees=35;
                area.colliders.Clear(); area.colliders.Add(collider);
            }
            LeagueVRMapRepair.PrepareSurfaces(map);
            foreach(var ray in origin.GetComponentsInChildren<XRRayInteractor>(true)) ray.raycastMask=ray.raycastMask.value|worldMask;
            Physics.SyncTransforms();
            Vector3 floorPoint=FindSpawn(map,worldMask);
            // Shift the imported map vertically so the practice start is at the scene floor.
            map.transform.position-=Vector3.up*floorPoint.y; floorPoint.y=0;
            Physics.SyncTransforms();
            var spawn=new GameObject("Gwen Spawn").transform; spawn.position=floorPoint+Vector3.up*.08f;
            origin.transform.position=spawn.position; origin.transform.rotation=Quaternion.identity;
            origin.CameraYOffset=1.65f;
            var health=origin.gameObject.AddComponent<Combatant>(); health.team=0; health.maxHealth=650; health.aimPoint=origin.Camera.transform;
            var champion=origin.gameObject.AddComponent<GwenAbilities>(); champion.origin=origin; champion.head=origin.Camera; champion.spawn=spawn;
            champion.rightHand=Find(origin.transform,"Right Controller"); champion.leftHand=Find(origin.transform,"Left Controller");
            champion.worldMask=worldMask; champion.combatMask=1<<combatLayer;
            var tuning=ScriptableObject.CreateInstance<GwenTuning>(); AssetDatabase.CreateAsset(tuning,Root+"/Data/Gwen.asset"); champion.tuning=tuning;
            cyan=Material("Hallowed Cyan",new Color(.15f,.9f,1)); rose=Material("Enemy Rose",new Color(1,.17f,.39f)); dark=Material("Obsidian",new Color(.035f,.06f,.09f));
            champion.effectMaterial=cyan;
            var hitbox=new GameObject("Gwen Hurtbox"); hitbox.transform.SetParent(origin.transform,false); hitbox.layer=combatLayer;
            var capsule=hitbox.AddComponent<CapsuleCollider>(); capsule.isTrigger=true; capsule.radius=.24f; capsule.height=1.65f; capsule.center=Vector3.up*.825f;
            var follow=hitbox.AddComponent<ChampionHitbox>(); follow.champion=champion; follow.capsule=capsule;
            var input=origin.gameObject.AddComponent<GwenVRInput>(); input.champion=champion; input.combatActions=InputAsset();
            CreateAvatar(champion,gwenPrefab);
            LeagueVRMapRepair.RepairGeneratedWeapons();
            var feedback=origin.gameObject.AddComponent<GwenFeedback>(); feedback.champion=champion; feedback.cyanMaterial=cyan; feedback.roseMaterial=rose;
            var hud=Text("Gwen wrist status","GWEN",origin.transform,.025f,TextAlignmentOptions.Left); hud.fontSize=2.4f; hud.rectTransform.sizeDelta=new Vector2(12,9); feedback.status=hud; feedback.wristDisplay=hud.transform;
            CreatePractice(champion,combatLayer,worldMask);
            LeagueVRMapRepair.ApplyReadability(champion);
            PrefabUtility.RecordPrefabInstancePropertyModifications(map.transform);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene,ScenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            var bounds=BoundsOf(map);
            SceneView.lastActiveSceneView?.LookAt(floorPoint+Vector3.up,Quaternion.Euler(25,0,0),14);
            Selection.activeGameObject=origin.gameObject;
            File.WriteAllText("Logs/LeagueVR-build.txt",$"Built {ScenePath}\nMap: {bounds}\nSpawn: {spawn.position}\nGround colliders: {groundColliders.Count}\nColliders: {map.GetComponentsInChildren<MeshCollider>().Length}\nGwen Animation clips: {AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(gwenPrefab)).OfType<AnimationClip>().Count()}\nXR rig prefab assets and OpenXR configuration preserved.\n");
            Debug.Log("LEAGUE VR BUILD COMPLETE: "+ScenePath);
        }
        [MenuItem("Tools/League VR/Refine Practice Placement")]
        public static void RefinePlacement()
        {
            var player=Object.FindFirstObjectByType<GwenAbilities>(); var map=GameObject.Find("Summoner's Rift");
            groundColliders.Clear();groundColliders.AddRange(map.GetComponentsInChildren<TeleportationArea>().Select(a=>a.GetComponent<MeshCollider>()));
            Physics.SyncTransforms();var p=FindSpawn(map,player.worldMask);map.transform.position-=Vector3.up*p.y;p.y=.08f;player.spawn.position=p;player.origin.transform.position=p;Physics.SyncTransforms();
            var practice=GameObject.Find("Rift Practice");if(practice)Object.DestroyImmediate(practice);
            cyan=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Generated/Hallowed Cyan.mat");rose=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Generated/Enemy Rose.mat");dark=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Generated/Obsidian.mat");
            CreatePractice(player,LayerMask.NameToLayer("LeagueCombat"),player.worldMask);
            LeagueVRMapRepair.ApplyReadability(player);PrefabUtility.RecordPrefabInstancePropertyModifications(map.transform);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            SceneView.lastActiveSceneView.LookAt(p+Vector3.up,Quaternion.Euler(25,0,0),12);Debug.Log("Practice spawn refined: "+p);
        }
        static int Layer(string name)
        {
            int existing=LayerMask.NameToLayer(name); if(existing>=0)return existing;
            var settings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers=settings.FindProperty("layers");
            for(int i=8;i<31;i++) if(string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
            { layers.GetArrayElementAtIndex(i).stringValue=name; settings.ApplyModifiedPropertiesWithoutUndo(); return i; }
            throw new InvalidOperationException("No free physics layer.");
        }
        static Vector3 FindSpawn(GameObject map,int worldMask)
        {
            if(groundColliders.Count==0)throw new InvalidOperationException("Map has no identified terrain meshes.");
            var bounds=groundColliders[0].bounds; foreach(var c in groundColliders)bounds.Encapsulate(c.bounds);
            var foliage=map.GetComponentsInChildren<Renderer>().Where(r=>r.sharedMaterials.Any(m=>m && m.name.Contains("chunk_jungle"))).ToArray();
            var focus=foliage.Length>0 ? foliage.Select(r=>r.bounds.center).Aggregate(Vector3.zero,(a,b)=>a+b)/foliage.Length : bounds.center;
            float best=float.NegativeInfinity; Vector3 chosen=Vector3.zero;
            float mapScale=Mathf.Max(1,map.transform.lossyScale.x);
            float gridStep=Mathf.Max(1.25f,mapScale*.5f);
            for(float x=bounds.min.x;x<bounds.max.x;x+=gridStep) for(float z=bounds.min.z;z<bounds.max.z;z+=gridStep)
            {
                if(!Ground(new Vector3(x,bounds.max.y+5,z),out var p,worldMask,bounds.size.y+10))continue;
                if(Mathf.Abs(p.y-focus.y)>4*mapScale)continue;
                if(Physics.CheckCapsule(p+Vector3.up*.45f,p+Vector3.up*1.6f,.28f,worldMask))continue;
                float score=0;
                foreach(var offset in new[]{Vector3.forward,Vector3.back,Vector3.right,Vector3.left})
                    for(int d=1;d<=4;d++) if(Ground(p+offset*d+Vector3.up,out var q,worldMask,2) && Mathf.Abs(q.y-p.y)<.35f)score+=1;
                score-=Vector2.Distance(new Vector2(x,z),new Vector2(focus.x,focus.z))*.3f/mapScale;
                // Main map terrain is on the high plateau, detached export fragments are lower.
                score-=Mathf.Abs(p.y-focus.y)*.5f/mapScale;
                if(score>best){best=score;chosen=p;}
            }
            if(float.IsNegativeInfinity(best))throw new InvalidOperationException("No safe walkable spawn found.");
            return chosen;
        }
        static bool Ground(Vector3 from,out Vector3 point,int mask,float distance=3)
        {
            point=default;
            if(!Physics.Raycast(from,Vector3.down,out var hit,distance,mask,QueryTriggerInteraction.Ignore) || hit.normal.y<.85f || !groundColliders.Contains(hit.collider)) return false;
            point=hit.point; return true;
        }
        static Bounds BoundsOf(GameObject go) { var r=go.GetComponentsInChildren<Renderer>(); var b=r[0].bounds; foreach(var a in r)b.Encapsulate(a.bounds); return b; }
        static Transform Find(Transform root,string name)=>root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name);
        static Material Material(string name,Color color)
        {
            var m=new Material(Shader.Find("Universal Render Pipeline/Unlit")); m.name=name; m.SetColor("_BaseColor",color); AssetDatabase.CreateAsset(m,Root+"/Generated/"+name+".mat"); return m;
        }
        static InputActionAsset InputAsset()
        {
            var asset=ScriptableObject.CreateInstance<InputActionAsset>(); var map=new InputActionMap("Combat");
            map.AddAction("Attack",InputActionType.Button,"<XRController>{RightHand}/triggerPressed");
            map.AddAction("Grip",InputActionType.Button,"<XRController>{RightHand}/gripPressed");
            map.AddAction("Snip",InputActionType.Button,"<XRController>{RightHand}/secondaryButton");
            map.AddAction("Mist",InputActionType.Button,"<XRController>{LeftHand}/primaryButton");
            map.AddAction("Dash",InputActionType.Button,"<XRController>{RightHand}/primaryButton");
            map.AddAction("Needle",InputActionType.Button,"<XRController>{LeftHand}/triggerPressed");
            asset.AddActionMap(map); string path=Root+"/Data/GwenVR.inputactions"; File.WriteAllText(path,asset.ToJson()); Object.DestroyImmediate(asset); AssetDatabase.ImportAsset(path); return AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
        }
        static TMP_Text Text(string name,string text,Transform parent,float scale,TextAlignmentOptions align=TextAlignmentOptions.Center)
        {
            var go=new GameObject(name); go.transform.SetParent(parent,false); go.transform.localScale=Vector3.one*scale;
            var t=go.AddComponent<TextMeshPro>(); t.text=text; t.fontSize=3; t.alignment=align; t.color=new Color(.75f,.95f,1); t.rectTransform.sizeDelta=new Vector2(25,10); return t;
        }
        static void CreatePractice(GwenAbilities player,int layer,int worldMask)
        {
            var practice=new GameObject("Rift Practice").transform;
            Vector3 start=player.spawn.position; var positions=new List<Vector3>();
            // Seek grounded, visible positions around the existing map; never add a replacement floor.
            foreach(float distance in new[]{3f,5f,7f,2f,4f,6f})
                foreach(float angle in new[]{0f,-35f,35f,-70f,70f,135f,-135f,180f})
                {
                    Vector3 offset=Quaternion.Euler(0,angle,0)*Vector3.forward*distance;
                    if(!Ground(start+offset+Vector3.up*1.5f,out var point,worldMask,4) || Mathf.Abs(point.y-start.y)>1 || positions.Any(p=>Vector3.Distance(point,p)<1.6f))continue;
                    if(Physics.CheckCapsule(point+Vector3.up*.5f,point+Vector3.up*1.4f,.35f,worldMask) || Physics.Linecast(start+Vector3.up,point+Vector3.up,worldMask))continue;
                    positions.Add(point); if(positions.Count>=4)break;
                }
            if(positions.Count==0)throw new InvalidOperationException("No practice enemy position could be grounded.");
            for(int i=0;i<Mathf.Min(4,positions.Count);i++)
            {
                var go=new GameObject(i==0?"Practice Echo":"Sparring Echo "+i); go.transform.SetParent(practice); go.transform.position=positions[i]; go.layer=layer;
                var target=go.AddComponent<Combatant>(); target.team=1; target.maxHealth=600; target.armor=25; target.magicResistance=25;
                var c=go.AddComponent<CapsuleCollider>(); c.radius=.38f; c.height=1.7f; c.center=Vector3.up*.85f; c.isTrigger=true;
                var visuals=new GameObject("Sentinel Visuals").transform; visuals.SetParent(go.transform,false);
                var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Gwen/gwen.glb"));
                model.name="Gwen practice echo";model.transform.SetParent(visuals,false);
                model.transform.rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(start-go.transform.position,Vector3.up));
                var animation=model.GetComponentInChildren<Animation>();
                if(animation && animation["Idle.anm"]!=null){animation.clip=animation["Idle.anm"].clip;animation.wrapMode=WrapMode.Loop;animation.playAutomatically=true;}
                var core=new GameObject("Echo aim point");core.transform.SetParent(go.transform,false);core.transform.localPosition=Vector3.up*1.1f;target.aimPoint=core.transform;
                var label=Text("Sentinel health",go.name,visuals,.05f); label.transform.localPosition=Vector3.up*2;
                var enemy=go.AddComponent<TrainingSentinel>(); enemy.player=player; enemy.visuals=visuals; enemy.label=label; enemy.boltMaterial=rose; enemy.attacksPlayer=i>0; enemy.moves=i==3;
            }
        }
        static void CreateAvatar(GwenAbilities player,GameObject source)
        {
            var visual=(GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(visual,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            visual.name="Gwen First Person Body"; visual.transform.SetParent(player.transform,false); visual.transform.localPosition=Vector3.zero;
            var skin=visual.GetComponentInChildren<SkinnedMeshRenderer>(); if(!skin)throw new InvalidOperationException("Gwen has no skinned renderer.");
            var original=skin.sharedMesh; var body=Object.Instantiate(original); body.name="Gwen first person body";
            var bones=skin.bones; var weights=original.boneWeights; var head=Find(visual.transform,"Head");
            bool HeadVertex(int i) { if(weights.Length==0)return original.vertices[i].y>1.4f; var w=weights[i]; int idx=w.boneIndex0; float weight=w.weight0; if(w.weight1>weight){idx=w.boneIndex1;weight=w.weight1;} if(w.weight2>weight){idx=w.boneIndex2;weight=w.weight2;} if(w.weight3>weight)idx=w.boneIndex3; return idx<bones.Length && head && (bones[idx]==head || bones[idx].IsChildOf(head)); }
            for(int s=0;s<body.subMeshCount;s++)
            {
                if(skin.sharedMaterials[s].name!="Body") { body.SetTriangles(Array.Empty<int>(),s); continue; }
                var kept=new List<int>(); var indices=original.GetTriangles(s);
                for(int k=0;k<indices.Length;k+=3) if(!HeadVertex(indices[k]) && !HeadVertex(indices[k+1]) && !HeadVertex(indices[k+2])) { kept.Add(indices[k]);kept.Add(indices[k+1]);kept.Add(indices[k+2]); }
                body.SetTriangles(kept,s);
            }
            AssetDatabase.CreateAsset(body,Root+"/Generated/GwenFirstPersonBody.asset");
            // Bake the existing weapon submeshes before removing them from the body renderer.
            var baked=new Mesh(); skin.BakeMesh(baked);
            var scissors=new GameObject("Gwen Scissors").transform; scissors.SetParent(player.transform,false);
            Transform a=Find(visual.transform,"Scissors_A"), tip=Find(visual.transform,"Buffbone_Scissors_A_Tip");
            Vector3 pivot=skin.transform.InverseTransformPoint(a.position);
            Vector3 direction=skin.transform.InverseTransformDirection((tip.position-a.position).normalized);
            var rotation=Quaternion.Inverse(Quaternion.LookRotation(direction,Vector3.up));
            var triangles=original.GetTriangles(1); var ai=new List<int>(); var bi=new List<int>();
            for(int i=0;i<triangles.Length;i+=3)
            {
                var w=weights[triangles[i]]; var bone=bones[w.boneIndex0]; bool isB=bone && (bone.name.StartsWith("Scissors_B") || (Find(visual.transform,"Scissors_B") && bone.IsChildOf(Find(visual.transform,"Scissors_B"))));
                var list=isB?bi:ai; list.Add(triangles[i]);list.Add(triangles[i+1]);list.Add(triangles[i+2]);
            }
            Transform MakeBlade(string name,List<int> indices)
            {
                var mesh=new Mesh {name=name,indexFormat=IndexFormat.UInt32}; mesh.vertices=baked.vertices.Select(v=>rotation*(v-pivot)*.75f).ToArray(); mesh.uv=baked.uv; mesh.triangles=indices.ToArray(); mesh.RecalculateNormals();mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh,Root+"/Generated/"+name+".asset");
                var go=new GameObject(name);go.transform.SetParent(scissors,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=skin.sharedMaterials[1];return go.transform;
            }
            var bladeA=MakeBlade("Scissors A",ai); var bladeB=MakeBlade("Scissors B",bi);
            var needleMesh=new Mesh{name="Gwen Needle",indexFormat=IndexFormat.UInt32}; needleMesh.vertices=baked.vertices;needleMesh.uv=baked.uv;needleMesh.triangles=original.GetTriangles(3);needleMesh.RecalculateBounds();
            Vector3 needleCenter=needleMesh.bounds.center; var needleBone=Find(visual.transform,"Needle"); var needleTip=Find(visual.transform,"Buffbone_Needle_Tip"); var nr=Quaternion.Inverse(Quaternion.LookRotation(skin.transform.InverseTransformDirection(needleTip.position-needleBone.position)));
            needleMesh.vertices=needleMesh.vertices.Select(v=>nr*(v-needleCenter)).ToArray();needleMesh.RecalculateNormals();needleMesh.RecalculateBounds();AssetDatabase.CreateAsset(needleMesh,Root+"/Generated/GwenNeedle.asset");
            var needle=new GameObject("Gwen Needle");needle.AddComponent<MeshFilter>().sharedMesh=needleMesh;needle.AddComponent<MeshRenderer>().sharedMaterial=skin.sharedMaterials[3];player.needleVisualPrefab=PrefabUtility.SaveAsPrefabAsset(needle,Root+"/Generated/GwenNeedle.prefab");Object.DestroyImmediate(needle);Object.DestroyImmediate(baked);
            skin.sharedMesh=body; skin.updateWhenOffscreen=true;
            var avatar=player.gameObject.AddComponent<GwenAvatar>();avatar.champion=player;avatar.visualRoot=visual.transform;avatar.scissorsRoot=scissors;avatar.bladeA=bladeA;avatar.bladeB=bladeB;
            avatar.animationPlayer=visual.GetComponentInChildren<Animation>();
            avatar.leftUpper=Find(visual.transform,"L_Shoulder");avatar.leftLower=Find(visual.transform,"L_Elbow");avatar.leftPalm=Find(visual.transform,"L_Hand");
            avatar.rightUpper=Find(visual.transform,"R_Shoulder");avatar.rightLower=Find(visual.transform,"R_Elbow");avatar.rightPalm=Find(visual.transform,"R_Hand");
            if(avatar.animationPlayer) { avatar.animationPlayer.playAutomatically=true; var idle=avatar.animationPlayer["Idle.anm"];if(idle!=null){avatar.animationPlayer.clip=idle.clip;avatar.animationPlayer.wrapMode=WrapMode.Loop;} }
        }
    }
}
