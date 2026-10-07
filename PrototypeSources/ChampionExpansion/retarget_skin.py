from pathlib import Path
P=Path(r'F:\Vr\VR quirky testing gpt astra\League of legends test\league vr imoport testing')
p=P/'Assets/_Game/LeagueVR/Champions/Editor/ChampionBuild.cs';s=p.read_text()
old='string meshPath=Base+"/Meshes/"+entry.name'
new='RetargetForearms(mesh,renderer);\n                        string meshPath=Base+"/Meshes/"+entry.name'
assert old in s;s=s.replace(old,new)
old='static string Clean(string n)=>'
new='''static void RetargetForearms(Mesh mesh,SkinnedMeshRenderer renderer)
        {
            // Bind the derived VR mesh to its sampled idle pose. Original GLBs and full-body meshes are retained.
            var baked=new Mesh();renderer.BakeMesh(baked);mesh.vertices=baked.vertices;mesh.normals=baked.normals;mesh.tangents=baked.tangents;Object.DestroyImmediate(baked);
            var bones=renderer.bones;mesh.bindposes=bones.Select(b=>b.worldToLocalMatrix*renderer.transform.localToWorldMatrix).ToArray();
            int Index(params string[] names)=>Array.FindIndex(bones,b=>names.Any(n=>string.Equals(b.name,n,StringComparison.OrdinalIgnoreCase)));
            var weights=mesh.boneWeights;var vertices=mesh.vertices;
            foreach(string side in new[]{"L","R"})
            {
                int lower=Index(side+"_Elbow",side+"_Forearm"),hand=Index(side+"_Hand");if(lower<0||hand<0)continue;
                var helpers=new HashSet<int>{lower};for(int b=0;b<bones.Length;b++){string name=bones[b].name.ToLowerInvariant();if(name.StartsWith(side.ToLowerInvariant()+"_")&&(name.Contains("arm_twist")||name.Contains("hand_twist")||name.EndsWith("_bracelet")||name.Contains("elbow_scale")))helpers.Add(b);}
                Vector3 start=renderer.transform.InverseTransformPoint(bones[lower].position),end=renderer.transform.InverseTransformPoint(bones[hand].position),axis=end-start;
                for(int v=0;v<vertices.Length;v++)
                {
                    var w=weights[v];var influence=new Dictionary<int,float>();float t=Mathf.Clamp01(Vector3.Dot(vertices[v]-start,axis)/Mathf.Max(.000001f,axis.sqrMagnitude));
                    void Add(int bone,float weight){if(weight<=0)return;if(helpers.Contains(bone)){AddDirect(lower,weight*(1-t));AddDirect(hand,weight*t);}else AddDirect(bone,weight);}
                    void AddDirect(int bone,float weight){if(weight<=0)return;influence[bone]=influence.TryGetValue(bone,out float prior)?prior+weight:weight;}
                    Add(w.boneIndex0,w.weight0);Add(w.boneIndex1,w.weight1);Add(w.boneIndex2,w.weight2);Add(w.boneIndex3,w.weight3);
                    var entries=influence.OrderByDescending(e=>e.Value).Take(4).ToArray();float sum=entries.Sum(e=>e.Value);var result=new BoneWeight();
                    for(int j=0;j<entries.Length;j++){int bone=entries[j].Key;float weight=entries[j].Value/sum;switch(j){case 0:result.boneIndex0=bone;result.weight0=weight;break;case 1:result.boneIndex1=bone;result.weight1=weight;break;case 2:result.boneIndex2=bone;result.weight2=weight;break;case 3:result.boneIndex3=bone;result.weight3=weight;break;}}
                    weights[v]=result;
                }
            }
            mesh.boneWeights=weights;mesh.RecalculateBounds();
        }
        static string Clean(string n)=>'''
assert old in s;s=s.replace(old,new);p.write_text(s)
print('Rebound derived VR skin to idle pose and retargeted forearm weights between elbow and wrist.')
