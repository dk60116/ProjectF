using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.Conveyors
{
    // Slots increase in the direction of travel. A run owns its items; there is
    // no second per-slot item array while a line is attached to this store.
    internal sealed class ConveyorRunStore
    {
        internal struct Item : IEquatable<Item>
        {
            internal int Id;
            internal int MoveFrame;
            // The start position is an offset from the destination slot here.
            // The Block adapter reconstructs world coordinates only on demand.
            internal ConveyorDataMotionState Motion;
            internal ConveyorPickupGateState Gate;
            internal float HoldUntil;

            internal static Item Empty => new Item { Id = -1, MoveFrame = -1 };

            public bool Equals(Item other)
            {
                return Id == other.Id && MoveFrame == other.MoveFrame
                    && HoldUntil == other.HoldUntil
                    && Gate.hasGate == other.Gate.hasGate
                    && Gate.requiresExit == other.Gate.requiresExit
                    && Gate.hasExited == other.Gate.hasExited
                    && Gate.exitRadius == other.Gate.exitRadius
                    && Gate.isSettled == other.Gate.isSettled
                    && Gate.dropOrigin.Equals(other.Gate.dropOrigin)
                    && Gate.hasOrigin == other.Gate.hasOrigin
                    && Gate.autoPickupBlocked == other.Gate.autoPickupBlocked
                    && Motion.active == other.Motion.active
                    && (!Motion.active || (Motion.startTime == other.Motion.startTime
                        && Motion.duration == other.Motion.duration
                        && Motion.pathLength == other.Motion.pathLength
                        && Motion.durationPathLength == other.Motion.durationPathLength
                        && Motion.progress == other.Motion.progress
                        && Motion.useGpuLinearRendering == other.Motion.useGpuLinearRendering
                        && Motion.startWorldPosition.Equals(other.Motion.startWorldPosition)));
            }
        }

        internal struct Run
        {
            internal int First;
            internal int Count;
            internal Item Value;
            internal int Last => First + Count - 1;
        }

        private readonly List<Run> runs = new List<Run>();
        internal int SlotCount { get; }
        internal int RunCount => runs.Count;
        internal int ItemCount { get; private set; }
        internal int Version { get; private set; }
        internal int LastVisitedRuns { get; private set; }
        internal Run GetRun(int index) => runs[index];

        internal void Clear()
        {
            runs.Clear();
            ItemCount = 0;
            unchecked { Version++; }
        }

        internal ConveyorRunStore(int slotCount)
        {
            if (slotCount <= 0) throw new ArgumentOutOfRangeException(nameof(slotCount));
            SlotCount = slotCount;
        }

        private int Find(int slot)
        {
            int low = 0, high = runs.Count - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                Run run = runs[middle];
                if (slot < run.First) high = middle - 1;
                else if (slot > run.Last) low = middle + 1;
                else return middle;
            }
            return ~low;
        }

        internal Item Read(int slot)
        {
            if (slot < 0 || slot >= SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
            int index = Find(slot);
            return index >= 0 ? runs[index].Value : Item.Empty;
        }

        internal bool Write(int slot, Item value)
        {
            if (slot < 0 || slot >= SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
            int index = Find(slot);
            if (index >= 0)
            {
                Run previous = runs[index];
                if (previous.Value.Equals(value)) return false;
                runs.RemoveAt(index);
                ItemCount--;
                if (slot > previous.First)
                {
                    runs.Insert(index++, new Run { First = previous.First,
                        Count = slot - previous.First, Value = previous.Value });
                }
                if (slot < previous.Last)
                {
                    runs.Insert(index, new Run { First = slot + 1,
                        Count = previous.Last - slot, Value = previous.Value });
                }
            }
            else
            {
                if (value.Id < 0) return false;
                index = ~index;
            }

            if (value.Id >= 0)
            {
                runs.Insert(index, new Run { First = slot, Count = 1, Value = value });
                ItemCount++;
                MergeAt(index);
            }
            unchecked { Version++; }
            return true;
        }

        private void MergeAt(int index)
        {
            if (index > 0 && CanMerge(runs[index - 1], runs[index]))
            {
                Run left = runs[index - 1];
                left.Count += runs[index].Count;
                runs[index - 1] = left;
                runs.RemoveAt(index--);
            }
            if (index + 1 < runs.Count && CanMerge(runs[index], runs[index + 1]))
            {
                Run left = runs[index];
                left.Count += runs[index + 1].Count;
                runs[index] = left;
                runs.RemoveAt(index + 1);
            }
        }

        private static bool CanMerge(Run left, Run right) =>
            left.Last + 1 == right.First && left.Value.Equals(right.Value);

        // One scheduled slot step, matching the existing destination-reservation
        // semantics. N packed items change one run, not N Block lane records.
        // vacatedSlots contains only real vacancies (not occupied interior slots).
        internal bool Advance(float now, int frame, Vector3 slotStep, float duration,
            List<int> vacatedSlots, float notBefore = float.NegativeInfinity)
        {
            LastVisitedRuns = 0;
            bool changed = false;
            int nextVacatedSlot = -1;
            float nextVacancyTime = now;
            for (int i = runs.Count - 1; i >= 0; i--)
            {
                LastVisitedRuns++;
                Run run = runs[i];
                Item item = run.Value;
                if (item.Motion.active && now < item.Motion.startTime + item.Motion.duration)
                {
                    nextVacatedSlot = -1;
                    continue;
                }
                if (item.HoldUntil > now || item.MoveFrame == frame)
                {
                    nextVacatedSlot = -1;
                    continue;
                }

                bool settled = item.Motion.active;
                float moveTime = settled ? Mathf.Max(notBefore, item.Motion.startTime + item.Motion.duration) : now;
                moveTime = Mathf.Max(moveTime, item.HoldUntil);
                if (nextVacatedSlot == run.Last + 1) moveTime = Mathf.Max(moveTime, nextVacancyTime);
                nextVacatedSlot = -1;
                item.Motion.active = false;
                item.MoveFrame = -1;
                item.HoldUntil = 0f;
                int nextOccupied = i + 1 < runs.Count ? runs[i + 1].First : SlotCount;
                if (run.Last + 1 < nextOccupied && duration > 0f)
                {
                    vacatedSlots.Add(run.First);
                    nextVacatedSlot = run.First;
                    nextVacancyTime = moveTime;
                    run.First++;
                    item.MoveFrame = frame;
                    item.Gate.MarkSettled();
                    item.Motion = new ConveyorDataMotionState
                    {
                        active = true,
                        startWorldPosition = -slotStep,
                        pathLength = slotStep.magnitude,
                        startTime = moveTime,
                        duration = duration
                    };
                    changed = true;
                }
                else if (settled) changed = true;
                run.Value = item;
                runs[i] = run;
            }
            // Timing differences disappear when items settle against a jam.
            for (int i = runs.Count - 2; i >= 0; i--)
            {
                if (!CanMerge(runs[i], runs[i + 1])) continue;
                Run run = runs[i];
                run.Count += runs[i + 1].Count;
                runs[i] = run;
                runs.RemoveAt(i + 1);
            }
            if (changed) unchecked { Version++; }
            return changed;
        }
    }

    internal enum ConveyorLaneField { Item, MoveFrame, Motion, PickupGate, Hold }

    internal interface IConveyorLaneMap<T>
    {
        bool Read(int lane, ConveyorLaneField field, out T value);
        bool Write(int lane, ConveyorLaneField field, T value);
        void Detach();
    }

    // Existing Block APIs stay indexed by lane. Resizing switches back to local
    // storage before layout changes; normal reads/writes address the run owner.
    internal sealed class ConveyorLaneBuffer<T>
    {
        private readonly List<T> local = new List<T>();
        private readonly ConveyorLaneField field;
        internal IConveyorLaneMap<T> Map;
        internal int Count => local.Count;
        internal ConveyorLaneBuffer(ConveyorLaneField field) { this.field = field; }
        internal T this[int lane]
        {
            get => Map != null && Map.Read(lane, field, out T value) ? value : local[lane];
            set { if (Map == null || !Map.Write(lane, field, value)) local[lane] = value; }
        }
        internal void Add(T value) { Map?.Detach(); local.Add(value); }
        internal void RemoveAt(int index) { Map?.Detach(); local.RemoveAt(index); }
        internal void Clear() { Map?.Detach(); local.Clear(); }
    }
}
