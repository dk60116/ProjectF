# Monster Data Authoring

Open `Window > ProjectF > Monster Data` in Unity when ready to inspect the tool.
This provides authoring, a runtime custom-ECS combat world and an Editor Play Mode spawn
harness. There is no per-actor MonoBehaviour, Unity Entities dependency or experience system.

## Data

- `Assets/Monsters`: four species assets and sixteen prefab variants from Level 1 Monster Pack.
- `MonsterDefinition` is the source of truth. Initial stats are editable placeholders;
  drops are deliberately unassigned, not inferred gameplay rewards.
- Eight common stats: health, move/turn speed, damage, attack range/interval,
  detection range, leash range. Each color can override individual stats and its drop table.
- Variant IDs remain stable across reorder/delete. Deleted IDs are not reused.
- `TryGetRuntimeData` produces an unmanaged numeric archetype for the project's custom
  data-oriented simulation. `CreateDropData` is a load-time allocation, not a tick API.
  Rendering references stay in authoring assets, outside simulation records.
- Animation clips are species-wide. Slime has no source hit clip; the tool does not invent one.
- Pack models are imported at 20 times their original size (Bat import scale 0.5 -> 10,
  others 1 -> 20). Standalone prefabs have matching 20x rest-bone translations and skinned
  renderer bounds. All transform scales remain one; no spawn-time size multiplier is used.
- Bat and Slime each have four illustrated transparent portrait Sprites in `Assets/Monsters/Portraits`.
  Bat follows the supplied spherical-head/attached-wing model; Slime follows the rounded
  dome, black eyes and scalloped ooze base. Each color is assigned to its existing variant icon;
  Ghost and Rabbit icons remain unassigned. See `BatPortraits.md` and `SlimePortraits.md`
  for references, generation prompts and import settings.

## Editor

- Searchable species/variant tree, unsaved drafts, Undo/Redo, Save/Load, validation.
- List rows display each variant's draft portrait; species rows use the first assigned portrait.
  Fixed icon slots keep names aligned when no icon is assigned; sprite aspect ratio is preserved.
- Common edits affect all inheriting variants. Clear All Overrides restores inheritance.
- Add/remove variants, create species, prefab/icon binding, ItemDefinition drop selection.
- Rebuild adds missing pack prefabs without resetting existing values, references or IDs.
- JSON uses asset GUID + local file ID, including FBX clip sub-assets. Imports validate the
  complete resulting catalog before changing drafts. Save is separate from JSON import.
- Saving refuses conflicting external source-asset edits.
- Unsaved drafts are captured before script reload and restored into temporary objects afterward.
- Preview keeps the original ToonCharacter materials and uses a manual PlayableGraph;
  it does not need an Animator Controller. Supports orbit, zoom, play/pause, speed and scrub.
- Each saved variant has a Spawn button, enabled only in Play Mode with no unsaved
  species changes. Each click places exactly one model on loaded, empty, walkable ground
  2-8 tiles from the player's cell, avoiding other test spawns and active animals.
- The selected color variant also exposes a monster-paint toggle in the detail header.
  In the Play Mode Game view, each left click places one monster at the clicked XZ position,
  using the loaded block's ground height, not a nearby fallback cell. UI clicks, occupied
  ground and pause/load barriers are rejected. Escape or another button press stops painting;
  selection/data changes, closing the window and exiting Play Mode stop it too.
  The brush owns player/installation/primary HUD pointer input while active and consumes the
  cancellation frame. This Editor-only input helper adds no per-monster update or component.
- Brush/spawn regression checks execute the production brush, both spawn paths and ECS world
  against deterministic camera, input, UI, scene and animal ports. Actual mouse routing,
  ray/ground intersection and button layout still require in-Editor verification.
- Spawn now delegates to runtime `MonsterSpawner.TrySpawnNearPlayer` and registers with
  `MonsterWorld`. Manual PlayableGraphs use saved idle/move/attack/death clips without an
  Animator Controller; simulation pause/load barriers also pause animation. Runtime owns
  graphs, roots and selection cleanup, replacing the old Editor-only idle graph registry.
- Spawned monster views register a plain `MonsterInstance`, not a per-actor MonoBehaviour.
- All sixteen source prefabs contain a solid vertical CapsuleCollider and a gravity-free
  kinematic Rigidbody on the existing Animal layer. Capsule geometry is stored in root
  space using each model's renderer bounds; the narrower horizontal extent keeps wings
  from becoming wide walls. Center Y is half the capsule height so the bottom touches
  root-local ground (y=0), matching the ground position assigned at spawn. Runtime only
  binds these authored components and preserves their geometry/settings. Missing components
  fail explicitly instead of being added silently.
  ECS pose output writes the body position/rotation instead of moving static colliders.
  Death and disposal disable the capsule and body contacts immediately. No per-monster
  script, dynamic-physics movement or global per-tick SyncTransforms is added.
  Navigation still owns monster movement. A pooled 2-cell spatial index tracks the actual
  capsule centers, radii, layers and vertical extents. Swept body movement blocks tunneling
  against other living monsters and the player's capsule, slides around head-on blockers,
  and separates initial overlaps. Rotation cannot turn an offset capsule into its neighbor.
  The actual combined PlayerController sweep queries the same ECS bodies alongside PhysX,
  pipes and conveyors, including height/mask filtering and movement out of initial overlaps.
  Cloaking changes targeting only; disabled/dead/disposed bodies stop blocking immediately.
  Short-range player attacks stop at physical contact instead of requiring penetration.
  This is planar upright-capsule collision, not arbitrary PhysX obstacle response or force simulation.
  Geometry/lifetime and real ECS movement/player-sweep code run against deterministic scene
  ports in tests; asset checks verify root ownership, unique IDs, geometry, layers and body
  configuration for every color, including bottom-to-ground alignment. Actual Unity
  FixedUpdate/input ordering, contact callbacks and rendered behavior require in-game verification.
  Player/Animal collision-layer compatibility is checked against project settings.
  Mouse hover uses visible renderer bounds and the existing outline pass; right-click pins
  the portrait/name and HP gauge in ObjectInfoPanel; other stats are not displayed.
  HP starts at the resolved maximum and supports clamped updates.
  Gauge text is rebuilt only when HP changes. Selection compares distance with animals,
  portable objects and map targets. Inactive/destroyed/disposed views release the panel.
  Focus raycasts remain separate from simulation and do not drive target acquisition.

## Combat

- Right-click a living monster to select it, then use the existing knife interaction button
  or Space. The player approaches and performs one swing per request, with the existing
  knife equip visual and pick animation. Animal attacks use the same controller in
  `PlayerController.Knife.cs`, retaining corpse harvesting and animal attack notification.
  Damage is 20 at the animation midpoint; impact revalidates the living target, current
  range (the existing player harvest range, minimum 0.75) and an unobstructed terrain line.
  Monster range uses its authoritative capsule surface, including authored root offsets.
  Repeat clicks cannot stack hits or skip recovery. Death, carrying, mounting, input locks,
  loading, cancellation and animation-start timeouts cancel queued hits.
  A lethal hit disables monster collision immediately and enters the existing ECS death
  animation on its next tick. No new actor component, loot system or monster resurrection is added.
  `KnifeChecks` executes the production shared controller and monster HP API against input/
  animation ports, covering hit timing, misses, walls, cancellations, death, animal regression
  and allocation-free steady ticks. Source checks verify actual HUD/Space/equip integration;
  mouse routing, animation appearance and button layout still need in-game verification.
- One fixed-tick `MonsterWorld` manages every actor. Target scans and A* searches have
  shared budgets and rotate their first actor to avoid starvation. Actor creation allocates
  its path once; the normal tick has no managed list/LINQ allocation.
  Simulation position/heading live in actor data; scene transforms are output only.
- Swarms reserve non-overlapping attack positions on the player's ring or installation
  footprint perimeter. Nearby body/slot spatial buckets are pooled; approach searches share
  a four-actor-per-tick budget and cache stable goals. Far-side approaches circle outside
  the occupied inner ring, including multi-cell building bounds, instead of crossing it.
  Moving targets, death, disposal and target changes release obsolete reservations.
  Two seconds without approach progress retries another position; another stall or no free
  position temporarily excludes that target for four seconds. A failed path skips only its
  attack-position cell and tries other sides (up to eight failed cells), rather than rejecting
  the entire building after the nearest position fails. An unreachable orbit waypoint first
  falls back to routing to the actual attack position. Narrow spaces also test a closer band
  if the preferred outer attack ring has no free position. Other valid
  targets retain player/structure priority rules; up to four failed identities are remembered
  per actor. Exclusion, priority-zero blocker rules, attack commitment and collision remain.
  Attack boundaries delayed by the search budget wait for the next tick instead of starting
  another cycle and repeatedly missing their retarget opportunity.
  `SwarmChecks` runs the production world for same-side player encirclement, moving targets,
  multi-cell structures, crowded-target overflow, freed positions, inaccessible targets,
  body-blocked corridors, collision separation and allocation-free settled attacks.
  Proximity scans enumerate foreground owners and overlapping pipes/belts/train identities
  independently, with a reused candidate list. Detection bounds use the actor position and
  footprint edges, including the exact range boundary; leash bounds remain centered on home.
  Discovery regressions cover a sealed nearest-side pocket with a reachable opposite edge,
  a pipe hidden by a priority-zero I/O owner, exact-range discovery, opposite-side home bounds,
  and a narrow attack space with only an inner position available.
- Moving actors turn once per tick toward actual displacement, including obstacle detours
  and returning home. Only stationary actors in attack range or an attack cycle face targets;
  waiting for a path preserves heading. Authored turn speed is retained.
  Facing normalizes the actual constrained displacement, so the
  authored 0.5-speed Bat/Slime actors still turn (regression covers 30/60/120/240 Hz).
  Commit even sub-millimeter arrival steps so rounding cannot leave short-range attackers
  permanently outside attack range. Only an actual target transition clears a path;
  targetless/returning actors retain obstacle detours instead of clearing them every tick.
  The home-arrival distance does not stop player pursuit outside authored attack range.
- A living player within detection range has priority; pursuit can continue to the leash
  boundary. Without a reachable player, attack the nearest eligible installation,
  including traversable conveyors and pipes.
  Failed player paths temporarily select a building, allowing wall destruction and pursuit
  to resume. Paths reuse `AnimalGridPathfinder`, including diagonal corner clearance.
- GameManager's Cloaking Mode (default off) excludes only the player from monster targets.
  Acquisition and existing-target validation share the same predicate; enabling it cancels
  chase and pending hits on the next simulation tick, even during a committed attack.
  Turning it off restores normal acquisition. Buildings remain attackable; player rendering
  and non-monster damage are unchanged. EditorTool's debug checkbox sends `debug cloakingMode`
  and synchronizes via the `cloakingMode` status/acknowledgement token.
- Monsters treat tree cells as traversable so dense forests cannot trap them. Animals
  keep their original tree-blocking policy. Loaded ECS/scene trees and unloaded
  generated trees agree; walls, closed doors, water, blocking non-tree resources and
  registered static obstacles remain blocked. Movement, A*, smoothing, structure
  approach and attack-line checks share the monster policy through a world-cached
  predicate, not per-tick closures. Both navigation caches invalidate together.
  This is a tree-cell pass-through rule, not trunk-level physical avoidance.
- Damage occurs once at 35% of the attack cycle, with integer simulation-tick cooldowns.
  Range and attack line are checked again at impact. Losing/replacing a target cancels the
  pending hit without resetting cooldown. Buildings use the nearest footprint edge.
- Wind-up and recovery retain the same target for damage, facing and animation; target
  scans may switch at the next attack boundary, not mid-swing. A player behind an obstacle
  cannot repeatedly steal a building attack; opening a direct attack line restores player
  priority. Losing/destroying a target cancels its attack pose immediately while preserving
  cooldown, so movement cannot run under the previous target's attack animation.
- Player and installation HP default to 100, configured on Player/ItemDefinition respectively.
  Animal and monster overhead HP bars share WorldHealthBarView geometry and visibility:
  damaged actors remain visible, healthy attacks show for one unscaled second, restoring
  full health gets the same grace period, and death hides immediately. Monster bars are
  created lazily on damage/attack, use model/capsule height, do not intercept selection,
  and are parented outside rotated/scaled monster models. A single MonsterWorldView
  LateUpdate after the camera updates all bars even while simulation is paused; no
  per-monster behaviour is added. Disposal/world reset releases all bar hierarchies.
  Production fixture checks cover shared Animal behavior, fill/color/dimensions, height,
  body motion, camera changes while paused, healing/attack expiry, inactivity/death/disposal
  and zero per-frame GC after creation. Actual Canvas rendering needs Editor play validation.
  Player HP zero locks gameplay input, cancels actions and detaches mounts. The HUD shows
  HP and a death-only manual revive button; there is no automatic respawn or living heal.
- Installation damage resolves canonical placement sequence + item ID, not an overlapping
  I/O area's coordinate owner. Zero HP clears occupied bindings, calls persistent removal
  for all data worlds, and releases any scene instance. Destruction does not refund contents.
- Save v72 appends player/installation health, including nested mounted installations.
  Older saves start with full HP. Dead players remain dead after loading; normal machine
  captures preserve canonical health even if a runtime record submits a stale clone.
- ItemData exposes Maximum HP and Exclude From Monster Targets for single/multi editing
  and JSON import/export. Exclusion affects target acquisition, not the health concept.
  Trees and rocks have independent saved HP, but are not monster combat goals;
  harvesting quantities/work gauges are unchanged. No invulnerability, armor or resistance.
- ItemData's Monster Attack Priority defaults to 1 and accepts 0/1/2/3 (including JSON import).
  Priority 3 is a persistent raid destination. The nearest priority 2 within five world
  units of its footprint is always attacked first, even off the direct route and even
  when 3 is closer. The encountered structure is finished before resuming the same 3;
  a newly closer 3 does not replace that destination. Without an available 3, pursue the
  nearest 2 inside detection range. After all detected 3 targets are destroyed, clean up
  the remaining 2 targets, then the nearest 1 targets, continuing scans from the actor's
  position after each destruction. Only an empty eligible search permits returning home.
  Temporarily rejected targets keep the actor from returning; a rejected 3 is not treated
  as destroyed for priority-one cleanup. Selection is allocation-free and scan-order independent.
  Priority 0 is never an unsolicited proximity goal, and 1 is not one while 3 remains.
  Either becomes a temporary target when it is the first blocking installation on a
  pursuit/return movement segment, even if A* could find a detour. Existing path-search
  budgets cover this check; it does not scan every installation. Water/resources stop the
  blocker query. Exclude From Monster Targets remains an absolute override, including
  obstacle attacks. Player priority, cloak policy and attack-cycle commitment are unchanged.
  Swarm checks cover both scan orders, near/far encounter boundaries, off-path 2,
  persistent destination resumption, chained encounters, multi-cell priority-one blockers
  with valid detours, ignored off-path 1 during pursuit, excluded blockers and failed approaches.
  Cleanup checks cover both scan orders, destruction by the raider or another attacker,
  multiple distant 2 targets followed by 1, no premature return, final homeward travel,
  absolute exclusion, priority-zero preservation and temporary priority-three rejection.
  Single/multi editing and JSON include priority; legacy JSON omission preserves existing
  authored values. No save-format change or mass asset rewrite is required.
  Attack priority and absolute exclusion are shown only for InstallationObject items;
  mixed multi-selections hide both controls. Resource HP authoring remains independent.
- Damaged installations alone enter the overhead-bar index; pristine objects and InfoPanel
  receive no new HP display. One presentation owner pools the world-space bars, culling
  unloaded/offscreen objects. No per-installation health MonoBehaviour is added.
- Zero HP removes functional simulation/topology and internal items, then creates separate
  passable visual wreckage. Use the map-edit pack action to remove wreckage without refund.
  Committing a new installation also removes every wreck whose occupied cells overlap
  the new footprint, including partial overlaps of large wrecks and multiple wrecks under
  one placement. Adjacent debris is preserved. Empty legacy footprints use their anchor.
  Shared canonical storage covers ECS and live installation registration; previews, failed
  null registrations, routine captures and moving-vehicle saves do not run this cleanup.
  Wreckage revision invalidates the presentation once per cleanup batch, and removed
  records are absent from later saves. No save-format change or item refund is involved.
  Wreckage fixture checks exercise partial/multiple overlap, neighbor preservation,
  anchor fallback, invalid input, idempotence and zero temporary allocation.
  Wreckage saves its appearance/pose, not inventory, fluids or working state, and cannot
  be repaired/reinstalled. Actual repair is deferred.
- Recovery/reinstallation and map editing retain individual HP. Bag/hand moves, ground/box
  stacks, FreightCar/Handcart cargo, robot arms and native/legacy belt payloads carry it.
  Healthy units use zero metadata as the pristine sentinel; damaged and pristine units can
  share a stack. Upgrading creates a full-health replacement, retaining cargo HP separately.
- Save v73 adds wreckage, resource HP and individual portable HP across those routes.
  v72 and older entries skip the new fields instead of misreading the stream.
- Spawned monsters themselves are not saved. World-state clearing, script reload and Play
  Mode exit release them. No pollution goals, monster drops, player weapon damage against
  monsters, flying navigation or player death pose are added here.

## Verification

```powershell
./Tools/MonsterDataHarness/Run.ps1
./Tools/MonsterDataHarness/Run.ps1 -CompileUnity
dotnet run --project Tools/SaveLoadOptimizationHarness --configuration Release -p:UseAppHost=false -- --combat-only
```

The headless harness compiles the actual stats/definition sources using a small Unity fixture.
It checks every override mask, numeric validity, drop inheritance, stable IDs, missing
animations, unmanaged records, bounded spawn search, health/death/manual revival, exact
60-tick attack boundaries, hit cancellation and zero allocation in numeric combat/lookup.
Tiered compilation is disabled for deterministic allocation measurements (JIT/OSR setup
otherwise contributes a one-off allocation in the test loop).
Asset checks verify the initial prefab roots and FBX clip IDs against their source files,
along with model import size, matching skeleton size, renderer bounds and unit transform scales.
Bat and Slime portrait checks cover color-to-Sprite GUID bindings, square RGBA PNGs and alpha-safe UI import settings.
The runtime fixture compiles production MonsterInstance (including its collision proxy), MonsterWorld,
terrain damage/walkability and the existing A*/integer
navigation code. It covers player pursuit/death/revival, range escapes, multi-cell building
damage/destruction, blocker destruction, simultaneous attacks, overlapped I/O identities,
target replacement, shared-budget fairness, pause/load barriers and leash return. Forest
checks cover enclosed escape, wall detours, corner clearance, approach/attack consistency,
loaded/generated trees, separate cache policies, invalidation and allocation-free lookups. Scene
objects, animation playback, player input and the state-store index are isolated test doubles.
Collision lifecycle checks use the real instance constructor, pose forwarding, health setter and disposal;
there is no replacement MonsterInstance implementation in the fixture.
Source contracts cover spawn lifecycle, manual revival, canonical teardown, distance ordering and InfoPanel wiring.
`-CompileUnity` builds against the locally generated Unity references without opening Unity.
Its source globs include new files even if the Editor has not imported them yet, so this build alone
does not validate the Editor's source inventory. For an import-related missing-type error, also compile
the current Bee response files with Unity's Roslyn compiler and redirect outputs outside Library.
The save harness tests real compiled binary serializers with current/legacy player and
installation payloads, recursive mounted health, wreckage, resource/cargo/arm/native-belt
HP, sparse floor/box metadata reindexing and full-stream alignment.

`Tools > ProjectF > Tests > Monster Data` is an optional in-Editor harness for JSON round
trips, prefab/clip/icon references, list portrait selection (including missing/edited icons),
rejected duplicate IDs and safe authoring-field copying. Collider-free focus tests cover
nearest hits, distance clipping, inactive/hidden/moving/destroyed views, disposal and stat/icon identity.
It changes temporary clones only and never saves assets. It has not been run headlessly.
Rendering, animation poses and actual Unity import require separate Editor verification.
The broader save optimization harness currently fails an unrelated world-load presentation
source contract; the animal feeding harness also has stale PortableObject.WorldPosition stubs.
Neither is hidden or treated as a passing whole-game regression suite.
