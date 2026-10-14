using System;
using ProjectF.Monsters;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace ProjectF.Editor.Monsters
{
    internal sealed class MonsterModelPreview : IDisposable
    {
        private PreviewRenderUtility renderer;
        private GameObject prefab;
        private GameObject instance;
        private Animator animator;
        private Renderer[] modelRenderers;
        private PlayableGraph graph;
        private AnimationClipPlayable playable;
        private AnimationClip clip;
        private MonsterAnimationState state;
        private Vector2 orbit = new Vector2(145f, 12f);
        private float zoom = 1f;
        private float time;
        private float speed = 1f;
        private bool playing;
        private double lastUpdate;
        private string error;

        public bool Playing => playing;

        public void Draw(GameObject target, MonsterAnimations animations)
        {
            EnsureInstance(target);
            EditorGUILayout.BeginHorizontal();
            MonsterAnimationState next = (MonsterAnimationState)EditorGUILayout.EnumPopup("Animation", state);
            if (next != state) { state = next; time = 0f; }
            if (GUILayout.Button(Icon(playing ? "PauseButton" : "PlayButton", playing ? "Pause" : "Play"),
                GUILayout.Width(28f)))
            {
                playing = !playing;
                if (clip != null && time >= clip.length) time = 0f;
                lastUpdate = EditorApplication.timeSinceStartup;
            }
            if (GUILayout.Button(Icon("Refresh", "Reset view"), GUILayout.Width(28f)))
            {
                orbit = new Vector2(145f, 12f);
                zoom = 1f;
            }
            EditorGUILayout.EndHorizontal();
            SetClip(animations?.GetClip(state));
            using (new EditorGUI.DisabledScope(clip == null))
            {
                float nextTime = EditorGUILayout.Slider("Time", time, 0f, clip != null ? clip.length : 1f);
                if (!Mathf.Approximately(time, nextTime)) { time = nextTime; Sample(); }
                speed = EditorGUILayout.Slider("Speed", speed, 0.1f, 2f);
            }
            Rect rect = GUILayoutUtility.GetRect(10f, 300f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));
            HandleInput(rect);
            if (instance == null || renderer == null)
            {
                EditorGUI.DropShadowLabel(rect, error ?? "No prefab");
                return;
            }
            Bounds bounds = GetBounds();
            float radius = Mathf.Max(0.01f, bounds.extents.magnitude);
            float limitingFov = Mathf.Min(renderer.cameraFieldOfView,
                2f * Mathf.Atan(Mathf.Tan(renderer.cameraFieldOfView * Mathf.Deg2Rad * 0.5f)
                    * rect.width / rect.height) * Mathf.Rad2Deg);
            float distance = radius / Mathf.Sin(limitingFov * Mathf.Deg2Rad * 0.5f) * 1.1f * zoom;
            Camera camera = renderer.camera;
            camera.transform.position = bounds.center
                + Quaternion.Euler(orbit.y, orbit.x, 0f) * (Vector3.back * distance);
            camera.transform.LookAt(bounds.center);
            camera.nearClipPlane = Mathf.Max(0.001f, distance - radius * 2f);
            camera.farClipPlane = Mathf.Max(100f, distance + radius * 5f);
            if (Event.current.type == EventType.Repaint)
            {
                renderer.BeginPreview(rect, GUIStyle.none);
                renderer.Render(true, true);
                GUI.DrawTexture(rect, renderer.EndPreview(), ScaleMode.StretchToFill, false);
                if (clip == null) GUI.Label(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, 20f),
                    "No " + state + " clip", EditorStyles.miniLabel);
            }
        }

        public bool Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float delta = (float)Math.Min(0.1, Math.Max(0.0, now - lastUpdate));
            lastUpdate = now;
            if (!playing || clip == null || instance == null) return false;
            time += delta * speed;
            if (clip.length > 0f && (state == MonsterAnimationState.Idle || state == MonsterAnimationState.Move))
                time %= clip.length;
            else if (time >= clip.length) { time = clip.length; playing = false; }
            Sample();
            return true;
        }

        private void EnsureInstance(GameObject target)
        {
            if (prefab == target && (instance != null || target == null)) return;
            Dispose();
            prefab = target;
            error = null;
            if (target == null) return;
            try
            {
                renderer = new PreviewRenderUtility(true);
                renderer.cameraFieldOfView = 30f;
                renderer.ambientColor = new Color(0.35f, 0.35f, 0.35f);
                renderer.lights[0].intensity = 1.15f;
                renderer.lights[0].transform.rotation = Quaternion.Euler(35f, 35f, 0f);
                renderer.lights[1].intensity = 0.65f;
                renderer.lights[1].transform.rotation = Quaternion.Euler(340f, 215f, 0f);
                instance = renderer.InstantiatePrefabInScene(target);
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;
                foreach (MonoBehaviour behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                    behaviour.enabled = false;
                foreach (AudioSource source in instance.GetComponentsInChildren<AudioSource>(true))
                    source.enabled = false;
                animator = instance.GetComponentInChildren<Animator>(true);
                if (animator != null)
                {
                    animator.runtimeAnimatorController = null;
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.enabled = true;
                }
                modelRenderers = instance.GetComponentsInChildren<Renderer>(true);
            }
            catch (Exception exception)
            {
                Dispose();
                error = exception.Message;
                Debug.LogException(exception);
            }
        }

        private void SetClip(AnimationClip next)
        {
            if (clip == next) return;
            if (graph.IsValid()) graph.Destroy();
            if (animator != null) animator.Rebind();
            clip = next;
            time = 0f;
            if (clip == null || instance == null) return;
            if (animator != null && !clip.legacy)
            {
                graph = PlayableGraph.Create("Monster Data Preview");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false);
                playable.SetApplyPlayableIK(false);
                AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Model", animator);
                output.SetSourcePlayable(playable);
                graph.Play();
            }
            Sample();
        }

        private void Sample()
        {
            if (clip == null || instance == null) return;
            if (graph.IsValid())
            {
                playable.SetTime(time);
                graph.Evaluate(0f);
            }
            else clip.SampleAnimation(animator != null ? animator.gameObject : instance, time);
        }

        private Bounds GetBounds()
        {
            Bounds result = new Bounds(instance.transform.position, Vector3.one * 0.02f);
            bool found = false;
            foreach (Renderer model in modelRenderers)
            {
                if (!model.enabled || !model.gameObject.activeInHierarchy) continue;
                if (!found) { result = model.bounds; found = true; }
                else result.Encapsulate(model.bounds);
            }
            return result;
        }

        private void HandleInput(Rect rect)
        {
            Event current = Event.current;
            if (!rect.Contains(current.mousePosition)) return;
            if (current.type == EventType.ScrollWheel)
            {
                zoom = Mathf.Clamp(zoom + current.delta.y * 0.05f, 0.5f, 4f);
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && (current.button == 0 || current.button == 1))
            {
                orbit.x += current.delta.x;
                orbit.y = Mathf.Clamp(orbit.y - current.delta.y, -89f, 89f);
                current.Use();
            }
        }

        internal static GUIContent Icon(string name, string tooltip)
        {
            GUIContent content = new GUIContent(EditorGUIUtility.IconContent(name));
            content.tooltip = tooltip;
            return content;
        }

        public void Dispose()
        {
            if (graph.IsValid()) graph.Destroy();
            if (renderer != null) renderer.Cleanup();
            renderer = null;
            instance = null;
            animator = null;
            prefab = null;
            clip = null;
            modelRenderers = null;
            time = 0f;
            playing = false;
            lastUpdate = EditorApplication.timeSinceStartup;
        }
    }
}
