using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace LeagueVR.Champions
{
    /// <summary>
    /// The stream view: what the desktop window shows while someone plays in the headset, for sharing on Discord or
    /// OBS. The raw headset mirror shakes with every head movement and is cropped to fill the window, so this camera
    /// renders its own view instead: SMOOTH follows the player's eyes with the jitter and roll filtered out and a wider
    /// field of view; SHOULDER films the whole champion from behind; EXACT shows the headset's own left-eye view 1:1,
    /// uncropped. The headset view is never affected. F8 cycles the views.
    /// </summary>
    public class SpectatorCamera : MonoBehaviour
    {
        public PlayerChampion player;

        // SMOOTH's vertical field of view: 72 degrees is about 104 across a 16:9 window, close to the headset's.
        const float SmoothFov = 72, ShoulderFov = 58;
        const int Exact = 3;
        const float FollowTime = .05f, TurnTime = .18f, ShoulderTime = .14f, ShoulderTurnTime = .4f;

        Camera view, letterbox;
        int mode = -1;
        bool mirrorOff, exactApplied;
        Vector3 position, shoulderForward, lastPivot;
        Quaternion rotation;
        bool placed;
        readonly List<Renderer> faceRenderers = new();
        readonly List<Renderer> hiddenFaceRenderers = new();
        float faceScanAt;
        static readonly List<XRDisplaySubsystem> displays = new();

        public Camera View => view;
        public bool Active => view && view.enabled;

        void OnEnable() => RenderPipelineManager.beginCameraRendering += BeginCamera;

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            SetMirror(true);
            if (view)
                view.enabled = false;
            if (letterbox)
                letterbox.enabled = false;
        }

        void OnDestroy()
        {
            if (view)
                Destroy(view.gameObject);
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f8Key.wasPressedThisFrame)
            {
                ComfortSettings.StreamView++;
                RiftNotify("Stream view: " + ComfortSettings.StreamViewNames[ComfortSettings.StreamView]);
            }
        }

        void RiftNotify(string text)
        {
            var match = Match.RiftMatch.Instance;
            if (match)
                match.Notify(text);
        }

        void LateUpdate()
        {
            if (!player || !player.head)
                return;
            // Without a headset the desktop window already is the player's view.
            int wanted = player.DesktopMode ? 0 : ComfortSettings.StreamView;
            if (wanted != mode)
            {
                mode = wanted;
                placed = false;
                SetMirror(mode == 0);
            }
            if (mode == 0)
            {
                if (view)
                    view.enabled = false;
                if (letterbox)
                    letterbox.enabled = false;
                return;
            }
            EnsureCamera();
            view.enabled = true;
            if (mode == Exact)
                FollowExact();
            else
            {
                ResetExact();
                if (mode == 1)
                    FollowEyes();
                else
                    FollowShoulder();
            }
            view.transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>
        /// EXACT: exactly what the player sees in the headset. The headset's left eye, its pose and its lens frustum
        /// straight from the XR display each frame, shown whole at the eye image's own shape (never cropped or stretched)
        /// with black bars at the sides. The comfort vignette and fades show as they do in the headset.
        /// </summary>
        void FollowExact()
        {
            position = player.head.transform.position;
            rotation = player.head.transform.rotation;
            placed = false;
            ApplyEyeMatrices();
        }

        void ApplyEyeMatrices()
        {
            SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays)
            {
                if (!display.running || display.GetRenderPassCount() == 0)
                    continue;
                display.GetRenderPass(0, out var pass);
                if (pass.GetRenderParameterCount() == 0)
                    continue;
                pass.GetRenderParameter(player.head, 0, out var eye);
                view.worldToCameraMatrix = eye.view;
                view.projectionMatrix = eye.projection;
                exactApplied = true;
                Letterbox(eye.projection);
                return;
            }
            // No headset running: the head camera's own view.
            ResetExact();
            view.fieldOfView = player.head.fieldOfView;
        }

        /// <summary>The eye's whole image, centred at its own aspect; a black camera clears the bars around it.</summary>
        void Letterbox(Matrix4x4 projection)
        {
            // The frustum's side tangents, read off the (OpenGL style) projection: lenses are asymmetric.
            float left = (projection.m02 - 1) / projection.m00, right = (projection.m02 + 1) / projection.m00;
            float bottom = (projection.m12 - 1) / projection.m11, top = (projection.m12 + 1) / projection.m11;
            float eyeAspect = Mathf.Abs((right - left) / Mathf.Max(1e-4f, top - bottom));
            float screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            var rect = eyeAspect < screenAspect
                ? new Rect((1 - eyeAspect / screenAspect) / 2, 0, eyeAspect / screenAspect, 1)
                : new Rect(0, (1 - screenAspect / eyeAspect) / 2, 1, screenAspect / eyeAspect);
            view.rect = rect;
            EnsureLetterbox();
            letterbox.enabled = rect.width < .999f || rect.height < .999f;
        }

        void ResetExact()
        {
            if (letterbox)
                letterbox.enabled = false;
            if (!exactApplied)
                return;
            exactApplied = false;
            view.ResetWorldToCameraMatrix();
            view.ResetProjectionMatrix();
            view.ResetAspect();
            view.rect = new Rect(0, 0, 1, 1);
        }

        void EnsureLetterbox()
        {
            if (letterbox)
                return;
            var go = new GameObject("Spectator letterbox");
            go.transform.SetParent(view.transform, false);
            letterbox = go.AddComponent<Camera>();
            letterbox.stereoTargetEye = StereoTargetEyeMask.None;
            letterbox.depth = view.depth - 1;
            letterbox.cullingMask = 0;
            letterbox.clearFlags = CameraClearFlags.SolidColor;
            letterbox.backgroundColor = Color.black;
            var data = letterbox.GetUniversalAdditionalCameraData();
            data.allowXRRendering = false;
            data.renderPostProcessing = false;
            data.renderShadows = false;
        }

        /// <summary>First person, steadied: position follows the head closely, rotation lags a little and never rolls.</summary>
        void FollowEyes()
        {
            var head = player.head.transform;
            var forward = head.forward;
            float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(forward.y, -1, 1)) * Mathf.Rad2Deg, -70, 70);
            var flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f)
                flat = Vector3.ProjectOnPlane(head.up * -Mathf.Sign(forward.y), Vector3.up);
            var target = Quaternion.LookRotation(flat.normalized) * Quaternion.Euler(pitch, 0, 0);
            // Teleports, dashes and snap turns cut instead of whipping the camera across the map.
            if (!placed || (head.position - position).sqrMagnitude > 1 || Quaternion.Angle(rotation, target) > 70)
            {
                position = head.position;
                rotation = target;
                placed = true;
            }
            float dt = Time.unscaledDeltaTime;
            position = Vector3.Lerp(position, head.position, 1 - Mathf.Exp(-dt / FollowTime));
            rotation = Quaternion.Slerp(rotation, target, 1 - Mathf.Exp(-dt / TurnTime));
            view.fieldOfView = SmoothFov;
            view.nearClipPlane = Mathf.Min(.05f, player.head.nearClipPlane);
        }

        /// <summary>Third person from behind the right shoulder, turning with the player's view and kept out of walls.</summary>
        void FollowShoulder()
        {
            var head = player.head.transform;
            var flat = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f)
                flat = shoulderForward.sqrMagnitude > 0 ? shoulderForward : Vector3.forward;
            flat.Normalize();
            float dt = Time.unscaledDeltaTime;
            float scale = player.Body && player.Body.Rig != null ? Mathf.Clamp(player.Body.Rig.Scale, .7f, 1.5f) : 1;
            var pivot = head.position - Vector3.up * .12f;
            bool jumped = !placed || (pivot - lastPivot).sqrMagnitude > 4;
            lastPivot = pivot;
            shoulderForward = jumped ? flat : Vector3.Slerp(shoulderForward, flat, 1 - Mathf.Exp(-dt / ShoulderTurnTime)).normalized;
            var right = Vector3.Cross(Vector3.up, shoulderForward);
            var desired = pivot - shoulderForward * 2.6f * scale + Vector3.up * .5f * scale + right * .45f * scale;
            desired = KeepOutOfWalls(pivot, desired);
            position = jumped ? desired : Vector3.Lerp(position, desired, 1 - Mathf.Exp(-dt / ShoulderTime));
            var look = pivot + shoulderForward * 3f - Vector3.up * .35f * scale;
            rotation = Quaternion.LookRotation(look - position);
            placed = true;
            view.fieldOfView = ShoulderFov;
            view.nearClipPlane = .1f;
        }

        Vector3 KeepOutOfWalls(Vector3 pivot, Vector3 desired)
        {
            var offset = desired - pivot;
            float distance = offset.magnitude;
            if (distance < .01f)
                return desired;
            var hits = Physics.SphereCastAll(pivot, .2f, offset / distance, distance, Physics.DefaultRaycastLayers & ~(1 << 5), QueryTriggerInteraction.Ignore);
            float nearest = distance;
            foreach (var hit in hits)
            {
                // Units walking past (minions, champions, structures' hitboxes) never shove the camera; only the map does.
                if (player.OwnCollider(hit.collider) || hit.collider.GetComponentInParent<Combatant>())
                    continue;
                nearest = Mathf.Min(nearest, hit.distance);
            }
            return pivot + offset / distance * Mathf.Max(.4f, nearest);
        }

        void EnsureCamera()
        {
            if (view)
                return;
            var head = player.head;
            var go = new GameObject("Spectator Camera");
            view = go.AddComponent<Camera>();
            // Renders only to the desktop window; the headset keeps its own camera.
            view.stereoTargetEye = StereoTargetEyeMask.None;
            view.depth = head.depth + 10;
            view.cullingMask = head.cullingMask;
            view.clearFlags = head.clearFlags;
            view.backgroundColor = head.backgroundColor;
            view.farClipPlane = head.farClipPlane;
            view.allowHDR = head.allowHDR;
            view.allowMSAA = head.allowMSAA;
            var source = head.GetUniversalAdditionalCameraData();
            var data = view.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = source.renderPostProcessing;
            data.antialiasing = source.antialiasing;
            data.renderShadows = true;
            // A plain desktop camera: URP would otherwise treat it as an XR camera and give it the headset's projection.
            data.allowXRRendering = false;
        }

        /// <summary>The headset's mirror into the desktop window is switched off while this camera draws there.</summary>
        void SetMirror(bool on)
        {
            if (mirrorOff == !on)
                return;
            mirrorOff = !on;
            SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays)
                if (display.running)
                    display.SetPreferredMirrorBlitMode(on ? XRMirrorViewBlitMode.Default : XRMirrorViewBlitMode.None);
        }

        // ---------- Per-camera visibility ----------

        void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != view || !player)
                return;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
            // EXACT shows what the headset shows, face effects included, with this frame's final eye pose.
            if (mode == Exact)
            {
                ApplyEyeMatrices();
                return;
            }
            // Things fixed to the player's face (comfort vignette, screen fades) are for the player's eyes only.
            if (Time.unscaledTime >= faceScanAt)
            {
                faceScanAt = Time.unscaledTime + 1;
                faceRenderers.Clear();
                player.head.GetComponentsInChildren(true, faceRenderers);
            }
            hiddenFaceRenderers.Clear();
            foreach (var r in faceRenderers)
                if (r && !r.forceRenderingOff)
                {
                    r.forceRenderingOff = true;
                    hiddenFaceRenderers.Add(r);
                }
            if (mode == 2 && player.Body)
                player.Body.ShowFullBody(true);
        }

        void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != view)
                return;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            foreach (var r in hiddenFaceRenderers)
                if (r)
                    r.forceRenderingOff = false;
            hiddenFaceRenderers.Clear();
            if (player && player.Body)
                player.Body.ShowFullBody(false);
        }
    }
}
