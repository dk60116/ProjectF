using System;

namespace ProjectF.Simulation
{
    public readonly struct SceneLoadRequest
    {
        public SceneLoadRequest(int buildIndex, string sceneName, ulong revision)
        {
            BuildIndex = buildIndex;
            SceneName = sceneName;
            Revision = revision;
        }

        public int BuildIndex { get; }
        public string SceneName { get; }
        public ulong Revision { get; }
        public bool UsesBuildIndex => BuildIndex >= 0;
        public bool IsValid => UsesBuildIndex || !string.IsNullOrWhiteSpace(SceneName);
    }

    /// <summary>
    /// Single-consumer mailbox for scene changes. A scene operation already handed to Unity
    /// cannot be cancelled, so requests received while it runs collapse to the newest target.
    /// </summary>
    public sealed class LatestSceneLoadRequest
    {
        private SceneLoadRequest pending;
        private ulong nextRevision = 1;
        private bool hasPending;

        public bool HasPending => hasPending;

        public ulong Enqueue(int buildIndex)
        {
            if (buildIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(buildIndex));
            }

            return EnqueueCore(buildIndex, null);
        }

        public ulong Enqueue(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException("A scene name is required.", nameof(sceneName));
            }

            return EnqueueCore(-1, sceneName);
        }

        public bool TryTake(out SceneLoadRequest request)
        {
            if (!hasPending)
            {
                request = default;
                return false;
            }

            request = pending;
            pending = default;
            hasPending = false;
            return true;
        }

        public void Clear()
        {
            pending = default;
            hasPending = false;
        }

        private ulong EnqueueCore(int buildIndex, string sceneName)
        {
            ulong revision = AllocateRevision();
            pending = new SceneLoadRequest(buildIndex, sceneName, revision);
            hasPending = true;
            return revision;
        }

        private ulong AllocateRevision()
        {
            ulong revision = nextRevision++;
            if (revision != 0)
            {
                return revision;
            }

            revision = nextRevision++;
            return revision != 0 ? revision : 1;
        }
    }
}
