from pathlib import Path
p=Path(r'F:\Vr\VR quirky testing gpt astra\League of legends test\league vr imoport testing\Assets\_Game\LeagueVR\Champions\Editor\ChampionBuild.cs');s=p.read_text();old='if(existing){existing.Clear();EditorUtility.CopySerialized(mesh,existing);existing.UploadMeshData(false);EditorUtility.SetDirty(existing);Object.DestroyImmediate(mesh);mesh=existing;}';new='if(existing){CopySkinnedMesh(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;}';assert old in s;s=s.replace(old,new)
needle='        struct HandVertex\n';method='''        // Reassign the native buffers as well as skinning data when updating an existing asset.
        // CopySerialized alone can leave a renderer using the previous graphics buffers.
        static void CopySkinnedMesh(Mesh source,Mesh target)
        {
            target.Clear(false);target.indexFormat=source.indexFormat;
            target.vertices=source.vertices;target.uv=source.uv;
            if(source.normals.Length==source.vertexCount)target.normals=source.normals;else target.RecalculateNormals();
            if(source.tangents.Length==source.vertexCount)target.tangents=source.tangents;
            target.bindposes=source.bindposes;target.boneWeights=source.boneWeights;
            target.subMeshCount=source.subMeshCount;
            for(int sub=0;sub<source.subMeshCount;sub++)target.SetTriangles(source.GetTriangles(sub),sub);
            target.bounds=source.bounds;target.UploadMeshData(false);EditorUtility.SetDirty(target);
        }
''';assert needle in s;s=s.replace(needle,method+needle);p.write_text(s,encoding='utf-8')
p=Path(r'F:\Vr\VR quirky testing gpt astra\League of legends test\league vr imoport testing\Assets\_Game\LeagueVR\Champions\Editor\ChampionHands.cs');s=p.read_text();s=s.replace('using System.Collections.Generic;','using System.Collections.Generic;\nusing System.Collections.Concurrent;');needle='        IEnumerator Start()';insert='''        readonly ConcurrentQueue<string> renderErrors=new();
        void OnEnable(){Application.logMessageReceivedThreaded+=Collect;}
        void OnDisable(){Application.logMessageReceivedThreaded-=Collect;}
        void Collect(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||message.Contains("Invalid AABB"))renderErrors.Enqueue(message);}
        static bool Finite(Vector3 p)=>!float.IsNaN(p.x)&&!float.IsInfinity(p.x)&&!float.IsNaN(p.y)&&!float.IsInfinity(p.y)&&!float.IsNaN(p.z)&&!float.IsInfinity(p.z);
''';assert needle in s;s=s.replace(needle,insert+needle)
needle='                float error=0;var headRotation=p.head.transform.rotation;';insert='''                foreach(var renderer in roster.avatar.Instance.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.enabled))
                {
                    var mesh=renderer.sharedMesh;
                    bool skin=mesh.bindposes.Length==renderer.bones.Length&&mesh.boneWeights.Length==mesh.vertexCount&&mesh.vertices.All(Finite);
                    var baked=new Mesh();renderer.BakeMesh(baked);bool valid=baked.vertexCount==mesh.vertexCount&&baked.vertices.All(Finite);Destroy(baked);
                    results.Add(roster.Active.name+": "+(skin&&valid?"PASS":"FAIL")+" persisted skin weights, bind poses and finite rendered vertices");
                }
''';assert needle in s;s=s.replace(needle,insert+needle)
needle='            File.WriteAllText("Logs/ChampionExpansion/Hands.md",';insert='''            yield return null;
            results.Add((renderErrors.IsEmpty?"PASS":"FAIL")+" no native rendering errors or invalid bounds during all six hand views");
            File.WriteAllText("Logs/ChampionExpansion/Hands-errors.txt",string.Join("\\n",renderErrors));
''';assert needle in s;s=s.replace(needle,insert+needle);p.write_text(s,encoding='utf-8')
print('Explicitly preserve skin buffers; added focused native-render and skin-data checks.')
