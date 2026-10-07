using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace LeagueVR.Editor
{
    [InitializeOnLoad]
    public static class LeagueVRAdditionalDummies
    {
        const string Root="Assets/_Game/LeagueVR";
        const string Group="Gwen2 and Gwen3 Practice";
        static LeagueVRAdditionalDummies(){EditorApplication.playModeStateChanged+=OnPlayState;}
        [MenuItem("Tools/League VR/Arrange New Dummies In Front Of Spawn")]
        public static void Arrange()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play Mode first.");
            var g=Object.FindFirstObjectByType<GwenAbilities>();var group=GameObject.Find(Group);
            if(!g || !group)throw new InvalidOperationException("Open the integrated LeagueVR scene first.");
            var dummies=group.GetComponentsInChildren<TrainingSentinel>();
            var points=FindFrontPositions(g);
            if(points.Count<6)throw new InvalidOperationException("Could not find six clear grounded positions in front of spawn.");
            var report=new StringBuilder();
            for(int i=0;i<6;i++)
            {
                var dummy=dummies[i];dummy.transform.position=points[i];
                FitBody(dummy);
                SceneVisibilityManager.instance.Show(dummy.gameObject,true);
                report.AppendLine(dummy.name+" feet="+dummy.transform.position+" body="+BodyBounds(dummy.visuals.GetChild(0),false));
            }
            Physics.SyncTransforms();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            File.WriteAllText("Logs/LeagueVR-dummy-placement.txt",report.ToString());CapturePreview();
            Debug.Log("Six new dummies positioned on clear ground in front of spawn and sized from body geometry.");
        }
        static List<Vector3> FindFrontPositions(GwenAbilities g)
        {
            var points=new List<Vector3>();var old=Object.FindObjectsByType<TrainingSentinel>(FindObjectsSortMode.None).Where(d=>!d.transform.IsChildOf(GameObject.Find(Group).transform)).Select(d=>d.transform.position).ToArray();
            foreach(float radius in new[]{7f,9f,11f})foreach(float angle in new[]{-24f,24f,-12f,12f,-40f,40f,-30f,30f,-18f,18f})
            {
                var at=g.spawn.position+Quaternion.Euler(0,angle,0)*Vector3.forward*radius;
                if(!Physics.Raycast(at+Vector3.up*2,Vector3.down,out var hit,4,g.worldMask,QueryTriggerInteraction.Ignore) || hit.normal.y<.85f)continue;
                var p=hit.point;if(old.Concat(points).Any(v=>Vector3.Distance(v,p)<1.8f))continue;
                if(Physics.CheckCapsule(p+Vector3.up*.5f,p+Vector3.up*1.5f,.4f,g.worldMask,QueryTriggerInteraction.Ignore))continue;
                if(Physics.Linecast(g.spawn.position+Vector3.up*1.3f,p+Vector3.up*1.1f,g.worldMask,QueryTriggerInteraction.Ignore))continue;
                points.Add(p);if(points.Count==6)return points;
            }
            return points;
        }
        static Bounds BodyBounds(Transform model,bool refreshSkinBounds)
        {
            var body=model.GetComponentsInChildren<Renderer>().First(r=>r.sharedMaterials.Any(m=>m && m.name.ToLowerInvariant().Contains("body")));
            if(body is SkinnedMeshRenderer skin)
            {
                // These FBXs are stationary bind poses. Mesh bounds avoid stale skinning matrices during edit-time resizing.
                var local=skin.sharedMesh.bounds;var b=new Bounds(skin.transform.TransformPoint(local.center),Vector3.zero);
                foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})foreach(int z in new[]{-1,1})
                    b.Encapsulate(skin.transform.TransformPoint(local.center+Vector3.Scale(local.extents,new Vector3(x,y,z))));
                if(refreshSkinBounds){skin.localBounds=local;skin.updateWhenOffscreen=true;PrefabUtility.RecordPrefabInstancePropertyModifications(skin);}
                return b;
            }
            return body.bounds;
        }
        static void FitBody(TrainingSentinel dummy)
        {
            var model=dummy.visuals.GetChild(0);var facing=Vector3.ProjectOnPlane(dummy.player.spawn.position-dummy.transform.position,Vector3.up);
            model.rotation=Quaternion.LookRotation(facing);
            var bounds=BodyBounds(model,false);model.localScale*=1.7f/bounds.size.y;bounds=BodyBounds(model,false);var p=dummy.transform.position;
            model.position+=new Vector3(p.x-bounds.center.x,p.y-bounds.min.y,p.z-bounds.center.z);
            foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                skin.localBounds=skin.sharedMesh.bounds;skin.updateWhenOffscreen=true;PrefabUtility.RecordPrefabInstancePropertyModifications(skin);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(model);
        }
        [MenuItem("Tools/League VR/Finish Gwen2 Gwen3 Textures")]
        public static void FinishTextures()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play Mode first.");
            if(SceneManager.GetActiveScene().path!=LeagueVRBuilder.ScenePath)
            {
                if(SceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Save the current scene before opening LeagueVR.");
                EditorSceneManager.OpenScene(LeagueVRBuilder.ScenePath);
            }
            Apply();
            var group=GameObject.Find(Group);var report=new StringBuilder("Gwen2 = GwenMod mesh; Gwen3 = GwenB mesh. Matched by vertex and index counts.\n");
            foreach(var dummy in group.GetComponentsInChildren<TrainingSentinel>())
            {
                int variant=dummy.name.StartsWith("Gwen2")?2:3;
                foreach(var r in dummy.visuals.GetComponentsInChildren<Renderer>(true))
                {
                    if(r is TMP_SubMesh || r is TMP_SubMeshUI || r.GetComponent<TMP_Text>())continue;
                    if(r.name=="Cube")continue;
                    var mats=r.sharedMaterials;
                    for(int i=0;i<mats.Length;i++)
                    {
                        if(!mats[i])throw new InvalidOperationException("Missing material on "+r.name);
                        string name=mats[i].name.ToLowerInvariant();
                        string kind=name.Contains("smear")?"Smears":name.Contains("scissors") || name.Contains("needle")?"Weapons":name.Contains("doll")?"Doll":name.Contains("body")?"Body":null;
                        if(kind==null)throw new InvalidOperationException("Unrecognized model material "+mats[i].name);
                        mats[i]=VariantMaterial(variant,kind);
                        if(kind=="Smears")r.enabled=false;
                        report.AppendLine(dummy.name+" / "+r.name+" -> "+mats[i].name);
                    }
                    r.sharedMaterials=mats;PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                }
            }
            AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            File.WriteAllText("Logs/LeagueVR-variant-textures.txt",report.ToString());CapturePreview();
            Debug.Log("Gwen2/Gwen3 textures assigned, exported cubes hidden, scene saved.");
        }
        static Material VariantMaterial(int variant,string kind)
        {
            string textureName=kind=="Body"?"Gwen"+variant+"Body":kind=="Doll"?"Gwen3Doll":kind=="Weapons"?"GwenWeapons":"GwenSmears";
            Texture texture;
            if(variant==2 && kind=="Doll")
            {
                var doll=AssetDatabase.LoadAllAssetsAtPath("Assets/_Game/Gwen/gwen.glb").OfType<Material>().First(m=>m.name=="Doll");
                texture=doll.GetTexture("baseColorTexture");
            }
            else
            {
                string texPath=Root+"/Art/GwenVariants/"+textureName+".png";
                var importer=AssetImporter.GetAtPath(texPath) as TextureImporter;if(!importer)throw new InvalidOperationException("Missing "+texPath);
                importer.sRGBTexture=true;importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=true;importer.mipmapEnabled=true;importer.maxTextureSize=4096;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
                texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            }
            if(!texture)throw new InvalidOperationException("Texture is missing for Gwen"+variant+" "+kind);
            string folder=Root+"/Generated/GwenVariants";Directory.CreateDirectory(folder);
            string path=folder+"/Gwen"+variant+" "+kind+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));AssetDatabase.CreateAsset(material,path);}
            material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",Color.white);material.SetFloat("_Cull",0);
            material.SetFloat("_AlphaClip",1);material.SetFloat("_Cutoff",.35f);material.EnableKeyword("_ALPHATEST_ON");material.renderQueue=2450;material.SetOverrideTag("RenderType","TransparentCutout");EditorUtility.SetDirty(material);return material;
        }
        [MenuItem("Tools/League VR/Add Gwen2 Gwen3 Dummies and Sound")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play Mode first.");
            var g=Object.FindFirstObjectByType<GwenAbilities>();if(!g)throw new InvalidOperationException("Open LeagueVR scene first.");
            var scene=SceneManager.GetActiveScene();EditorSceneManager.SaveScene(scene);Directory.CreateDirectory("PrototypeBackups");
            File.Copy(scene.path,"PrototypeBackups/"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-BeforeAdditionalDummies.unity");
            var report=new StringBuilder();
            var paths=new[]{"Assets/_Game/Gwen2/Untitled.fbx","Assets/_Game/Gwen3/Untitled1.fbx"};
            foreach(var path in paths)
            {
                var importer=AssetImporter.GetAtPath(path) as ModelImporter;
                if(!importer)throw new InvalidOperationException("Missing model: "+path);
                // Only the model is needed from these FBXs, not exported cameras or lights.
                if(importer.importCameras || importer.importLights){importer.importCameras=false;importer.importLights=false;importer.SaveAndReimport();}
                var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                report.AppendLine(path+" renderers="+asset.GetComponentsInChildren<Renderer>(true).Length+" clips="+AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Count());
                foreach(var r in asset.GetComponentsInChildren<Renderer>(true))report.AppendLine("  Renderer "+r.name+" bounds="+r.bounds);
                foreach(var m in asset.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m).Distinct())
                    report.AppendLine("  Material "+m.name+" shader="+m.shader.name+" mainTexture="+(m.mainTexture?m.mainTexture.name:"none"));
            }
            if(!GameObject.Find(Group))
            {
                Physics.SyncTransforms();var points=FindPositions(g);
                if(points.Count<6)throw new InvalidOperationException("Cannot place six dummies on clear ground. Found "+points.Count);
                var group=new GameObject(Group);
                Undo.RegisterCreatedObjectUndo(group,"Add Gwen2 and Gwen3 dummies");
                for(int i=0;i<6;i++)Create(g,group.transform,paths[i/3],i,points[i],report);
            }
            foreach(var dummy in GameObject.Find(Group).GetComponentsInChildren<TrainingSentinel>())
            {
                var model=dummy.visuals.GetChild(0);
                foreach(var r in model.GetComponentsInChildren<Renderer>(true))if(r.name=="Cube")
                {r.gameObject.SetActive(false);PrefabUtility.RecordPrefabInstancePropertyModifications(r.gameObject);}
                FitBody(dummy);
            }
            LeagueVRWorldRecovery.AddAudio(g);
            AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
            File.WriteAllText("Logs/LeagueVR-dummies-and-audio.txt",report.ToString()+"\nSix additional dummies installed. Champion sound clips connected. Map unchanged.\n");
            Selection.activeGameObject=GameObject.Find(Group);SceneView.lastActiveSceneView?.FrameSelected();
            CapturePreview();
            Debug.Log("Gwen2/Gwen3 practice dummies and champion sounds saved.");
        }
        static List<Vector3> FindPositions(GwenAbilities g)
        {
            var result=new List<Vector3>();var occupied=Object.FindObjectsByType<TrainingSentinel>(FindObjectsSortMode.None).Select(t=>t.transform.position).ToList();
            foreach(float radius in new[]{4f,6f,8f,10f,12f})foreach(float angle in new[]{-80f,80f,-110f,110f,-145f,145f,180f,-45f,45f,0f})
            {
                var at=g.spawn.position+Quaternion.Euler(0,angle,0)*Vector3.forward*radius;
                if(!Physics.Raycast(at+Vector3.up*2,Vector3.down,out var hit,4,g.worldMask,QueryTriggerInteraction.Ignore) || hit.normal.y<.85f)continue;
                var p=hit.point;
                if(occupied.Concat(result).Any(v=>Vector3.Distance(v,p)<2.3f))continue;
                if(Physics.CheckCapsule(p+Vector3.up*.5f,p+Vector3.up*1.5f,.4f,g.worldMask,QueryTriggerInteraction.Ignore))continue;
                if(Physics.Linecast(g.spawn.position+Vector3.up*1.3f,p+Vector3.up*1.3f,g.worldMask,QueryTriggerInteraction.Ignore))continue;
                result.Add(p);if(result.Count==6)return result;
            }
            return result;
        }
        static void Create(GwenAbilities g,Transform parent,string assetPath,int index,Vector3 position,StringBuilder report)
        {
            var go=new GameObject("Gwen"+(index/3+2)+" Dummy "+(index%3+1));go.transform.SetParent(parent,false);go.transform.position=position;go.layer=LayerMask.NameToLayer("LeagueCombat");
            var health=go.AddComponent<Combatant>();health.team=1;health.maxHealth=600;health.armor=25;health.magicResistance=25;
            var capsule=go.AddComponent<CapsuleCollider>();capsule.isTrigger=true;capsule.radius=.38f;capsule.height=1.7f;capsule.center=Vector3.up*.85f;
            var visuals=new GameObject("Dummy Visuals").transform;visuals.SetParent(go.transform,false);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(assetPath));model.transform.SetParent(visuals,false);
            model.transform.localPosition=Vector3.zero;
            foreach(var r in model.GetComponentsInChildren<Renderer>(true))if(r.name=="Cube")r.gameObject.SetActive(false);
            var facing=Vector3.ProjectOnPlane(g.spawn.position-position,Vector3.up);model.transform.rotation=Quaternion.LookRotation(facing)*model.transform.rotation;
            var renderers=model.GetComponentsInChildren<Renderer>().Where(r=>!r.sharedMaterials.Any(m=>m && m.name.ToLowerInvariant().Contains("smear"))).ToArray();if(renderers.Length==0)throw new InvalidOperationException("Model contains no renderers: "+assetPath);
            Bounds Bounds(){var b=renderers[0].bounds;foreach(var r in renderers.Skip(1))b.Encapsulate(r.bounds);return b;}
            var bounds=Bounds();if(bounds.size.y<.001f)throw new InvalidOperationException("Model has empty bounds.");
            model.transform.localScale*=1.7f/bounds.size.y;bounds=Bounds();model.transform.position+=new Vector3(position.x-bounds.center.x,position.y-bounds.min.y,position.z-bounds.center.z);
            foreach(var t in model.GetComponentsInChildren<Transform>(true))t.gameObject.layer=go.layer;
            // A stationary target needs the imported pose only; no root motion or extra controller rig.
            foreach(var animator in model.GetComponentsInChildren<Animator>(true)){animator.applyRootMotion=false;animator.enabled=false;}
            foreach(var animation in model.GetComponentsInChildren<Animation>(true))animation.enabled=false;
            var aim=new GameObject("Dummy aim point").transform;aim.SetParent(go.transform,false);aim.localPosition=Vector3.up*1.1f;health.aimPoint=aim;
            var labelGo=new GameObject("Dummy health");labelGo.transform.SetParent(visuals,false);labelGo.transform.localPosition=Vector3.up*2;labelGo.transform.localScale=Vector3.one*.04f;
            var label=labelGo.AddComponent<TextMeshPro>();label.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular SDF.asset");label.fontSize=12;label.alignment=TextAlignmentOptions.Center;label.rectTransform.sizeDelta=new Vector2(45,12);label.text=go.name+"\n600 / 600";label.color=new Color(1,.7f,.85f);
            var sentinel=go.AddComponent<TrainingSentinel>();sentinel.player=g;sentinel.visuals=visuals;sentinel.label=label;sentinel.attacksPlayer=false;sentinel.moves=false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);
            report.AppendLine(go.name+" grounded at "+position+" model bounds="+Bounds());
        }
        [MenuItem("Tools/League VR/Preview Additional Dummies")]
        public static void CapturePreview()
        {
            var group=GameObject.Find(Group);if(!group)return;Directory.CreateDirectory("Logs/DummyPreview");
            var temp=new GameObject("Temporary dummy preview");var cam=temp.AddComponent<Camera>();cam.enabled=false;cam.GetUniversalAdditionalCameraData().allowXRRendering=false;cam.cullingMask=1<<LayerMask.NameToLayer("LeagueCombat");cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.12f,.14f,.18f);cam.nearClipPlane=.05f;cam.farClipPlane=10;
            var rt=new RenderTexture(700,800,24);cam.targetTexture=rt;
            try{foreach(int i in new[]{0,3}){var target=group.transform.GetChild(i);var toward=(Object.FindFirstObjectByType<GwenAbilities>().spawn.position-target.position).normalized;var focus=target.position+Vector3.up*.9f;cam.transform.SetPositionAndRotation(focus+toward*2.3f,Quaternion.LookRotation(-toward));cam.Render();var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(700,800,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,700,800),0,0);image.Apply();File.WriteAllBytes("Logs/DummyPreview/Gwen"+(i/3+2)+".png",image.EncodeToPNG());RenderTexture.active=old;Object.DestroyImmediate(image);}}
            finally{cam.targetTexture=null;Object.DestroyImmediate(temp);rt.Release();Object.DestroyImmediate(rt);}
        }
        [MenuItem("Tools/League VR/Test Additional Dummies and Sound")]
        public static void Test(){if(EditorApplication.isPlayingOrWillChangePlaymode)return;SessionState.SetBool("LeagueVR.TestDummies",true);EditorApplication.isPlaying=true;}
        static void OnPlayState(PlayModeStateChange state)
        {if(state!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("LeagueVR.TestDummies",false))return;SessionState.SetBool("LeagueVR.TestDummies",false);Object.FindFirstObjectByType<GwenAbilities>().StartCoroutine(TestRoutine());}
        static IEnumerator TestRoutine()
        {
            var lines=new List<string>();void Check(bool pass,string name){lines.Add((pass?"PASS ":"FAIL ")+name);File.WriteAllLines("Logs/LeagueVR-dummy-audio-tests.txt",lines);}
            var g=Object.FindFirstObjectByType<GwenAbilities>();g.GetComponent<GwenVRInput>().enabled=false;
            yield return new WaitForSeconds(.5f);
            var group=GameObject.Find(Group);var dummies=group.GetComponentsInChildren<TrainingSentinel>();
            Check(dummies.Length==6,"Six additional dummies");
            foreach(int variant in new[]{2,3})Check(dummies.Count(d=>d.name.StartsWith("Gwen"+variant+" Dummy"))==3,"Three Gwen"+variant+" dummies");
            var chests=dummies.Select(d=>d.visuals.GetComponentsInChildren<Transform>().First(t=>t.name=="Chest")).ToArray();
            var rotations=chests.Select(t=>t.rotation).ToArray();
            yield return new WaitForSeconds(.25f);
            for(int i=0;i<dummies.Length;i++)
            {
                var a=dummies[i].visuals.GetComponentInChildren<Animation>();
                Check(a && a.IsPlaying("Idle.anm") && Quaternion.Angle(rotations[i],chests[i].rotation)>.001f,dummies[i].name+" plays Gwen idle and changes its pose");
            }
            CapturePreview();
            foreach(var d in dummies)
            {
                Check(!d.visuals.GetComponentsInChildren<Renderer>().Any(r=>r.name=="Cube"),d.name+" excludes the exported default cube");
                Check(d.visuals.GetComponentsInChildren<Renderer>().Where(r=>r.enabled && !r.GetComponent<TMP_Text>()).All(r=>r.sharedMaterials.All(m=>m && m.GetTexture("_BaseMap"))),d.name+" has texture assignments");
                Check(Physics.Raycast(d.transform.position+Vector3.up*.2f,Vector3.down,.5f,g.worldMask),d.name+" stands on map collision");
                var bounds=BodyBounds(d.visuals.GetChild(0),false);Check(Mathf.Abs(bounds.size.y-1.7f)<.03f && Mathf.Abs(bounds.min.y-d.transform.position.y)<.03f,d.name+" body is human-sized and feet are grounded");
                Check(d.visuals.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.enabled).All(r=>r.updateWhenOffscreen),d.name+" skinned renderers remain updated");
                var h=d.GetComponent<Combatant>();float hp=h.Health;h.TakeDamage(new DamageHit(g.Health,g.Feet,100,DamageKind.True));Check(Mathf.Abs(h.Health-(hp-100))<.01f,d.name+" receives damage");
                Check(d.visuals.GetComponentInChildren<Animation>().IsPlaying("Stunned"),d.name+" reacts to hits using Gwen's animation");
            }
            yield return new WaitForSeconds(.45f);
            Check(dummies.All(d=>d.visuals.GetComponentInChildren<Animation>().IsPlaying("Idle.anm")),"All six return to idle after a hit");
            var first=dummies[0];first.GetComponent<Combatant>().TakeDamage(new DamageHit(g.Health,g.Feet,10000,DamageKind.True));Check(!first.visuals.gameObject.activeSelf,"Defeated dummy hides");
            var audio=g.GetComponent<GwenAudio>();
            Check(audio && new[]{audio.scissors,audio.snip,audio.mist,audio.dash,audio.needle,audio.impact,audio.hurt,audio.footstep}.All(c=>c && c.samples>0),"All eight sound clips connected");
            Check(Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(a=>a.enabled)==1,"Exactly one active audio listener");
            Check(AudioListener.volume>0 && !AudioListener.pause,"Global audio is enabled");
            var map=GameObject.Find("Summoner's Rift");
            var original=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/SummonersRIft/summoner_rift_3d_export.glb");
            var sourceRenderers=original.GetComponentsInChildren<Renderer>();
            Check(map.GetComponentsInChildren<Renderer>().All(r=>sourceRenderers.Any(s=>s.name==r.name && s.sharedMaterials.SequenceEqual(r.sharedMaterials))),"Map keeps original GLB materials");
            audio.Play(audio.mist);yield return null;Check(audio.EffectsSource.isPlaying && !audio.EffectsSource.mute && audio.EffectsSource.volume>0,"Champion audio source plays sound");
            yield return new WaitForSeconds(.1f);var samples=new float[1024];audio.EffectsSource.GetOutputData(samples,0);Check(samples.Any(v=>Mathf.Abs(v)>.000001f),"Champion audio produces a nonzero signal");
            yield return new WaitForSeconds(5.1f);Check(first.GetComponent<Combatant>().IsAlive && first.visuals.gameObject.activeSelf,"Dummy respawns after defeat");
            Debug.Log("DUMMY/AUDIO TESTS: "+lines.Count(l=>l.StartsWith("PASS"))+" passed, "+lines.Count(l=>l.StartsWith("FAIL"))+" failed.");
            EditorApplication.delayCall+=()=>EditorApplication.isPlaying=false;
        }
    }
}
