using UnityEngine;

namespace ProjectF.Monsters
{
    [DefaultExecutionOrder(900), DisallowMultipleComponent]
    public sealed class MonsterWorldView : MonoBehaviour
    {
        private MonsterWorld world;
        private Camera presentationCamera;

        internal static MonsterWorldView Create(MonsterWorld owner, Transform parent)
        {
            var root = new GameObject("MonsterWorldView");
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<MonsterWorldView>();
            view.world = owner;
            return view;
        }

        private Camera GetCamera()
        {
            if (presentationCamera == null || !presentationCamera.isActiveAndEnabled) presentationCamera = Camera.main;
            return presentationCamera;
        }

        internal void NotifyAttackAnimationStarted(MonsterInstance monster)
            => monster.NotifyAttackAnimationStarted(transform, GetCamera());

        private void LateUpdate()
        {
            using var sample = MapObjectTickProfiler.SampleLateUpdateCaller<MonsterWorldView>();
            world?.RefreshHealthBars(transform, GetCamera());
        }

        internal void Release()
        {
            world = null;
            gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        private void OnDestroy() => world?.Dispose();
    }
}
