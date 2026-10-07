using System;
using System.IO;
using UnityEditor;
namespace LeagueVR.Editor
{
    // Development-only command runner. Accepts a fixed set of verification/build actions.
    [InitializeOnLoad]public static class LeagueVRMatchWork
    {
        static double next;static LeagueVRMatchWork(){EditorApplication.update+=Tick;}
        static void Tick()
        {
            if(EditorApplication.timeSinceStartup<next || EditorApplication.isCompiling || EditorApplication.isUpdating)return;next=EditorApplication.timeSinceStartup+1;
            const string path="Temp/LeagueVRMatch.request";if(!File.Exists(path))return;string command=File.ReadAllText(path).Trim();File.Delete(path);Directory.CreateDirectory("Logs/LeagueMatch");
            try{if(command=="Build")LeagueVRMatchBuilder.Build();else if(command=="Test")LeagueVRMatchTests.Run();else if(command=="Capture")LeagueVRMatchTests.Capture();else if(command=="Play")EditorApplication.isPlaying=true;else if(command=="Stop")EditorApplication.isPlaying=false;else throw new InvalidOperationException("Unknown verification action");}
            catch(Exception e){File.WriteAllText("Logs/LeagueMatch/work-error.txt",e.ToString());UnityEngine.Debug.LogException(e);}
        }
    }
}
