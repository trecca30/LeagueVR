using System;using UnityEngine;
namespace LeagueVR{
 [CreateAssetMenu(menuName="League VR/Original Sound Bank")]public class LeagueSoundBank:ScriptableObject{
  [Serializable]public class Layer{public AudioClip[] variants;}
  [Serializable]public class Event{public string key,eventName,sourceBank;public Layer[] layers;}
  [Range(0,1)]public float outputGain=.6f;public Event[] events;public static int PlayedLayers;public static string LastEvent;
  public Event Find(string key){if(events==null)return null;foreach(var e in events)if(e.key==key)return e;return null;}
  public float Play(AudioSource source,string key,float volume=1){var e=Find(key);if(e==null||!source)return 0;float duration=0;foreach(var layer in e.layers){if(layer.variants==null||layer.variants.Length==0)continue;var clip=layer.variants[UnityEngine.Random.Range(0,layer.variants.Length)];if(!clip)continue;source.PlayOneShot(clip,volume*outputGain);duration=Mathf.Max(duration,clip.length);PlayedLayers++;}LastEvent=e.eventName;return duration;}
  public void At(string key,Vector3 position,float volume=.5f){if(Find(key)==null)return;var go=new GameObject("Original League impact audio");go.transform.position=position;var source=go.AddComponent<AudioSource>();Configure(source);float duration=Play(source,key,volume);Destroy(go,duration+.1f);}
  public static void Configure(AudioSource s){s.playOnAwake=false;s.spatialBlend=1;s.dopplerLevel=0;s.rolloffMode=AudioRolloffMode.Logarithmic;s.minDistance=2;s.maxDistance=35;s.priority=160;}
 }
}
