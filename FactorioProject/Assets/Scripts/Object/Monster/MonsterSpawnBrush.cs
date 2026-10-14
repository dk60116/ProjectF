#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectF.Monsters
{
    // Editor tool input only; spawned actors still belong to the shared ECS world.
    public static class MonsterSpawnBrush
    {
        private static MonsterDefinition definition;
        private static int variantId = -1;
        private static int cancelledFrame = -1;
        private static int inputFrame = -1;
        private static PointerEventData pointerEvent;
        private static EventSystem pointerEventSystem;
        private static readonly List<RaycastResult> uiHits = new List<RaycastResult>();

        public static bool IsActive => definition != null;
        public static bool BlocksGameplayInput => IsActive || cancelledFrame == Time.frameCount;
        public static bool IsPainting(MonsterDefinition source, int variant) => IsActive && definition == source && variantId == variant;

        public static bool Begin(MonsterDefinition source, int variant)
        {
            if (!Application.isPlaying || source == null || !source.TryGetVariant(variant, out var data)
                || data.prefab == null || TerrainGenerator.ResolveActive() == null) return false;
            definition = source;
            variantId = variant;
            inputFrame = Time.frameCount;
            return true;
        }

        public static void Stop()
        {
            if (IsActive) cancelledFrame = Time.frameCount;
            definition = null;
            variantId = -1;
            pointerEvent = null;
            pointerEventSystem = null;
            uiHits.Clear();
        }

        public static void Tick()
        {
            if (!IsActive || inputFrame == Time.frameCount) return;
            inputFrame = Time.frameCount;
            if (Input.GetKeyDown(KeyCode.Escape) || !Application.isPlaying || MapObjectTickManager.WaitingForWorldLoad)
            {
                Stop();
                return;
            }
            if (SaveManager.GameplayInputBlocked || GameManager.TextInputFocused || MapObjectTickManager.SimulationPaused
                || !Input.GetMouseButtonDown(0)) return;
            Vector2 pointer = Input.mousePosition;
            Camera camera = Camera.main;
            TerrainGenerator terrain = TerrainGenerator.ResolveActive();
            if (camera == null || terrain == null || !camera.pixelRect.Contains(pointer) || IsPointerOverUi(pointer)) return;
            Ray ray = camera.ScreenPointToRay(pointer);
            var plane = new Plane(Vector3.up, terrain.transform.position);
            if (!plane.Raycast(ray, out float distance)) return;
            if (!MonsterSpawner.TrySpawnAt(definition, variantId, ray.GetPoint(distance), out _, out string error))
                Debug.LogWarning("Monster Data: " + error, definition);
        }

        private static bool IsPointerOverUi(Vector2 position)
        {
            EventSystem events = EventSystem.current;
            if (events == null) return false;
            if (pointerEvent == null || pointerEventSystem != events)
            {
                pointerEvent = new PointerEventData(events);
                pointerEventSystem = events;
            }
            pointerEvent.Reset();
            pointerEvent.position = position;
            uiHits.Clear();
            events.RaycastAll(pointerEvent, uiHits);
            for (int i = 0; i < uiHits.Count; i++)
                if (uiHits[i].gameObject != null && uiHits[i].gameObject.GetComponentInParent<Selectable>() != null)
                {
                    uiHits.Clear();
                    return true;
                }
            uiHits.Clear();
            return false;
        }
    }
}
#endif
