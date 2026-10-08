using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.Railway
{
    public sealed partial class TrainWorld
    {
        private readonly Dictionary<TrainInstance, Train> views = new Dictionary<TrainInstance, Train>();

        internal void AttachView(TrainInstance instance, Train view)
        {
            if (!Contains(instance) || view == null) return;
            views[instance] = view;
            SetActive(instance, true);
        }

        internal void DetachView(TrainInstance instance)
        {
            // Rendering detachment alone must not discard simulation state or links.
            views.Remove(instance);
        }

        internal bool TryGetView(TrainInstance instance, out Train view)
        {
            view = null;
            return Contains(instance) && views.TryGetValue(instance, out view) && view != null;
        }

        internal void CollectActiveViews(ICollection<Train> results)
        {
            if (results == null) return;
            foreach (var pair in views)
                if (pair.Key.IsActive && pair.Key.IsPlaced && pair.Value != null)
                    results.Add(pair.Value);
        }

        partial void RemoveView(TrainInstance instance) => views.Remove(instance);
        partial void ClearViews() => views.Clear();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => Shared.Clear();
    }

    // The legacy APIs expose native cars, but the connection graph lives only in data.
    // A concrete struct enumerable avoids boxing for the hot foreach call sites.
    public readonly struct NativeTrainConnections : IReadOnlyCollection<Train>
    {
        private readonly TrainInstance instance;
        internal NativeTrainConnections(TrainInstance instance) { this.instance = instance; }

        public int Count
        {
            get
            {
                int count = 0;
                var enumerator = GetEnumerator();
                while (enumerator.MoveNext()) count++;
                return count;
            }
        }

        public Enumerator GetEnumerator() => new Enumerator(instance);
        IEnumerator<Train> IEnumerable<Train>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<Train>
        {
            private readonly TrainInstance instance;
            private int index;
            public Train Current { get; private set; }
            object IEnumerator.Current => Current;

            internal Enumerator(TrainInstance instance)
            { this.instance = instance; index = -1; Current = null; }

            public bool MoveNext()
            {
                while (instance != null && instance.IsAlive && ++index < instance.ConnectionCount)
                    if (instance.World.TryGetView(instance.GetConnection(index).Target, out Train view))
                    { Current = view; return true; }
                Current = null;
                return false;
            }

            public void Reset() { index = -1; Current = null; }
            public void Dispose() { }
        }
    }
}
