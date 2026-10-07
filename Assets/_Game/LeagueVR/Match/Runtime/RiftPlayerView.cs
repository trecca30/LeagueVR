using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using LeagueVR.Champions;
namespace LeagueVR.Match
{
    [DefaultExecutionOrder(300)]
    public class RiftPlayerView : MonoBehaviour
    {
        public PlayerChampion champion;
        readonly List<Renderer> templateMeshes = new();
        readonly List<LineRenderer> templateRays = new();
        public int HiddenTemplateMeshCount => templateMeshes.Count;

        void Awake()
        {
            if (!champion)
                champion = GetComponent<PlayerChampion>();
            champion.head.nearClipPlane = .025f;
            foreach (var pose in champion.origin.GetComponentsInChildren<TrackedPoseDriver>(true))
                pose.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            RefreshTemplateVisuals();
            HideTemplateVisuals();
        }

        public void RefreshTemplateVisuals()
        {
            templateMeshes.Clear();
            templateRays.Clear();
            foreach (var t in champion.origin.GetComponentsInChildren<Transform>(true))
            {
                if (t != champion.leftHand && t != champion.rightHand && t.name != "Left Hand" && t.name != "Right Hand")
                    continue;
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is LineRenderer lr)
                    {
                        if (!lr.name.Contains("Teleport"))
                            templateRays.Add(lr);
                    }
                    else
                        templateMeshes.Add(r);
                }
            }
        }

        void OnEnable()
        {
            Application.onBeforeRender += BeforeRender;
        }

        void OnDisable()
        {
            Application.onBeforeRender -= BeforeRender;
            foreach (var r in templateMeshes)
                if (r)
                    r.forceRenderingOff = false;
            foreach (var r in templateRays)
                if (r)
                    r.forceRenderingOff = false;
        }

        [BeforeRenderOrder(200)]
        void BeforeRender()
        {
            HideTemplateVisuals();
        }

        void LateUpdate()
        {
            HideTemplateVisuals();
        }

        public void HideTemplateVisuals()
        {
            foreach (var r in templateMeshes)
                if (r)
                    r.forceRenderingOff = true;
            foreach (var r in templateRays)
                if (r)
                    r.forceRenderingOff = true;
        }
    }
}
