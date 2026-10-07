using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering.Universal;
using LeagueVR.Match;
using Object=UnityEngine.Object;
namespace LeagueVR.Champions.Editor
{
    [Serializable]class RosterSource{public SourceChampion[] champions;}
    [Serializable]class SourceSpell{public string name,vrDescription,icon;public float[] cooldowns,costs;}
    [Serializable]class SourceChampion
    {
        public int id;public string name,title,role,passiveName,passiveDescription,patch,portrait,passiveIcon;public Color color;
        public float health,healthGrowth,armor,armorGrowth,magicResist,magicResistGrowth,mana,manaGrowth,attackDamage,attackGrowth,attackSpeed,attackSpeedGrowth,moveSpeed,healthRegen,manaRegen,attackReach;
        public SourceSpell[] spells;
    }
    [InitializeOnLoad]public static class ChampionWork
    {
        static double next;
        static ChampionWork(){EditorApplication.update+=Tick;}
        static void Tick()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.5;
            const string path="Temp/LeagueChampions.request";if(!File.Exists(path))return;string request=File.ReadAllText(path).Trim();File.Delete(path);Directory.CreateDirectory("Logs/ChampionExpansion");
            try{if(request=="Build")ChampionBuild.Build();else if(request=="Survey")ChampionBuild.Survey();else if(request=="Test1")ChampionTests.Begin(1);else if(request=="Test2")ChampionTests.Begin(2);else if(request=="Test3")ChampionTests.Begin(3);else if(request=="Test4")ChampionTests.Begin(4);else throw new Exception("Unknown request");}
            catch(Exception e){File.WriteAllText("Logs/ChampionExpansion/error.txt",e.ToString());Debug.LogException(e);}
        }
    }
    public static class ChampionBuild
    {
        const string Base="Assets/_Game/LeagueVR/Champions";
        static void Folder(string path){Directory.CreateDirectory(path);}
        public static void Build()
        {
            if(EditorApplication.isPlaying)throw new Exception("Build from edit mode");
            var m=Object.FindAnyObjectByType<RiftMatch>();if(!m)throw new Exception("LeagueVR scene must be open");var p=m.player;
            Folder(Base+"/Data");Folder(Base+"/Prefabs");Folder(Base+"/Meshes");Folder(Base+"/Materials");AssetDatabase.Refresh();
            var source=JsonUtility.FromJson<RosterSource>(File.ReadAllText(Base+"/RosterSource.json"));var definitions=new List<ChampionDefinition>();var log=new StringBuilder();
            foreach(var entry in source.champions)
            {
                string dir=Base+"/Art/"+entry.name;
                var d=AssetDatabase.LoadAssetAtPath<ChampionDefinition>(Base+"/Data/"+entry.name+".asset");if(!d){d=ScriptableObject.CreateInstance<ChampionDefinition>();AssetDatabase.CreateAsset(d,Base+"/Data/"+entry.name+".asset");}
                d.id=(ChampionId)entry.id;d.title=entry.title;d.role=entry.role;d.passiveName=entry.passiveName;d.passiveDescription=entry.passiveDescription;d.patch=entry.patch;d.color=entry.color;
                d.health=entry.health;d.healthGrowth=entry.healthGrowth;d.armor=entry.armor;d.armorGrowth=entry.armorGrowth;d.magicResist=entry.magicResist;d.magicResistGrowth=entry.magicResistGrowth;d.mana=entry.mana;d.manaGrowth=entry.manaGrowth;d.attackDamage=entry.attackDamage;d.attackGrowth=entry.attackGrowth;d.attackSpeed=entry.attackSpeed;d.attackSpeedGrowth=entry.attackSpeedGrowth;d.moveSpeed=entry.moveSpeed;d.healthRegen=entry.healthRegen;d.manaRegen=entry.manaRegen;d.attackReach=entry.attackReach;
                d.portrait=AssetDatabase.LoadAssetAtPath<Texture2D>(dir+"/"+entry.portrait);d.passiveIcon=AssetDatabase.LoadAssetAtPath<Texture2D>(dir+"/"+entry.passiveIcon);
                d.spells=entry.spells.Select(s=>new ChampionSpell{name=s.name,vrDescription=s.vrDescription,cooldowns=s.cooldowns,costs=s.costs,icon=AssetDatabase.LoadAssetAtPath<Texture2D>(dir+"/"+s.icon)}).ToArray();
                if(d.id==ChampionId.Gwen){var g=p.GetComponent<GwenAvatar>();d.health=p.Health?p.Health.maxHealth:p.GetComponent<Combatant>().maxHealth;d.armor=p.GetComponent<Combatant>().armor;d.magicResist=p.GetComponent<Combatant>().magicResistance;d.attackDamage=p.tuning.attackDamage;d.attackSpeed=1/p.tuning.attackInterval;d.attackReach=p.tuning.attackReach;}
                else
                {
                    string glb=dir+"/"+entry.name+".glb";var importer=AssetImporter.GetAtPath(glb);var settings=new SerializedObject(importer);var method=settings.FindProperty("importSettings.animationMethod");if(method!=null&&method.intValue!=1){method.intValue=1;settings.ApplyModifiedPropertiesWithoutUndo();importer.SaveAndReimport();}
                    var sourceModel=AssetDatabase.LoadAssetAtPath<GameObject>(glb);if(!sourceModel)throw new Exception("Missing imported model "+glb);
                    var clips=AssetDatabase.LoadAllAssetsAtPath(glb).OfType<AnimationClip>().ToArray();d.idle=clips.FirstOrDefault(c=>c.name.StartsWith("Idle1",StringComparison.OrdinalIgnoreCase))??clips.FirstOrDefault(c=>c.name.StartsWith("Idle",StringComparison.OrdinalIgnoreCase));
                    if(!d.idle)throw new Exception("Idle missing "+d.name);
                    GameObject full=null,fp=null;try {full=new GameObject(entry.name);var model=Object.Instantiate(sourceModel,full.transform);model.name=sourceModel.name;
                    foreach(var animation in full.GetComponentsInChildren<Animation>()){animation.Stop();animation.enabled=false;}foreach(var animator in full.GetComponentsInChildren<Animator>())animator.enabled=false;
                    d.idle.SampleAnimation(model,0);
                    Transform Bone(string name)=>full.GetComponentsInChildren<Transform>().FirstOrDefault(t=>string.Equals(t.name,name,StringComparison.OrdinalIgnoreCase));
                    var ls=Bone("L_Shoulder");var rs=Bone("R_Shoulder");if(rs.position.x-ls.position.x<0)model.transform.localRotation=Quaternion.Euler(0,180,0);
                    var materials=new Dictionary<Material,Material>();
                    foreach(var renderer in full.GetComponentsInChildren<Renderer>())renderer.sharedMaterials=renderer.sharedMaterials.Select(original=>
                    {
                        if(materials.TryGetValue(original,out var mat))return mat;mat=AssetDatabase.LoadAssetAtPath<Material>(Base+"/Materials/"+entry.name+"-"+Clean(original.name)+".mat");
                        if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));AssetDatabase.CreateAsset(mat,Base+"/Materials/"+entry.name+"-"+Clean(original.name)+".mat");}
                        Texture tex=original.mainTexture;foreach(string property in original.GetTexturePropertyNames())if(property.ToLowerInvariant().Contains("basecolor")||property=="_MainTex"){var t=original.GetTexture(property);if(t)tex=t;}
                        if(!tex)throw new Exception("Base color absent on "+entry.name+"/"+original.name);mat.SetTexture("_BaseMap",tex);mat.SetColor("_BaseColor",Color.white);mat.SetFloat("_Cull",0);EditorUtility.SetDirty(mat);materials[original]=mat;return mat;
                    }).ToArray();
                    d.model=PrefabUtility.SaveAsPrefabAsset(full,Base+"/Prefabs/"+entry.name+".prefab");
                    fp=Object.Instantiate(full);fp.name=entry.name+" FP Arms";
                    foreach(var renderer in fp.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        if(d.id==ChampionId.Brand||d.id==ChampionId.Yunara){BuildFloatingHands(renderer,entry.name,log);renderer.enabled=false;continue;}
                        var original=renderer.sharedMesh;var mesh=Object.Instantiate(original);mesh.name=entry.name+" first person mesh";
                        var weights=mesh.boneWeights;var arms=new HashSet<int>();var bones=renderer.bones;
                        for(int i=0;i<bones.Length;i++){var n=bones[i].name.ToLowerInvariant();bool hand=n=="l_hand"||n=="r_hand";bool arm=n.Contains("elbow")||n.Contains("forearm")||n.Contains("uparm")||n.Contains("shoulder")||n.Contains("arm_twist")||n.Contains("hand_twist")||n.EndsWith("_bracelet");bool finger=new[]{"_thumb","_index","_middle","_ring","_pinky"}.Any(s=>n.Contains(s));if(hand||finger||arm)arms.Add(i);}
                        bool OnArm(int i){var w=weights[i];float sum=0;if(arms.Contains(w.boneIndex0))sum+=w.weight0;if(arms.Contains(w.boneIndex1))sum+=w.weight1;if(arms.Contains(w.boneIndex2))sum+=w.weight2;if(arms.Contains(w.boneIndex3))sum+=w.weight3;return sum>.55f;}
                        int kept=0;
                        for(int sub=0;sub<mesh.subMeshCount;sub++)
                        {
                            string mat=renderer.sharedMaterials[sub].name.ToLowerInvariant();bool weapon=(d.id==ChampionId.Aatrox&&mat.Contains("sword"))||(d.id==ChampionId.Akshan&&mat.EndsWith("weapon"))||(d.id==ChampionId.Pantheon&&(mat.EndsWith("shield")||mat.EndsWith("spear")));
                            var triangles=mesh.GetTriangles(sub);var retained=new List<int>();for(int t=0;t<triangles.Length;t+=3)if(weapon||(OnArm(triangles[t])&&OnArm(triangles[t+1])&&OnArm(triangles[t+2]))){retained.Add(triangles[t]);retained.Add(triangles[t+1]);retained.Add(triangles[t+2]);}mesh.SetTriangles(retained,sub);kept+=retained.Count/3;
                        }
                        
                        string meshPath=Base+"/Meshes/"+entry.name+"-"+Clean(renderer.name)+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);if(existing){CopySkinnedMesh(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;}else AssetDatabase.CreateAsset(mesh,meshPath);renderer.sharedMesh=mesh;renderer.updateWhenOffscreen=true;renderer.localBounds=new Bounds(Vector3.zero,Vector3.one*12);
                        log.AppendLine(entry.name+": FP triangles="+kept+" source vertices="+original.vertexCount+" bones="+bones.Length+" textures="+renderer.sharedMaterials.Count(mat=>mat.mainTexture));if(kept<100)throw new Exception("Arm selection empty for "+entry.name);
                    }
                    Transform FpBone(string name)=>fp.GetComponentsInChildren<Transform>().FirstOrDefault(t=>string.Equals(t.name,name,StringComparison.OrdinalIgnoreCase));
                    if(d.id==ChampionId.Aatrox||d.id==ChampionId.Akshan)FpBone("Weapon")?.SetParent(FpBone("R_Hand"),true);
                    if(d.id==ChampionId.Pantheon){FpBone("Shield")?.SetParent(FpBone("L_Hand"),true);FpBone("Spear")?.SetParent(FpBone("R_Hand"),true);}
                    d.firstPerson=PrefabUtility.SaveAsPrefabAsset(fp,Base+"/Prefabs/"+entry.name+"-FP.prefab");} finally {if(fp)Object.DestroyImmediate(fp);if(full)Object.DestroyImmediate(full);}
                    log.AppendLine(entry.name+": animations="+clips.Length+" idle="+d.idle.name+" source="+glb);
                }
                EditorUtility.SetDirty(d);definitions.Add(d);
            }
            var roster=p.GetComponent<ChampionRoster>();if(!roster)roster=p.gameObject.AddComponent<ChampionRoster>();roster.player=p;roster.champions=definitions.ToArray();roster.selected=0;
            var abilities=p.GetComponent<ChampionAbilities>();if(!abilities)abilities=p.gameObject.AddComponent<ChampionAbilities>();roster.abilities=abilities;
            var avatar=p.GetComponent<ChampionVRAvatar>();if(!avatar)avatar=p.gameObject.AddComponent<ChampionVRAvatar>();avatar.player=p;roster.avatar=avatar;
            EditorUtility.SetDirty(roster);EditorUtility.SetDirty(avatar);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(m.gameObject.scene);EditorSceneManager.SaveScene(m.gameObject.scene);
            File.WriteAllText("Logs/ChampionExpansion/Build.txt",log.ToString());File.WriteAllText("Logs/ChampionExpansion/error.txt",string.Empty);Debug.Log("Seven champion roster saved.");
        }
        // Reassign the native buffers as well as skinning data when updating an existing asset.
        // CopySerialized alone can leave a renderer using the previous graphics buffers.
        static void CopySkinnedMesh(Mesh source,Mesh target)
        {
            target.Clear(false);target.indexFormat=source.indexFormat;
            target.vertices=source.vertices;target.uv=source.uv;
            if(source.normals.Length==source.vertexCount)target.normals=source.normals;
            if(source.tangents.Length==source.vertexCount)target.tangents=source.tangents;
            target.bindposes=source.bindposes;target.boneWeights=source.boneWeights;
            target.subMeshCount=source.subMeshCount;
            for(int sub=0;sub<source.subMeshCount;sub++)target.SetTriangles(source.GetTriangles(sub),sub);
            if(source.normals.Length!=source.vertexCount)target.RecalculateNormals();
            target.bounds=source.bounds;target.UploadMeshData(false);EditorUtility.SetDirty(target);
        }
        struct HandVertex
        {
            public Vector3 position,normal;public Vector2 uv;
            public static HandVertex Lerp(HandVertex a,HandVertex b,float t)=>new HandVertex{position=Vector3.Lerp(a.position,b.position,t),normal=Vector3.Lerp(a.normal,b.normal,t).normalized,uv=Vector2.Lerp(a.uv,b.uv,t)};
        }
        // Use the original bind-pose shape in controller space, clip at the wrist, and close the cut. Finger tips
        // can be farther from the wrist than the forearm, so a small spherical crop is unsuitable.
        static void BuildFloatingHands(SkinnedMeshRenderer renderer,string champion,StringBuilder log)
        {
            var source=renderer.sharedMesh;var baked=Object.Instantiate(source);var bones=renderer.bones;var weights=source.boneWeights;var bindposes=source.bindposes;
            var world=baked.vertices.Select(v=>renderer.transform.TransformPoint(v)).ToArray();if(baked.normals.Length!=baked.vertexCount)baked.RecalculateNormals();var normals=baked.normals;var uvs=source.uv;
            foreach(string side in new[]{"L","R"})
            {
                var palm=bones.First(b=>string.Equals(b.name,side+"_Hand",StringComparison.OrdinalIgnoreCase));
                Vector3 RestPosition(Transform bone)=>renderer.transform.TransformPoint(bindposes[Array.IndexOf(bones,bone)].inverse.MultiplyPoint3x4(Vector3.zero));
                var middle=bones.First(b=>b.name.StartsWith(side+"_Middle",StringComparison.OrdinalIgnoreCase));var indexFinger=bones.First(b=>b.name.StartsWith(side+"_Index",StringComparison.OrdinalIgnoreCase));var pinky=bones.First(b=>b.name.StartsWith(side+"_Pinky",StringComparison.OrdinalIgnoreCase));
                var wrist=RestPosition(palm);var forward=(RestPosition(middle)-wrist).normalized;var up=Vector3.Cross(RestPosition(indexFinger)-RestPosition(pinky),forward).normalized;if(Vector3.Dot(up,Vector3.up)<0)up=-up;var frame=Quaternion.LookRotation(forward,up);var inverseFrame=Quaternion.Inverse(frame);
                log.AppendLine(champion+" "+side+" bind wrist="+wrist.ToString("F4")+" middle="+RestPosition(middle).ToString("F4")+" frame="+frame.eulerAngles+" renderer scale="+renderer.transform.lossyScale);
                float cut=-.025f;var vertices=new List<Vector3>();var normalList=new List<Vector3>();var uvList=new List<Vector2>();var submeshes=new List<int[]>();int keptCount=0;
                bool ArmBone(int index){string n=bones[index].name.ToLowerInvariant();return n.StartsWith(side.ToLowerInvariant()+"_")&&(n==side.ToLowerInvariant()+"_hand"||new[]{"_thumb","_index","_middle","_ring","_pinky"}.Any(part=>n.Contains(part)));}
                bool ArmVertex(int vertex){var w=weights[vertex];float sum=0;if(ArmBone(w.boneIndex0))sum+=w.weight0;if(ArmBone(w.boneIndex1))sum+=w.weight1;if(ArmBone(w.boneIndex2))sum+=w.weight2;if(ArmBone(w.boneIndex3))sum+=w.weight3;return sum>.5f;}
                HandVertex Vertex(int i)=>new HandVertex{position=world[i],normal=renderer.transform.TransformDirection(normals[i]).normalized,uv=uvs[i]};
                int Add(HandVertex v){int index=vertices.Count;vertices.Add(inverseFrame*(v.position-wrist));normalList.Add((inverseFrame*v.normal).normalized);uvList.Add(v.uv);return index;}
                for(int sub=0;sub<source.subMeshCount;sub++)
                {
                    var tris=source.GetTriangles(sub);var kept=new List<int>();var boundary=new List<(HandVertex a,HandVertex b)>();
                    for(int i=0;i<tris.Length;i+=3)
                    {
                        int a=tris[i],b=tris[i+1],c=tris[i+2];var centre=(world[a]+world[b]+world[c])/3;
                        if(!(ArmVertex(a)||ArmVertex(b)||ArmVertex(c))||Vector3.Distance(centre,wrist)>.27f)continue;
                        var input=new[]{Vertex(a),Vertex(b),Vertex(c)};var polygon=new List<HandVertex>();var cuts=new List<HandVertex>();
                        for(int edge=0;edge<3;edge++)
                        {
                            var first=input[edge];var next=input[(edge+1)%3];float da=Vector3.Dot(first.position-wrist,forward)-cut,db=Vector3.Dot(next.position-wrist,forward)-cut;
                            if(da>=0)polygon.Add(first);if((da>=0)!=(db>=0)){var crossing=HandVertex.Lerp(first,next,da/(da-db));polygon.Add(crossing);cuts.Add(crossing);}
                        }
                        if(cuts.Count==2)boundary.Add((cuts[0],cuts[1]));
                        for(int n=1;n<polygon.Count-1;n++){kept.Add(Add(polygon[0]));kept.Add(Add(polygon[n]));kept.Add(Add(polygon[n+1]));}
                    }
                    if(boundary.Count>0)
                    {
                        var centre=boundary.Aggregate(Vector3.zero,(v,e)=>v+e.a.position+e.b.position)/(boundary.Count*2);var uv=boundary.Aggregate(Vector2.zero,(v,e)=>v+e.a.uv+e.b.uv)/(boundary.Count*2);
                        var cap=new HandVertex{position=centre,normal=-forward,uv=uv};
                        foreach(var edge in boundary){var a=edge.a;var b=edge.b;a.normal=b.normal=-forward;if(Vector3.Dot(Vector3.Cross(a.position-centre,b.position-centre),-forward)<0)(a,b)=(b,a);kept.Add(Add(cap));kept.Add(Add(a));kept.Add(Add(b));}
                    }
                    submeshes.Add(kept.ToArray());keptCount+=kept.Count/3;
                }
                var handMesh=new Mesh{name=champion+" "+side+" tracked hand"};handMesh.SetVertices(vertices);handMesh.SetNormals(normalList);handMesh.SetUVs(0,uvList);handMesh.subMeshCount=submeshes.Count;for(int sub=0;sub<submeshes.Count;sub++)handMesh.SetTriangles(submeshes[sub],sub);handMesh.RecalculateBounds();
                string path=Base+"/Meshes/"+champion+"-"+side+"-TrackedHand.asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing){existing.Clear();existing.SetVertices(vertices);existing.SetNormals(normalList);existing.SetUVs(0,uvList);existing.subMeshCount=submeshes.Count;for(int sub=0;sub<submeshes.Count;sub++)existing.SetTriangles(submeshes[sub],sub);existing.RecalculateBounds();existing.UploadMeshData(false);EditorUtility.SetDirty(existing);Object.DestroyImmediate(handMesh);handMesh=existing;}else AssetDatabase.CreateAsset(handMesh,path);
                var hand=new GameObject(side+" original tracked hand");hand.transform.SetParent(palm,false);hand.transform.localRotation=Quaternion.Inverse(ChampionVRAvatar.Alignment(palm));hand.AddComponent<MeshFilter>().sharedMesh=handMesh;var visible=hand.AddComponent<MeshRenderer>();visible.sharedMaterials=renderer.sharedMaterials;visible.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                log.AppendLine(champion+": "+side+" neutral hand triangles="+keptCount+" vertices="+vertices.Count+" sealed wrist; bounds="+handMesh.bounds.size);if(keptCount<100)throw new Exception("Incomplete source hand "+champion+" "+side);
            }
            Object.DestroyImmediate(baked);
        }
        static string Clean(string n)=>string.Concat(n.Select(c=>char.IsLetterOrDigit(c)||c=='-'?c:'_'));
        public static void Survey(){var m=Object.FindAnyObjectByType<RiftMatch>();var roster=m.player.GetComponent<ChampionRoster>();var b=new StringBuilder();foreach(var d in roster.champions)b.AppendLine(d.name+": "+d.title+" HP "+d.health+" mana "+d.mana+" model="+d.model+" FP="+d.firstPerson+" spells="+d.spells.Length);Physics.SyncTransforms();foreach(var point in m.lanes[1].points){if(m.structures.Any(t=>GwenAbilities.FlatDistance(t.transform.position,point)<9))continue;bool clear=true;for(int n=0;n<12;n++){float a=n*Mathf.PI/6;var test=point+new Vector3(Mathf.Cos(a)*6,.3f,Mathf.Sin(a)*6);if(!m.Ground(test,out var ground)||Mathf.Abs(ground.y-point.y)>.6f||Physics.CheckSphere(ground+Vector3.up*.6f,.3f,m.player.worldMask,QueryTriggerInteraction.Ignore)){clear=false;break;}}if(clear)b.AppendLine("Clear arena: "+point);}File.WriteAllText("Logs/ChampionExpansion/Survey.txt",b.ToString());}
        public static void Capture(Camera camera,string name)
        {
            camera.GetUniversalAdditionalCameraData().allowXRRendering=false;var rt=new RenderTexture(1600,1000,24);camera.targetTexture=rt;var before=RenderTexture.active;camera.Render();RenderTexture.active=rt;var tex=new Texture2D(1600,1000,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1600,1000),0,0);tex.Apply();File.WriteAllBytes("Logs/ChampionExpansion/"+name+".png",tex.EncodeToPNG());RenderTexture.active=before;camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);
        }
    }
}
