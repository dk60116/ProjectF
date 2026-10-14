using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ProjectF.Simulation.Monsters;
using UnityEngine;

namespace ProjectF.Monsters
{
    // One fixed-tick owner for all actors. No per-monster Update/physics agent.
    public sealed class MonsterWorld : IDisposable, IMapObjectUpdateTick, IMapObjectSimulationIdentity
    {
        private const float MinimumFacingDirectionSquared = 0.000000000001f;
        private const float BodyCellSize = 2f;
        private const float BodySkin = .001f;
        private const float ApproachArrivalSquared = .0001f;
        private const float ApproachStallSeconds = 2f;
        private const float TargetRetrySeconds = 4f;
        private const float MaximumNavigationLeg = 48f;
        private const float PriorityTwoEncounterRange = 5f;

        private sealed class TargetReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly TargetReferenceComparer Instance = new TargetReferenceComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object target) => RuntimeHelpers.GetHashCode(target);
        }

        private sealed class Actor : IDisposable
        {
            internal MonsterInstance Monster;
            internal MonsterAnimationView Animation;
            internal Vector3 Home;
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal readonly Vector3[] Path = new Vector3[128];
            internal int PathCount, PathIndex;
            internal Vector3 PathGoal;
            internal float SearchTimer, PathTimer;
            internal Vector3 Approach, ApproachAnchor;
            internal Vector3 OrbitCenter;
            internal float OrbitRadius;
            internal MonsterBodyShape ApproachBody;
            internal Vector2Int ApproachCell;
            internal bool HasApproach;
            internal bool DirectApproach;
            internal readonly Vector2Int[] FailedApproachCells = new Vector2Int[8];
            internal int FailedApproachCount;
            internal float ApproachTimer, StalledTime, BestApproachDistance;
            internal int ApproachRetries;
            internal Dictionary<object, float> RejectedTargets;
            internal bool Returning;
            internal bool FinishingStructure;
            internal long NavigationRevision;
            internal Player PlayerTarget;
            internal TerrainGenerator.InstallationCombatTarget BuildingTarget;
            internal TerrainGenerator.InstallationCombatTarget RaidDestination;
            internal MonsterAttackCycle Attack;
            internal long AttackIntervalTicks;
            internal MonsterBodyShape Body;
            internal Vector2Int BodyCell;
            internal int BodyId;
            internal int BodyLayer;
            internal bool BodyIndexed;

            public void Dispose()
            {
                Animation.Dispose();
                Monster.Dispose();
                if (Monster.Root != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(Monster.Root);
                    else UnityEngine.Object.DestroyImmediate(Monster.Root);
                }
            }
        }

        private static MonsterWorld current;
        private readonly TerrainGenerator terrain;
        private readonly MonsterWorldView presentation;
        private readonly Func<Vector3, bool> canMoveTo;
        private readonly List<Actor> actors = new List<Actor>();
        private readonly List<TerrainGenerator.InstallationCombatTarget> combatCandidates = new List<TerrainGenerator.InstallationCombatTarget>(4);
        private readonly List<object> expiredTargets = new List<object>(4);
        private readonly Dictionary<Vector2Int, List<Actor>> bodyCells = new Dictionary<Vector2Int, List<Actor>>();
        private readonly Dictionary<Vector2Int, List<Actor>> approachCells = new Dictionary<Vector2Int, List<Actor>>();
        private readonly Stack<List<Actor>> bodyBucketPool = new Stack<List<Actor>>();
        private float maximumBodyRadius;
        private int nextBodyId;
        private Player collisionPlayer;
        private CapsuleCollider playerCapsule;
        private Rigidbody playerBody;
        private MonsterBodyShape playerShape;
        private bool hasPlayerBody;
        private bool disposed;
        private int searchBudget, pathBudget, approachBudget, simulationCursor;
        private float simulationTime;
        public long SimulationId => long.MinValue + 3L;
        public static MonsterWorld Current => current;

        private MonsterWorld(TerrainGenerator terrain)
        {
            this.terrain = terrain;
            canMoveTo = terrain.CanMonsterMoveTo;
            presentation = MonsterWorldView.Create(this, terrain.transform);
            MapObjectTickManager.RegisterUpdateTick(this);
        }

        public static void Register(MonsterInstance monster, MonsterDefinition definition, TerrainGenerator terrain)
        {
            if (monster == null || definition == null || terrain == null)
                throw new ArgumentNullException("Monster, definition and terrain are required.");
            if (current == null || current.disposed || current.terrain != terrain)
            {
                current?.Dispose();
                current = new MonsterWorld(terrain);
            }
            for (int i = 0; i < current.actors.Count; i++)
                if (ReferenceEquals(current.actors[i].Monster, monster)) return;
            var actor = new Actor
            {
                Monster = monster, Home = monster.Root.transform.position,
                Position = monster.Root.transform.position, Rotation = monster.Root.transform.rotation,
                AttackIntervalTicks = DeterministicSimulationUnits.SecondsToTicks(monster.Data.Stats.AttackInterval),
                Animation = new MonsterAnimationView(monster.Root, definition.Animations),
                BodyId = ++current.nextBodyId
            };
            current.actors.Add(actor);
            current.UpdateBody(actor);
        }

        public bool IsOccupied(Vector3 position, float radius)
        {
            for (int i = 0; i < actors.Count; i++)
                if (actors[i].Monster.IsActive
                    && FlatDistanceSquared(actors[i].Position, position) < radius * radius)
                    return true;
            return false;
        }

        public void ManagedUpdateTick(float deltaTime)
        {
            if (disposed) return;
            if (terrain == null) { Dispose(); return; }
            if (MapObjectTickManager.SimulationPaused || MapObjectTickManager.WaitingForWorldLoad) return;
            searchBudget = 2;
            pathBudget = 1;
            approachBudget = 4;
            simulationTime += Mathf.Max(0f, deltaTime);
            long elapsedTicks = DeterministicSimulationUnits.DeltaTimeToTicks(deltaTime);
            RefreshPlayerBody();
            for (int i = 0; i < actors.Count; i++) UpdateBody(actors[i]);
            // Rotate the first actor so the shared search/path budgets cannot starve later spawns.
            int count = actors.Count;
            for (int visited = 0; visited < count && actors.Count > 0; visited++)
            {
                if (simulationCursor >= actors.Count) simulationCursor = 0;
                Actor actor = actors[simulationCursor];
                if (!actor.Monster.IsActive)
                {
                    ReleaseApproach(actor);
                    RemoveBody(actor);
                    actor.Dispose();
                    actors.RemoveAt(simulationCursor);
                    continue;
                }
                simulationCursor++;
                Tick(actor, deltaTime, elapsedTicks);
                actor.Monster.ApplySimulationPose(actor.Position, actor.Rotation);
            }
            simulationCursor = actors.Count > 0 ? (simulationCursor + 1) % actors.Count : 0;
        }

        private void Tick(Actor actor, float dt, long elapsedTicks)
        {
            MonsterStats stats = actor.Monster.Data.Stats;
            actor.SearchTimer -= dt;
            actor.PathTimer -= dt;
            actor.ApproachTimer -= dt;
            if (actor.Monster.CurrentHealth <= 0f)
            {
                ClearTarget(actor);
                actor.Animation.SetState(MonsterAnimationState.Death);
                actor.Animation.Tick(dt);
                return;
            }
            ResolveBodyOverlap(actor, Mathf.Max(.5f, stats.MoveSpeed) * Mathf.Max(0f, dt));
            Vector3 position = actor.Position;
            bool hasPlayer = CanTargetPlayer(actor.PlayerTarget)
                && FlatDistanceSquared(actor.Home, actor.PlayerTarget.transform.position) <= stats.LeashRange * stats.LeashRange;
            bool hasBuilding = terrain.TryRefreshCombatTarget(actor.BuildingTarget, out var currentBuilding);
            if (hasBuilding) actor.BuildingTarget = currentBuilding;
            if ((actor.PlayerTarget != null || actor.BuildingTarget.Target != null)
                && !hasPlayer && !hasBuilding) ClearTarget(actor, preserveDestination: true);
            // Returning is a confirmed empty search, not a lock that disables further searches.
            bool searchPending = actor.Attack.CanRetarget(elapsedTicks) && actor.SearchTimer <= 0f;
            if (searchPending && searchBudget > 0)
            {
                searchBudget--;
                searchPending = false;
                AcquireTarget(actor, position, stats);
                actor.SearchTimer = 0.35f;
            }
            hasPlayer = actor.PlayerTarget != null;
            hasBuilding = actor.BuildingTarget.Target != null;
            bool hasTarget = hasPlayer || hasBuilding;
            Vector3 point = hasPlayer ? actor.PlayerTarget.transform.position
                : hasBuilding ? actor.BuildingTarget.ClosestPoint(position) : actor.Home;
            point.y = position.y;
            float attackRange = stats.AttackRange;
            float attackDistance = FlatDistanceSquared(position, point);
            // A short-range attack must not require physically overlapping the player's body.
            if (hasPlayer && hasPlayerBody && actor.PlayerTarget == collisionPlayer
                && attackRange < actor.Body.Radius + playerShape.Radius + BodySkin * 2f)
            {
                attackRange = actor.Body.Radius + playerShape.Radius + BodySkin * 2f;
                attackDistance = FlatDistanceSquared(actor.Body.Center, playerShape.Center);
            }
            bool inRange = hasTarget && attackDistance <= attackRange * attackRange
                && HasAttackLine(position, point, actor.BuildingTarget.Target);
            // Keep an attacker in place; approaching actors must reach their reserved side first.
            if (hasTarget && !actor.HasApproach && inRange && IsApproachFree(actor, actor.Body))
                ReserveApproach(actor, position, actor.Body, TargetAnchor(actor));
            if (actor.HasApproach && FlatDistanceSquared(actor.ApproachAnchor, TargetAnchor(actor)) > .01f)
            {
                ReleaseApproach(actor);
                actor.ApproachTimer = 0f;
                actor.FailedApproachCount = actor.ApproachRetries = 0;
            }
            bool atApproach = actor.HasApproach && FlatDistanceSquared(position, actor.Approach) <= ApproachArrivalSquared;
            inRange &= atApproach || actor.Attack.IsAttacking;
            // A budget-delayed search must get another chance next tick, not after another full attack cycle.
            MonsterAttackEvent attackEvent = actor.Attack.Tick(elapsedTicks, inRange && !searchPending, actor.AttackIntervalTicks);
            if ((attackEvent & MonsterAttackEvent.Hit) != 0 && inRange)
            {
                if (actor.PlayerTarget != null) actor.PlayerTarget.TakeDamage(stats.AttackDamage);
                else terrain.DamageInstallation(actor.BuildingTarget, stats.AttackDamage);
            }
            if ((attackEvent & MonsterAttackEvent.Started) != 0)
            {
                actor.Animation.SetState(MonsterAnimationState.Attack, true, stats.AttackInterval);
                presentation.NotifyAttackAnimationStarted(actor.Monster);
            }
            bool moved = false;
            if (!inRange && !actor.Attack.IsAttacking && stats.MoveSpeed > 0f
                && (hasTarget || actor.Returning && !searchPending && FlatDistanceSquared(position, point) > 0.04f))
            {
                Vector3 goal = point;
                if (!hasTarget || TryGetApproach(actor, stats, out goal))
                {
                    if (hasTarget && actor.HasApproach)
                        goal = GetOrbitWaypoint(actor, goal);
                    moved = Move(actor, goal, stats, dt);
                    if (hasTarget && (actor.PlayerTarget != null || actor.BuildingTarget.Target != null)
                        && actor.HasApproach && dt > 0f)
                    {
                        float distance = FlatDistanceSquared(actor.Position, actor.Approach);
                        if (distance < actor.BestApproachDistance - .0025f)
                        { actor.BestApproachDistance = distance; actor.StalledTime = 0f; }
                        else actor.StalledTime += dt;
                        if (actor.StalledTime >= ApproachStallSeconds)
                        {
                            if (actor.ApproachRetries++ == 0)
                                FailApproach(actor);
                            else RejectTarget(actor);
                        }
                    }
                }
            }
            if (!moved && hasTarget && (inRange || atApproach || actor.Attack.IsAttacking))
                Face(actor, point - position, stats.TurnSpeed * dt);
            if (!actor.Attack.IsAttacking)
                actor.Animation.SetState(moved ? MonsterAnimationState.Move : MonsterAnimationState.Idle);
            actor.Animation.Tick(dt);
        }

        private void AcquireTarget(Actor actor, Vector3 position, MonsterStats stats)
        {
            PruneRejectedTargets(actor);
            Player player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            bool playerAvailable = CanTargetPlayer(player)
                && (actor.PlayerTarget == player
                    || FlatDistanceSquared(position, player.transform.position) <= stats.DetectionRange * stats.DetectionRange)
                && FlatDistanceSquared(actor.Home, player.transform.position) <= stats.LeashRange * stats.LeashRange
                && (actor.BuildingTarget.Target == null || HasAttackLine(position, player.transform.position, null));
            bool retryPending = playerAvailable && IsRejected(actor, player);
            if (playerAvailable && !retryPending)
            {
                if (actor.PlayerTarget != player)
                {
                    ClearTarget(actor);
                    actor.PlayerTarget = player;
                }
                actor.Returning = false;
                return;
            }
            // Finish an encountered structure before resuming the persistent raid destination.
            if (actor.FinishingStructure && actor.BuildingTarget.Target != null) return;
            TerrainGenerator.InstallationCombatTarget priorityOne = default;
            TerrainGenerator.InstallationCombatTarget priorityTwo = default;
            terrain.TryRefreshCombatTarget(actor.RaidDestination, out var priorityThree);
            if (priorityThree.Target != null && priorityThree.Target.BoundItemDefinition.MonsterAttackPriority != 3)
                priorityThree = default;
            bool hasPriorityThree = priorityThree.Target != null;
            if (hasPriorityThree && IsRejected(actor, priorityThree.Target))
            {
                retryPending = true;
                priorityThree = default;
            }
            bool hasDestination = priorityThree.Target != null;
            float priorityOneDistance = float.MaxValue;
            float priorityThreeDistance = float.MaxValue;
            float detectionSquared = stats.DetectionRange * stats.DetectionRange;
            float best = float.MaxValue;
            int radius = Mathf.CeilToInt(stats.DetectionRange + .5f);
            Vector2Int center = TerrainGenerator.GetWorldBlockCoordinate(position);
            int firstX = center.x - radius;
            int lastX = center.x + radius;
            int firstZ = center.y - radius;
            int lastZ = center.y + radius;
            for (int z = firstZ; z <= lastZ; z++)
            for (int x = firstX; x <= lastX; x++)
            {
                terrain.CollectInstallationCombatTargets(new Vector2Int(x, z), combatCandidates);
                for (int i = 0; i < combatCandidates.Count; i++)
                {
                    var candidate = combatCandidates[i];
                    int priority = candidate.Target.BoundItemDefinition.MonsterAttackPriority;
                    if (priority == 0) continue;
                    Vector3 point = candidate.ClosestPoint(position);
                    float distance = FlatDistanceSquared(position, point);
                    if (distance > detectionSquared) continue;
                    if (priority == 3) hasPriorityThree = true;
                    if (IsRejected(actor, candidate.Target)) { retryPending = true; continue; }
                    if (priority == 1)
                    {
                        if (distance < priorityOneDistance)
                        {
                            priorityOneDistance = distance;
                            priorityOne = candidate;
                        }
                        continue;
                    }
                    if (priority == 3)
                    {
                        if (!hasDestination && distance < priorityThreeDistance)
                        {
                            priorityThreeDistance = distance;
                            priorityThree = candidate;
                        }
                        continue;
                    }
                    if (distance >= best) continue;
                    best = distance;
                    priorityTwo = candidate;
                }
            }
            bool encounteredTwo = priorityTwo.Target != null
                && best <= PriorityTwoEncounterRange * PriorityTwoEncounterRange;
            var nearest = encounteredTwo || priorityThree.Target == null ? priorityTwo : priorityThree;
            // Finish the surrounding factory after all detected threes are gone, not just the raid destination.
            if (nearest.Target == null && !hasPriorityThree) nearest = priorityOne;
            if (actor.PlayerTarget != null || !ReferenceEquals(actor.BuildingTarget.Target, nearest.Target))
            {
                ClearTarget(actor, preserveDestination: true);
                actor.BuildingTarget = nearest;
            }
            actor.RaidDestination = priorityThree;
            actor.FinishingStructure = encounteredTwo;
            actor.Returning = nearest.Target == null && !retryPending;
        }

        private static bool CanTargetPlayer(Player player) => player != null
            && player.isActiveAndEnabled && !player.IsDead
            && !(GameManager.Instance != null && GameManager.Instance.CloakingMode);

        private void ClearTarget(Actor actor, bool preserveDestination = false)
        {
            ReleaseApproach(actor);
            actor.ApproachTimer = actor.StalledTime = 0f;
            actor.ApproachRetries = 0;
            actor.FailedApproachCount = 0;
            actor.PlayerTarget = null;
            actor.BuildingTarget = default;
            if (!preserveDestination) actor.RaidDestination = default;
            actor.Returning = false;
            actor.FinishingStructure = false;
            actor.SearchTimer = 0f;
            actor.PathCount = actor.PathIndex = 0;
            actor.Attack.CancelHit();
        }

        private bool Move(Actor actor, Vector3 goal, MonsterStats stats, float dt)
        {
            Vector3 position = actor.Position;
            // Local path legs keep raids and long homeward trips inside the pathfinder's bounded grid.
            goal = GetNavigationGoal(position, goal);
            Vector3 destination = goal;
            if (!HasWalkableStep(position, goal))
            {
                bool stale = actor.NavigationRevision != terrain.AnimalNavigationRevision
                    || FlatDistanceSquared(actor.PathGoal, goal) > 0.25f
                    || actor.PathIndex >= actor.PathCount;
                if (stale && actor.PathTimer <= 0f && pathBudget > 0)
                {
                    pathBudget--;
                    actor.PathTimer = 0.5f;
                    if (terrain.TryGetMonsterPathBlocker(position, goal, out var blocker)
                        && (blocker.Target.BoundItemDefinition.MonsterAttackPriority <= 1
                            || blocker.Target.BoundItemDefinition.MonsterAttackPriority == 2
                                && actor.BuildingTarget.Target != null
                                && actor.BuildingTarget.Target.BoundItemDefinition.MonsterAttackPriority == 3)
                        && !IsRejected(actor, blocker.Target)
                        && blocker.Sequence != actor.BuildingTarget.Sequence)
                    {
                        ClearTarget(actor, preserveDestination: true);
                        actor.BuildingTarget = blocker;
                        actor.FinishingStructure = true;
                        actor.SearchTimer = 0.35f;
                        return false;
                    }
                    actor.PathGoal = goal;
                    actor.NavigationRevision = terrain.AnimalNavigationRevision;
                    actor.PathIndex = 0;
                    Vector3 navigationCenter = Vector3.Lerp(position, goal, .5f);
                    float navigationRadius = Mathf.Max(stats.LeashRange, Vector3.Distance(position, goal) * .5f + 2f);
                    actor.PathCount = AnimalGridPathfinder.FindPath(terrain, position, goal,
                        navigationCenter, navigationRadius, true, actor.Path, canMoveTo);
                    if (actor.PathCount == 0 && actor.HasApproach)
                    {
                        // An orbit waypoint can be sealed off while the actual attack position is reachable.
                        if (!actor.DirectApproach && FlatDistanceSquared(goal, actor.Approach) > .01f)
                        { actor.DirectApproach = true; actor.PathTimer = 0f; }
                        else FailApproach(actor);
                    }
                }
                // Never continue an old path through a newly placed obstacle.
                if (actor.NavigationRevision != terrain.AnimalNavigationRevision
                    || actor.PathIndex >= actor.PathCount) return false;
                destination = actor.Path[actor.PathIndex];
                destination.y = position.y;
                if (FlatDistanceSquared(position, destination) < 0.01f)
                {
                    actor.PathIndex++;
                    return false;
                }
            }
            else actor.PathCount = actor.PathIndex = 0;
            Vector3 next = Vector3.MoveTowards(position, destination, stats.MoveSpeed * dt);
            next = position + ConstrainBodyMove(actor, next - position);
            if (!HasWalkableStep(position, next))
            {
                actor.PathCount = actor.PathIndex = 0;
                return false;
            }
            // A tiny final step can be required to enter the exact attack range.
            if (position.x == next.x && position.z == next.z) return false;
            actor.Position = next;
            UpdateBody(actor);
            Face(actor, next - position, stats.TurnSpeed * dt);
            return true;
        }

        private bool HasWalkableStep(Vector3 start, Vector3 end) => AnimalGridPathfinder.HasWalkableLine(
            terrain, start, end, start, Mathf.Max(1f, Vector3.Distance(start, end) + 1f), true, canMoveTo);

        private Vector3 GetNavigationGoal(Vector3 position, Vector3 goal)
        {
            if (FlatDistanceSquared(position, goal) <= MaximumNavigationLeg * MaximumNavigationLeg) return goal;
            // A clipped homeward leg can end inside a wall; choose ground before asking A* for a detour.
            for (float distance = MaximumNavigationLeg; distance >= 1f; distance -= 1f)
            {
                Vector3 candidate = Vector3.MoveTowards(position, goal, distance);
                if (canMoveTo(candidate)) return candidate;
            }
            return position;
        }

        private bool HasAttackLine(Vector3 start, Vector3 end, IMapObjectTarget allowedTarget)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(start, end) * 5f));
            for (int i = 1; i <= steps; i++)
            {
                Vector3 point = Vector3.Lerp(start, end, (float)i / steps);
                if (canMoveTo(point)) continue;
                if (allowedTarget == null
                    || !terrain.IsCombatTargetOccupant(TerrainGenerator.GetWorldBlockCoordinate(point), allowedTarget)) return false;
            }
            return true;
        }

        private Vector3 TargetAnchor(Actor actor) => actor.PlayerTarget != null
            ? actor.PlayerTarget.transform.position : actor.BuildingTarget.Target.WorldPosition;

        private bool IsRejected(Actor actor, object target) => target != null && actor.RejectedTargets != null
            && actor.RejectedTargets.TryGetValue(target, out float until) && until > simulationTime;

        private void PruneRejectedTargets(Actor actor)
        {
            if (actor.RejectedTargets == null || actor.RejectedTargets.Count == 0) return;
            expiredTargets.Clear();
            foreach (var rejected in actor.RejectedTargets)
                if (rejected.Value <= simulationTime) expiredTargets.Add(rejected.Key);
            for (int i = 0; i < expiredTargets.Count; i++) actor.RejectedTargets.Remove(expiredTargets[i]);
            expiredTargets.Clear();
        }

        private void RejectTarget(Actor actor)
        {
            object target = actor.PlayerTarget != null ? (object)actor.PlayerTarget : actor.BuildingTarget.Target;
            if (target == null) return;
            // Do not evict unexpired failures: a crowded factory can have many more than four blocked targets.
            if (actor.RejectedTargets == null)
                actor.RejectedTargets = new Dictionary<object, float>(4, TargetReferenceComparer.Instance);
            actor.RejectedTargets[target] = simulationTime + TargetRetrySeconds;
            ClearTarget(actor, preserveDestination: true);
        }

        private void ReleaseApproach(Actor actor)
        {
            if (!actor.HasApproach) return;
            List<Actor> bucket = approachCells[actor.ApproachCell];
            bucket.Remove(actor);
            if (bucket.Count == 0)
            {
                approachCells.Remove(actor.ApproachCell);
                bodyBucketPool.Push(bucket);
            }
            actor.HasApproach = false;
            actor.StalledTime = 0f;
        }

        private void FailApproach(Actor actor)
        {
            if (!actor.HasApproach) return;
            Vector2Int cell = TerrainGenerator.GetWorldBlockCoordinate(actor.Approach);
            if (!IsFailedApproach(actor, cell)) actor.FailedApproachCells[actor.FailedApproachCount++] = cell;
            ReleaseApproach(actor);
            actor.PathCount = actor.PathIndex = 0;
            actor.PathTimer = actor.ApproachTimer = 0f;
            if (actor.FailedApproachCount >= actor.FailedApproachCells.Length) RejectTarget(actor);
        }

        private static bool IsFailedApproach(Actor actor, Vector2Int cell)
        {
            for (int i = 0; i < actor.FailedApproachCount; i++)
                if (actor.FailedApproachCells[i] == cell) return true;
            return false;
        }

        private void ReserveApproach(Actor actor, Vector3 goal, in MonsterBodyShape shape, Vector3 anchor)
        {
            ReleaseApproach(actor);
            actor.Approach = goal;
            actor.ApproachBody = shape;
            actor.ApproachAnchor = anchor;
            actor.OrbitCenter = actor.PlayerTarget == collisionPlayer && hasPlayerBody ? playerShape.Center : anchor;
            float ringRadius = (float)Math.Sqrt(FlatDistanceSquared(shape.Center, actor.OrbitCenter));
            var cells = actor.BuildingTarget.State?.occupiedCoordinates;
            if (actor.BuildingTarget.Target != null
                && !(actor.BuildingTarget.Target.BoundItemDefinition.mapObject is Vehicle) && cells != null && cells.Count > 0)
            {
                int minX = cells[0].x, maxX = minX, minZ = cells[0].y, maxZ = minZ;
                for (int i = 1; i < cells.Count; i++)
                {
                    minX = Mathf.Min(minX, cells[i].x); maxX = Mathf.Max(maxX, cells[i].x);
                    minZ = Mathf.Min(minZ, cells[i].y); maxZ = Mathf.Max(maxZ, cells[i].y);
                }
                actor.OrbitCenter = new Vector3((minX + maxX) * .5f, actor.Position.y, (minZ + maxZ) * .5f);
                float halfX = (maxX - minX + 1) * .5f, halfZ = (maxZ - minZ + 1) * .5f;
                ringRadius = (float)Math.Sqrt(halfX * halfX + halfZ * halfZ) + actor.Monster.Data.Stats.AttackRange * .85f;
            }
            actor.OrbitRadius = ringRadius + actor.Body.Radius + maximumBodyRadius + BodySkin * 2f;
            actor.ApproachCell = BodyCell(shape.Center);
            if (!approachCells.TryGetValue(actor.ApproachCell, out List<Actor> bucket))
            {
                bucket = bodyBucketPool.Count > 0 ? bodyBucketPool.Pop() : new List<Actor>(4);
                approachCells.Add(actor.ApproachCell, bucket);
            }
            bucket.Add(actor);
            actor.HasApproach = true;
            actor.DirectApproach = false;
            actor.BestApproachDistance = FlatDistanceSquared(actor.Position, goal);
            actor.StalledTime = 0f;
        }

        // Both approaching and stationary attackers occupy space. Search only nearby indexed bodies/slots.
        private bool IsApproachFree(Actor actor, in MonsterBodyShape shape)
        {
            if (hasPlayerBody && BodiesOverlap(shape, playerShape)) return false;
            float reach = shape.Radius + maximumBodyRadius + BodySkin;
            Vector2Int first = BodyCell(shape.Center - new Vector3(reach, 0f, reach));
            Vector2Int last = BodyCell(shape.Center + new Vector3(reach, 0f, reach));
            for (int z = first.y; z <= last.y; z++)
            for (int x = first.x; x <= last.x; x++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (approachCells.TryGetValue(cell, out List<Actor> reservations))
                    for (int i = 0; i < reservations.Count; i++)
                    {
                        Actor other = reservations[i];
                        if (other != actor && other.Monster.HasCollision && BodiesOverlap(shape, other.ApproachBody))
                            return false;
                    }
                if (bodyCells.TryGetValue(cell, out List<Actor> bodies))
                    for (int i = 0; i < bodies.Count; i++)
                    {
                        Actor other = bodies[i];
                        if (other != actor && other.Monster.HasCollision && BodiesOverlap(shape, other.Body))
                            return false;
                    }
            }
            return true;
        }

        private static bool BodiesOverlap(in MonsterBodyShape first, in MonsterBodyShape second)
        {
            float radius = first.Radius + second.Radius + BodySkin;
            return first.OverlapsHeight(second) && FlatDistanceSquared(first.Center, second.Center) < radius * radius;
        }

        private bool TryGetApproach(Actor actor, MonsterStats stats, out Vector3 goal)
        {
            goal = actor.Approach;
            if (actor.HasApproach && canMoveTo(goal)) return true;
            ReleaseApproach(actor);
            if (actor.ApproachTimer > 0f || approachBudget <= 0) return false;
            approachBudget--;
            actor.ApproachTimer = .35f;
            Vector3 anchor = TargetAnchor(actor);
            float best = float.MaxValue;
            MonsterBodyShape chosen = default;
            bool player = actor.PlayerTarget != null;
            bool contactAttack = player && hasPlayerBody && actor.PlayerTarget == collisionPlayer
                && stats.AttackRange < actor.Body.Radius + playerShape.Radius + BodySkin * 2f;
            float range = contactAttack ? actor.Body.Radius + playerShape.Radius + BodySkin * 2f : stats.AttackRange;
            MonsterBodyShape localBody = actor.Monster.GetBodyShape(Vector3.zero, Quaternion.Euler(0f, 0f, 0f));
            var cells = actor.BuildingTarget.State?.occupiedCoordinates;
            bool radialTarget = player || actor.BuildingTarget.Target.BoundItemDefinition.mapObject is Vehicle;
            int count = radialTarget ? 1 : cells != null ? cells.Count : 0;
            int bands = radialTarget ? 1 : 2;
            for (int band = 0; band < bands && best == float.MaxValue; band++)
            for (int cell = 0; cell < count; cell++)
            {
                Vector3 center = radialTarget ? anchor : new Vector3(cells[cell].x, actor.Position.y, cells[cell].y);
                if (contactAttack) center = playerShape.Center;
                // Bound work for very long-range actors while retaining enough samples for small bodies.
                int samples = Mathf.Max(16, Mathf.Min(64, Mathf.CeilToInt(6.283186f * (range + .5f)
                    / Mathf.Max(.2f, actor.Body.Radius * 2f))));
                samples = (samples + 7) / 8 * 8;
                for (int i = 0; i < samples; i++)
                {
                    double radians = i * (Math.PI * 2 / samples);
                    Vector3 radial = new Vector3((float)Math.Sin(radians), 0f, (float)Math.Cos(radians));
                    float radius = contactAttack ? range - BodySkin * .5f : radialTarget ? range * .92f
                        : .5f / Mathf.Max(Mathf.Abs(radial.x), Mathf.Abs(radial.z)) + range * (band == 0 ? .85f : .35f);
                    Vector3 candidate = center + radial * radius;
                    if (contactAttack)
                    {
                        Vector3 contactCenter = candidate;
                        for (int iteration = 0; iteration < 16; iteration++)
                        {
                            Vector3 direction = anchor - candidate;
                            direction.y = 0f;
                            candidate = contactCenter - Quaternion.LookRotation(direction) * localBody.Center;
                        }
                    }
                    candidate.y = actor.Position.y;
                    float distance = FlatDistanceSquared(actor.Position, candidate);
                    if (distance >= best || !canMoveTo(candidate)
                        || IsFailedApproach(actor, TerrainGenerator.GetWorldBlockCoordinate(candidate))) continue;
                    Vector3 point = player ? anchor : actor.BuildingTarget.ClosestPoint(candidate);
                    point.y = candidate.y;
                    Vector3 bodyCenter = candidate + Quaternion.LookRotation(point - candidate) * localBody.Center;
                    float heightOffset = bodyCenter.y - localBody.Center.y;
                    var shape = new MonsterBodyShape(bodyCenter, localBody.Radius,
                        localBody.MinY + heightOffset, localBody.MaxY + heightOffset);
                    float attackDistance = contactAttack ? FlatDistanceSquared(shape.Center, playerShape.Center)
                        : FlatDistanceSquared(candidate, point);
                    if (attackDistance > range * range + .000001f || !IsApproachFree(actor, shape)
                        || !HasAttackLine(candidate, point, actor.BuildingTarget.Target)) continue;
                    best = distance;
                    goal = candidate;
                    chosen = shape;
                }
            }
            if (best == float.MaxValue) { RejectTarget(actor); return false; }
            ReserveApproach(actor, goal, chosen, anchor);
            return true;
        }

        private Vector3 GetOrbitWaypoint(Actor actor, Vector3 goal)
        {
            if (actor.DirectApproach) return goal;
            Vector3 center = actor.OrbitCenter;
            Vector3 from = actor.Body.Center - center;
            Vector3 to = actor.ApproachBody.Center - center;
            float radius = actor.OrbitRadius;
            double angle = Math.Atan2(from.x, from.z);
            double difference = Math.Atan2(to.x, to.z) - angle;
            if (difference > Math.PI) difference -= Math.PI * 2;
            if (difference < -Math.PI) difference += Math.PI * 2;
            // Route to a far-side slot around the occupied inner ring, not through its center.
            if (Math.Abs(difference) <= .3 || from.x * from.x + from.z * from.z > (radius + 1f) * (radius + 1f))
                return goal;
            angle += difference > 0 ? .3 : -.3;
            Vector3 waypoint = center + new Vector3((float)Math.Sin(angle) * radius, 0f, (float)Math.Cos(angle) * radius)
                - (actor.Body.Center - actor.Position);
            waypoint.y = actor.Position.y;
            return canMoveTo(waypoint) ? waypoint : goal;
        }

        private void Face(Actor actor, Vector3 direction, float degrees)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude > MinimumFacingDirectionSquared)
            {
                direction *= 1f / (float)Math.Sqrt(direction.sqrMagnitude);
                Quaternion rotation = Quaternion.RotateTowards(actor.Rotation, Quaternion.LookRotation(direction), degrees);
                MonsterBodyShape rotated = actor.Monster.GetBodyShape(actor.Position, rotation);
                if (!SweepBodies(actor.Body, rotated.Center - actor.Body.Center, actor, true, out _, out _))
                {
                    actor.Rotation = rotation;
                    UpdateBody(actor);
                    if (actor.HasApproach && FlatDistanceSquared(actor.Position, actor.Approach) <= ApproachArrivalSquared
                        && FlatDistanceSquared(actor.Body.Center, actor.ApproachBody.Center) > .000001f)
                        ReserveApproach(actor, actor.Approach, actor.Body, actor.ApproachAnchor);
                }
            }
        }

        private static Vector2Int BodyCell(Vector3 center) => new Vector2Int(
            Mathf.FloorToInt(center.x / BodyCellSize), Mathf.FloorToInt(center.z / BodyCellSize));

        private void RemoveBody(Actor actor)
        {
            if (!actor.BodyIndexed) return;
            List<Actor> bucket = bodyCells[actor.BodyCell];
            bucket.Remove(actor);
            if (bucket.Count == 0)
            {
                bodyCells.Remove(actor.BodyCell);
                bodyBucketPool.Push(bucket);
            }
            actor.BodyIndexed = false;
        }

        private void UpdateBody(Actor actor)
        {
            if (!actor.Monster.HasCollision) { RemoveBody(actor); return; }
            actor.Body = actor.Monster.GetBodyShape(actor.Position, actor.Rotation);
            actor.BodyLayer = actor.Monster.Root.layer;
            maximumBodyRadius = Mathf.Max(maximumBodyRadius, actor.Body.Radius);
            Vector2Int cell = BodyCell(actor.Body.Center);
            if (actor.BodyIndexed && actor.BodyCell == cell) return;
            RemoveBody(actor);
            if (!bodyCells.TryGetValue(cell, out List<Actor> bucket))
            {
                bucket = bodyBucketPool.Count > 0 ? bodyBucketPool.Pop() : new List<Actor>(4);
                bodyCells.Add(cell, bucket);
            }
            bucket.Add(actor);
            actor.BodyCell = cell;
            actor.BodyIndexed = true;
        }

        private void RefreshPlayerBody()
        {
            Player player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (collisionPlayer != player)
            {
                collisionPlayer = player;
                playerCapsule = player != null ? player.GetComponent<CapsuleCollider>() : null;
                playerBody = player != null ? player.GetComponent<Rigidbody>() : null;
            }
            hasPlayerBody = player != null && player.isActiveAndEnabled
                && playerCapsule != null && playerCapsule.enabled && !playerCapsule.isTrigger;
            if (hasPlayerBody)
                playerShape = MonsterBodyShape.Read(playerCapsule,
                    playerBody != null ? playerBody.position : player.transform.position,
                    playerBody != null ? playerBody.rotation : player.transform.rotation);
        }

        // Shared with player movement: queries authoritative ECS bodies, never stale PhysX poses.
        public bool TrySweepBody(Vector3 center, Vector3 direction, float distance, float radius,
            float minY, float maxY, int collisionMask, out float hitDistance, out Vector3 normal)
        {
            hitDistance = distance;
            normal = Vector3.zero;
            if (disposed || distance <= 0f) return false;
            var shape = new MonsterBodyShape(center, radius, minY, maxY);
            bool hit = SweepBodies(shape, direction * distance, null, false, out float fraction, out normal, collisionMask);
            hitDistance = fraction * distance;
            return hit;
        }

        private bool SweepBodies(in MonsterBodyShape shape, Vector3 delta, Actor ignore, bool includePlayer,
            out float fraction, out Vector3 normal, int collisionMask = ~0)
        {
            fraction = 1f;
            normal = Vector3.zero;
            if (delta.x * delta.x + delta.z * delta.z < 1e-12f) return false;
            bool hit = false;
            float reach = shape.Radius + maximumBodyRadius + BodySkin;
            Vector3 end = shape.Center + delta;
            Vector2Int first = BodyCell(new Vector3(Mathf.Min(shape.Center.x, end.x) - reach, 0f,
                Mathf.Min(shape.Center.z, end.z) - reach));
            Vector2Int last = BodyCell(new Vector3(Mathf.Max(shape.Center.x, end.x) + reach, 0f,
                Mathf.Max(shape.Center.z, end.z) + reach));
            for (int z = first.y; z <= last.y; z++)
            for (int x = first.x; x <= last.x; x++)
            {
                if (!bodyCells.TryGetValue(new Vector2Int(x, z), out List<Actor> bucket)) continue;
                for (int i = 0; i < bucket.Count; i++)
                {
                    Actor other = bucket[i];
                    if (other == ignore || !other.Monster.HasCollision || (collisionMask & (1 << other.BodyLayer)) == 0) continue;
                    if (SweepCircle(shape, delta, other.Body, out float time, out Vector3 contact) && time <= fraction)
                    { fraction = time; normal = contact; hit = true; }
                }
            }
            if (includePlayer && hasPlayerBody
                && SweepCircle(shape, delta, playerShape, out float playerTime, out Vector3 playerNormal) && playerTime <= fraction)
            { fraction = playerTime; normal = playerNormal; hit = true; }
            return hit;
        }

        private static bool SweepCircle(in MonsterBodyShape shape, Vector3 delta, in MonsterBodyShape other,
            out float time, out Vector3 normal)
        {
            time = 1f;
            normal = Vector3.zero;
            if (!shape.OverlapsHeight(other)) return false;
            double x = shape.Center.x - other.Center.x, z = shape.Center.z - other.Center.z;
            double radius = shape.Radius + other.Radius + BodySkin;
            double c = x * x + z * z - radius * radius;
            double b = x * delta.x + z * delta.z;
            // Existing overlap may escape or slide tangentially, but cannot move deeper.
            if (b >= -1e-8) return false;
            double a = delta.x * delta.x + delta.z * delta.z;
            double discriminant = b * b - a * c;
            if (discriminant <= 0) return false;
            double t = c <= 0 ? 0 : (-b - Math.Sqrt(discriminant)) / a;
            if (t < 0 || t > 1) return false;
            time = (float)t;
            float nx = (float)(x + delta.x * t), nz = (float)(z + delta.z * t);
            float length = (float)Math.Sqrt(nx * nx + nz * nz);
            if (length > 1e-6f) normal = new Vector3(nx / length, 0f, nz / length);
            return true;
        }

        private Vector3 ConstrainBodyMove(Actor actor, Vector3 delta)
        {
            if (!SweepBodies(actor.Body, delta, actor, true, out float fraction, out Vector3 normal)) return delta;
            Vector3 move = delta * fraction;
            Vector3 rest = delta * (1f - fraction);
            float dot = rest.x * normal.x + rest.z * normal.z;
            Vector3 slide = rest - normal * dot;
            float remainingLength = (float)Math.Sqrt(rest.sqrMagnitude);
            if (slide.sqrMagnitude < 1e-8f)
                slide = new Vector3(-normal.z, 0f, normal.x) * remainingLength;
            else
                slide *= remainingLength / (float)Math.Sqrt(slide.sqrMagnitude);
            // Keep a tangent sweep just outside contact despite floating-point projection error.
            slide += normal * Mathf.Min(BodySkin * .05f, remainingLength * .01f);
            MonsterBodyShape atContact = actor.Body.Translated(move);
            if (SweepBodies(atContact, slide, actor, true, out float slideFraction, out _)) slide *= slideFraction;
            Vector3 candidate = move + slide;
            if (HasWalkableStep(actor.Position, actor.Position + candidate)) return candidate;
            return move;
        }

        private void ResolveBodyOverlap(Actor actor, float maxStep)
        {
            if (!actor.BodyIndexed || maxStep <= 0f) return;
            float deepest = 0f;
            Vector3 push = Vector3.zero;
            float reach = actor.Body.Radius + maximumBodyRadius + BodySkin;
            Vector2Int first = BodyCell(actor.Body.Center - new Vector3(reach, 0f, reach));
            Vector2Int last = BodyCell(actor.Body.Center + new Vector3(reach, 0f, reach));
            for (int z = first.y; z <= last.y; z++)
            for (int x = first.x; x <= last.x; x++)
            {
                if (!bodyCells.TryGetValue(new Vector2Int(x, z), out List<Actor> bucket)) continue;
                for (int i = 0; i < bucket.Count; i++)
                {
                    Actor other = bucket[i];
                    if (other == actor || !other.Monster.HasCollision) continue;
                    FindOverlapPush(actor.Body, other.Body, actor.BodyId > other.BodyId ? 1f : -1f, ref deepest, ref push);
                }
            }
            if (hasPlayerBody) FindOverlapPush(actor.Body, playerShape, 1f, ref deepest, ref push);
            if (deepest <= 0f) return;
            Vector3 move = ConstrainBodyMove(actor, push * Mathf.Min(deepest, maxStep));
            if (!HasWalkableStep(actor.Position, actor.Position + move)) return;
            actor.Position += move;
            UpdateBody(actor);
        }

        private static void FindOverlapPush(in MonsterBodyShape shape, in MonsterBodyShape other, float fallback,
            ref float deepest, ref Vector3 push)
        {
            if (!shape.OverlapsHeight(other)) return;
            float x = shape.Center.x - other.Center.x, z = shape.Center.z - other.Center.z;
            float length = (float)Math.Sqrt(x * x + z * z);
            float depth = shape.Radius + other.Radius + BodySkin - length;
            if (depth <= deepest) return;
            deepest = depth;
            push = length > 1e-6f ? new Vector3(x / length, 0f, z / length) : new Vector3(fallback, 0f, 0f);
        }

        private static float FlatDistanceSquared(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return x * x + z * z;
        }

        internal void RefreshHealthBars(Transform parent, Camera camera)
        {
            if (disposed) return;
            for (int i = 0; i < actors.Count; i++) actors[i].Monster.RefreshHealthBar(parent, camera);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            MapObjectTickManager.UnregisterUpdateTick(this);
            for (int i = 0; i < actors.Count; i++) actors[i].Dispose();
            actors.Clear();
            combatCandidates.Clear();
            bodyCells.Clear();
            approachCells.Clear();
            bodyBucketPool.Clear();
            if (presentation != null) presentation.Release();
            if (ReferenceEquals(current, this)) current = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => current?.Dispose();
    }
}
