using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace LeagueVR.Editor
{
    [InitializeOnLoad]
    public static class LeagueVRVariantAnimation
    {
        static LeagueVRVariantAnimation(){}
        class Bone
        {
            public Transform source,target;public Quaternion offset;public Vector3 sourcePosition,targetPosition,targetScale;
            public Quaternion sourceRotation;public Quaternion positionAxes;public float positionScale;
            public string path;
            public List<Keyframe>[] keys=Enumerable.Range(0,10).Select(_=>new List<Keyframe>()).ToArray();
        }
        [MenuItem("Tools/League VR/Animate Gwen2 Gwen3 Dummies")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play Mode first.");
            var group=GameObject.Find("Gwen2 and Gwen3 Practice");if(!group)throw new InvalidOperationException("Open LeagueVR scene first.");
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());Directory.CreateDirectory("PrototypeBackups");
            File.Copy(SceneManager.GetActiveScene().path,"PrototypeBackups/"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-BeforeDummyAnimations.unity");
            var sourceAsset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Gwen/gwen.glb");
            var names=new[]{"Idle.anm","Idle2.anm","Idle3.anm","Run.anm","Attack1","Stunned","Death","Respawn"};
            var sourceClips=AssetDatabase.LoadAllAssetsAtPath("Assets/_Game/Gwen/gwen.glb").OfType<AnimationClip>().Where(c=>names.Contains(c.name)).ToArray();
            if(sourceClips.Length!=names.Length)throw new InvalidOperationException("Required Gwen animations missing.");
            var report=new StringBuilder();
            foreach(int variant in new[]{2,3})
            {
                var path=variant==2?"Assets/_Game/Gwen2/Untitled.fbx":"Assets/_Game/Gwen3/Untitled1.fbx";
                var targetAsset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var clips=sourceClips.Select(c=>Bake(sourceAsset,targetAsset,c,variant,report)).ToArray();
                foreach(var d in group.GetComponentsInChildren<TrainingSentinel>().Where(d=>d.name.StartsWith("Gwen"+variant)))
                {
                    var model=d.visuals.GetChild(0).gameObject;
                    var anim=model.GetComponent<Animation>();if(!anim)anim=model.AddComponent<Animation>();anim.enabled=true;
                    for(int i=0;i<clips.Length;i++)anim.AddClip(clips[i],sourceClips[i].name);
                    anim.clip=clips[Array.FindIndex(sourceClips,c=>c.name=="Idle.anm")];anim.wrapMode=WrapMode.Loop;anim.playAutomatically=true;anim.cullingType=AnimationCullingType.AlwaysAnimate;
                    var reactions=model.GetComponent<PracticeDummyAnimation>();if(!reactions)reactions=model.AddComponent<PracticeDummyAnimation>();reactions.health=d.GetComponent<Combatant>();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(anim);PrefabUtility.RecordPrefabInstancePropertyModifications(reactions);
                    // The animated body can leave its bind-pose bounds. Keep all six visible while posing.
                    foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>()){skin.updateWhenOffscreen=true;PrefabUtility.RecordPrefabInstancePropertyModifications(skin);}
                }
            }
            AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());File.WriteAllText("Logs/LeagueVR-variant-animation.txt",report.ToString());
            Debug.Log("Gwen2/Gwen3 dummies now reuse Gwen idle, run, attack, stunned, death and respawn clips with rig conversion.");
        }
        static AnimationClip Bake(GameObject sourceAsset,GameObject targetAsset,AnimationClip original,int variant,StringBuilder report)
        {
            var source=Object.Instantiate(sourceAsset);var target=Object.Instantiate(targetAsset);
            source.hideFlags=target.hideFlags=HideFlags.HideAndDontSave;
            foreach(var r in source.GetComponentsInChildren<Renderer>())r.enabled=false;
            foreach(var r in target.GetComponentsInChildren<Renderer>())r.enabled=false;
            foreach(var a in source.GetComponentsInChildren<Animation>())a.enabled=false;
            foreach(var a in target.GetComponentsInChildren<Animator>())a.enabled=false;
            var sourceBones=source.GetComponentsInChildren<Transform>().Where(t=>t!=source.transform).ToDictionary(t=>t.name);
            var targetBones=target.GetComponentsInChildren<SkinnedMeshRenderer>().First().bones.Distinct().ToArray();
            var bones=target.GetComponentsInChildren<Transform>().Where(t=>targetBones.Contains(t) && sourceBones.ContainsKey(t.name)).Select(t=>
            {
                var s=sourceBones[t.name];return new Bone{source=s,target=t,offset=Quaternion.Inverse(s.rotation)*t.rotation,
                    sourcePosition=s.localPosition,sourceRotation=s.localRotation,targetPosition=t.localPosition,targetScale=t.localScale,
                    positionAxes=Quaternion.Inverse(t.parent.rotation)*s.parent.rotation,
                    positionScale=s.localPosition.magnitude>.000001f?t.localPosition.magnitude/s.localPosition.magnitude:.01f,
                    path=AnimationUtility.CalculateTransformPath(t,target.transform)};
            }).ToArray();
            var allSource=source.GetComponentsInChildren<Transform>();var rest=allSource.Select(t=>(t.localPosition,t.localRotation,t.localScale)).ToArray();
            string[] properties={"m_LocalPosition.x","m_LocalPosition.y","m_LocalPosition.z","m_LocalRotation.x","m_LocalRotation.y","m_LocalRotation.z","m_LocalRotation.w","m_LocalScale.x","m_LocalScale.y","m_LocalScale.z"};
            try
            {
                int frames=Mathf.CeilToInt(original.length*30);
                for(int frame=0;frame<=frames;frame++)
                {
                    for(int i=0;i<allSource.Length;i++){allSource[i].localPosition=rest[i].Item1;allSource[i].localRotation=rest[i].Item2;allSource[i].localScale=rest[i].Item3;}
                    float time=Mathf.Min(frame/30f,original.length);original.SampleAnimation(source,time);
                    foreach(var b in bones)
                    {
                        b.target.rotation=b.source.rotation*b.offset;
                        b.target.localPosition=b.targetPosition+b.positionAxes*(b.source.localPosition-b.sourcePosition)*b.positionScale;
                        b.target.localScale=b.targetScale;
                        var p=b.target.localPosition;var q=b.target.localRotation;var scale=b.target.localScale;
                        if(frame>0 && Quaternion.Dot(q,new Quaternion(b.keys[3][frame-1].value,b.keys[4][frame-1].value,b.keys[5][frame-1].value,b.keys[6][frame-1].value))<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
                        float[] values={p.x,p.y,p.z,q.x,q.y,q.z,q.w,scale.x,scale.y,scale.z};
                        for(int j=0;j<10;j++)b.keys[j].Add(new Keyframe(time,values[j]));
                    }
                }
                var clip=new AnimationClip{name=original.name,legacy=true,frameRate=30,wrapMode=original.name.StartsWith("Idle")||original.name=="Run.anm"?WrapMode.Loop:WrapMode.Once};
                var bindings=new List<EditorCurveBinding>();var curves=new List<AnimationCurve>();
                foreach(var b in bones)for(int j=0;j<10;j++)
                {
                    var curve=new AnimationCurve(b.keys[j].ToArray());
                    for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}
                    bindings.Add(EditorCurveBinding.FloatCurve(b.path,typeof(Transform),properties[j]));curves.Add(curve);
                }
                AnimationUtility.SetEditorCurves(clip,bindings.ToArray(),curves.ToArray());clip.EnsureQuaternionContinuity();
                string folder="Assets/_Game/LeagueVR/Generated/GwenVariants/Animations/Gwen"+variant;Directory.CreateDirectory(folder);
                string output=folder+"/"+original.name.Replace(".anm","")+".anim";var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(output);
                if(existing){EditorUtility.CopySerialized(clip,existing);Object.DestroyImmediate(clip);clip=existing;}else AssetDatabase.CreateAsset(clip,output);
                clip.name=original.name;EditorUtility.SetDirty(clip);
                report.AppendLine("Gwen"+variant+" "+original.name+" mapped bones="+bones.Length+" / "+targetBones.Length+" frames="+(frames+1));return clip;
            }
            finally{Object.DestroyImmediate(source);Object.DestroyImmediate(target);}
        }
        [MenuItem("Tools/League VR/Inspect Variant Animation Rigs")]
        public static void Inspect()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            var report=new StringBuilder();
            foreach(var path in new[]{"Assets/_Game/Gwen/gwen.glb","Assets/_Game/Gwen2/Untitled.fbx","Assets/_Game/Gwen3/Untitled1.fbx"})
            {
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);report.AppendLine(path);
                foreach(var t in model.GetComponentsInChildren<Transform>(true))
                    report.AppendLine(AnimationUtility.CalculateTransformPath(t,model.transform)+" pos="+t.localPosition.ToString("F4")+" rot="+t.localRotation.ToString("F4")+" scale="+t.localScale.ToString("F4"));
                foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    report.AppendLine("SKIN "+skin.name+" root="+skin.rootBone?.name+" bones="+string.Join(",",skin.bones.Select(b=>b?b.name:"NULL"))+" mesh="+skin.sharedMesh.bounds);
            }
            var clip=AssetDatabase.LoadAllAssetsAtPath("Assets/_Game/Gwen/gwen.glb").OfType<AnimationClip>().First(c=>c.name=="Idle.anm");
            foreach(var b in AnimationUtility.GetCurveBindings(clip))report.AppendLine("CURVE "+b.path+" "+b.propertyName);
            Directory.CreateDirectory("Logs");File.WriteAllText("Logs/LeagueVR-variant-rigs.txt",report.ToString());
        }
    }
}
