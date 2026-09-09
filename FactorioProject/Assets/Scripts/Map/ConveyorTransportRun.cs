using UnityEngine;

namespace ProjectF.Conveyors
{
    internal struct ConveyorTransportItem
    {
        internal int Id;
        internal ConveyorPickupGateState Gate;
    }

    internal sealed class ConveyorTransportRun
    {
        internal readonly Block[] Blocks;
        internal readonly int[] Front, Back;
        internal readonly ConveyorTransportStore<ConveyorTransportItem> Items;
        internal readonly Vector3 Origin, Step;
        internal readonly float Speed, Spacing;
        internal readonly Block Inlet, Outlet;
        internal readonly int InletLane, OutletLane;
        internal bool Active { get; private set; }
        internal int Revision { get; private set; }
        private double lastTime;
        private int boundaryFrame = -1;
        private double nextInputTime, nextOutputTime;
        internal long BoundaryChecks { get; private set; }
        internal long InputAttempts { get; private set; }
        internal long OutputAttempts { get; private set; }
        internal double NextBoundaryTime => System.Math.Min(nextInputTime, nextOutputTime);

        internal ConveyorTransportRun(Block[] blocks, int[] front, int[] back, Block inlet, int inletLane,
            Block outlet, int outletLane, float speed, float spacing)
        {
            Blocks = blocks; Front = front; Back = back;
            Inlet = inlet; InletLane = inletLane; Outlet = outlet; OutletLane = outletLane;
            Speed = speed; Spacing = spacing;
            Origin = blocks[0].TransportLanePosition(back[0]);
            Step = blocks[0].TransportLanePosition(front[0]) - Origin;
            Items = new ConveyorTransportStore<ConveyorTransportItem>(blocks.Length * 2, blocks.Length * 2 - 1);
            lastTime = Time.time;
        }

        internal bool Adopt()
        {
            // Validate the entire snapshot before moving ownership. A failed
            // import leaves every original slot untouched.
            double nextPosition = double.PositiveInfinity;
            double importTolerance = 0.001 / Spacing;
            for (int b = Blocks.Length - 1; b >= 0; b--)
            {
                if (!Blocks[b].CanOwnConveyorTransport) return false;
                for (int side = 1; side >= 0; side--)
                {
                    int lane = side == 0 ? Back[b] : Front[b];
                    if (!Blocks[b].ReadTransportImport(lane, out ConveyorTransportItem item, out Vector3 position)) continue;
                    double coordinate = Vector3.Dot(position - Origin, Step) / Step.sqrMagnitude;
                    int slot = b * 2 + side;
                    if (coordinate < slot - 1 - importTolerance || coordinate > slot + importTolerance) return false;
                    coordinate = System.Math.Min(slot, System.Math.Max(slot - 1, coordinate));
                    // Float world positions can round a dense gap slightly below
                    // the exact spacing. Repair at most 1 mm on import; a real
                    // overlap keeps its legacy owner instead of being overwritten.
                    if (coordinate > nextPosition - 1)
                    {
                        if (coordinate - (nextPosition - 1) > importTolerance) return false;
                        coordinate = nextPosition - 1;
                    }
                    if (!Items.TryInsert(coordinate, item)) return false;
                    nextPosition = coordinate;
                }
            }
            Active = true;
            for (int b = 0; b < Blocks.Length; b++) Blocks[b].BindConveyorTransport(this, b, Front[b], Back[b]);
            Inlet?.BindTransportPort(this, true);
            Outlet?.BindTransportPort(this, false);
            ScheduleOutput();
            return true;
        }

        internal void Synchronize()
        {
            if (!Active) return;
            double now = Time.time;
            double elapsed = System.Math.Max(0, now - lastTime);
            lastTime = now;
            if (Items.Advance(elapsed * Speed / Spacing)) unchecked { Revision++; }
        }

        internal void TickBoundaries()
        {
            if (!Active || boundaryFrame == Time.frameCount) return;
            boundaryFrame = Time.frameCount;
            Synchronize();
            if (Time.time >= nextOutputTime)
            {
                BoundaryChecks++;
                nextOutputTime = double.PositiveInfinity;
                if (Outlet != null && Items.Count > 0)
                {
                    if (Items.LastPosition < Items.End - 0.00001) ScheduleOutput();
                    else
                    {
                        OutputAttempts++;
                        if (Blocks[Blocks.Length - 1].TryMoveStraightConveyorDataLaneToCached(
                            Outlet, Front[Front.Length - 1], OutletLane, Spacing))
                        {
                            Outlet.RefreshConveyorActivityRegistration(true, false);
                            ScheduleOutput();
                        }
                        // A blocked output has no periodic retry. Its port's
                        // occupancy/hold/settled event invalidates this deadline.
                    }
                }
            }
            if (!Active) return;
            TryScheduledInput();
        }

        internal void NotifyInputChanged() { if (Active) nextInputTime = 0; }
        internal void NotifyOutputChanged()
        {
            if (!Active) return;
            Synchronize();
            ScheduleOutput();
        }

        private void ScheduleOutput()
        {
            nextOutputTime = Outlet == null || Items.Count == 0 ? double.PositiveInfinity
                : Time.time + System.Math.Max(0, Items.End - Items.LastPosition) * Spacing / Speed;
        }

        internal bool TryScheduledInput()
        {
            if (!Active || Time.time < nextInputTime) return false;
            Synchronize();
            BoundaryChecks++;
            nextInputTime = double.PositiveInfinity;
            if (Inlet == null || Items.Count >= Items.End + 1) return false;
            if (Items.FirstPosition < -0.0000001)
            {
                nextInputTime = Time.time - Items.FirstPosition * Spacing / Speed;
                return false;
            }
            double readyTime = Inlet.GetTransportInputReadyTime(InletLane);
            if (!Active) return false;
            if (readyTime > Time.time)
            {
                nextInputTime = readyTime;
                return false;
            }
            InputAttempts++;
            bool moved = Inlet.TryMoveStraightConveyorDataLaneToCached(Blocks[0], InletLane, Back[0], Spacing);
            // Accept/Clear fire notifications synchronously. A successful
            // input cannot accept another item until one spacing has elapsed.
            if (moved) nextInputTime = Time.time + Spacing / Speed;
            return moved;
        }

        internal bool Read(int slot, out ConveyorTransportItem item, out double position)
        {
            Synchronize();
            return Items.TryGetLane(slot, out item, out position);
        }

        internal bool Accept(int slot, ConveyorTransportItem item)
        {
            Synchronize();
            // Normal boundary input enters from the previous legacy block.
            if (slot != 0 || Items.TryGetLane(0, out _, out _)) return false;
            item.Gate.MarkSettled();
            bool inserted = Items.TryInsert(-1, item);
            if (inserted)
            {
                unchecked { Revision++; }
                nextInputTime = Time.time + Spacing / Speed;
                // Appending at the tail leaves an existing head's appointment.
                if (Items.Count == 1) ScheduleOutput();
            }
            return inserted;
        }

        internal void Remove(int slot)
        {
            Synchronize();
            if (Items.RemoveLane(slot, out _))
            {
                unchecked { Revision++; }
                NotifyInputChanged();
                ScheduleOutput();
            }
        }

        internal Vector3 WorldPosition(double position) => Origin + Step * (float)position;

        internal void Release()
        {
            if (!Active) return;
            Synchronize();
            Active = false;
            Inlet?.UnbindTransportPort(this);
            Outlet?.UnbindTransportPort(this);
            // Detach all bindings before exporting. These are the only writes
            // back to block storage after adoption, at an ownership boundary.
            for (int b = 0; b < Blocks.Length; b++) Blocks[b].DetachConveyorTransport(this);
            for (int i = 0; i < Items.Count; i++)
            {
                Items.TryGetAt(i, out ConveyorTransportItem item, out double position);
                int slot = Items.GetReservedLane(i, position);
                int b = slot / 2;
                int lane = (slot & 1) == 0 ? Back[b] : Front[b];
                if (Blocks[b] != null) Blocks[b].RestoreTransportItem(lane, item, WorldPosition(position));
            }
            for (int b = 0; b < Blocks.Length; b++)
                if (Blocks[b] != null) Blocks[b].RefreshConveyorActivityRegistration(true, false);
        }
    }
}
