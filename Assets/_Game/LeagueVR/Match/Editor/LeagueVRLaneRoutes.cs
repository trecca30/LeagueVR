using System;using System.Collections.Generic;using System.Linq;using UnityEngine;using LeagueVR.Match;
namespace LeagueVR.Editor
{
 public static class LeagueVRLaneRoutes
 {
  const float Grid=.7f;
  public static Vector3[] Build(RiftMatch m,Vector2[] anchors)
  {
   Physics.SyncTransforms();var path=new List<Vector3>();
   for(int i=1;i<anchors.Length;i++)
   {
    var start=new Vector2Int(Mathf.RoundToInt(anchors[i-1].x/Grid),Mathf.RoundToInt(anchors[i-1].y/Grid));var end=new Vector2Int(Mathf.RoundToInt(anchors[i].x/Grid),Mathf.RoundToInt(anchors[i].y/Grid));
    var valid=new Dictionary<Vector2Int,bool>();var heights=new Dictionary<Vector2Int,Vector3>();
    Vector3 Ground(Vector2Int p){if(!heights.TryGetValue(p,out var at)){m.Ground(new Vector3(p.x*Grid,.5f,p.y*Grid),out at);heights[p]=at;}return at;}
    bool Walk(Vector2Int p)
    {
     if(valid.TryGetValue(p,out bool v))return v;
     var at=Ground(p);v=Mathf.Abs(at.y)<1.5f&&at!=Vector3.zero;
     // Reserve collision clearance for a minion; formations narrow at gateways.
     if(v)foreach(var offset in new[]{Vector3.zero,Vector3.right*.45f,Vector3.left*.45f,Vector3.forward*.45f,Vector3.back*.45f})
     {if(!m.Ground(at+offset,out var g)||Mathf.Abs(g.y-at.y)>.4f||Physics.CheckSphere(g+Vector3.up*.55f,.22f,m.player.worldMask,QueryTriggerInteraction.Ignore)){v=false;break;}}
     valid[p]=v;return v;
    }
    Vector2Int Nearest(Vector2Int p){if(Walk(p))return p;for(int r=1;r<=8;r++){var candidates=new List<Vector2Int>();for(int x=-r;x<=r;x++)for(int z=-r;z<=r;z++){var n=p+new Vector2Int(x,z);if(Walk(n))candidates.Add(n);}if(candidates.Count>0)return candidates.OrderBy(n=>(n-p).sqrMagnitude).First();}throw new Exception("No lane clearance near "+p);}
    start=Nearest(start);end=Nearest(end);var open=new List<Vector2Int>{start};var cost=new Dictionary<Vector2Int,float>{{start,0}};var parent=new Dictionary<Vector2Int,Vector2Int>();var closed=new HashSet<Vector2Int>();bool found=false;
    float Distance(Vector2 v){var a=anchors[i-1];var d=anchors[i]-a;float t=Mathf.Clamp01(Vector2.Dot(v-a,d)/d.sqrMagnitude);return Vector2.Distance(v,a+t*d);}
    for(int tries=0;tries<16000&&open.Count>0;tries++)
    {
     var current=open.OrderBy(p=>cost[p]+Vector2Int.Distance(p,end)).First();open.Remove(current);if(current==end){found=true;break;}closed.Add(current);
     for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)
     {
      if(x==0&&z==0)continue;var n=current+new Vector2Int(x,z);if(closed.Contains(n)||Distance(new Vector2(n.x*Grid,n.y*Grid))>5.5f||!Walk(n))continue;
      if(x!=0&&z!=0&&(!Walk(current+new Vector2Int(x,0))||!Walk(current+new Vector2Int(0,z))))continue;
      
      float next=cost[current]+Mathf.Sqrt(x*x+z*z)+Distance(new Vector2(n.x*Grid,n.y*Grid))*.035f;
      if(!cost.ContainsKey(n)||next<cost[n]){cost[n]=next;parent[n]=current;if(!open.Contains(n))open.Add(n);}
     }
    }
    if(!found){System.IO.File.WriteAllText("Logs/POVOverhaul/LaneClearance.txt",string.Join("\n",valid.Select(v=>v.Key+" "+Ground(v.Key)+" "+v.Value)));throw new Exception("No lane corridor path "+anchors[i-1]+" to "+anchors[i]);}
    var segment=new List<Vector3>();var node=end;segment.Add(Ground(node));while(node!=start){node=parent[node];segment.Add(Ground(node));}segment.Reverse();if(path.Count>0&&Vector3.Distance(path.Last(),segment[0])<.01f)segment.RemoveAt(0);path.AddRange(segment);
   }
   // Remove redundant collinear points without cutting corners or crossing obstacles.
   var result=new List<Vector3>();for(int i=0;i<path.Count;i++){if(i>0&&i<path.Count-1&&Vector3.Angle(path[i]-path[i-1],path[i+1]-path[i])<1&&Vector3.Distance(result.Last(),path[i])<1.8f)continue;result.Add(path[i]);}return result.ToArray();
  }
 }
}
