# Pipe placement harness

`Run.ps1` also extracts the fixed-port owner collector, same-cell direction lookup,
adjacent connector lookup, and actual pipe-only neighbor lookup. Pipes over ECS
Double input/output/energy cells must retain their hidden facility port and form
a corner or tee with a reciprocal neighboring runtime pipe in all four rotations.
Legacy grid/area modules, scene owners, saved owners, overlapping previews,
deduplication, ignored previews, and pump/boiler/steam-generator directions are
covered. Footprint geometry, persistence indices and connector compatibility for
individual snapshots are fixture boundaries; Unity rendering is not executed.
`Run.ps1 -BeforeConnectorFix` substitutes the HEAD fixed-port lookups and reproduces
the hidden ECS DoubleInput port failure.

`AreaRun.ps1` extracts the complete per-block placement decision, shared runtime
grid-type query, and actual production-world coordinate lookup. It covers all
grid types in four rotations across legacy, production, mining and forestry
registries. Double input/output/energy and pipe-only cells accept pipes; item-only
areas, machine bodies, duplicates, incompatible fluid endpoints and water remain
blocked. Scene lookup and unrelated special-case placements are fixtures.
`AreaRun.ps1 -BeforeFix` substitutes the HEAD runtime query to reproduce the ECS
lookup omission. Unity placement transactions and rendering are not executed.

```powershell
./Tools/PipePlacementHarness/AreaRun.ps1
```

열차 회귀 검사는 실제 칸 단위 설치 판정, 설치물 격자 중첩 판정과 저장 레일 격자 판정을 함께 실행해.
증기·디젤·핸드카·두 종류 화물차 프리팹의 필터를 읽고, 레일 플래그가 빠져도 열차의 레일 중첩은 허용되는지 확인해.
일반 건물·물 지형 차단과 일반 설치물의 레일 필터는 유지해. 프리팹 Center 값을 특정 칸으로 강제하던 검사는 제거했어.
실제 Unity 격자 메시·색상·화면은 검증하지 않아.

Extracts the production duplicate-pipe placement rule from
`InstallationPlacementController`. It verifies that a new pipe cannot occupy a
coordinate already owned by another pipe, while an explicitly ignored preview,
non-pipe placement, and empty coordinate remain unaffected.

Also extracts the production fluid preference, network traversal, placement
compatibility, and port-priority rules. A world-lookup fixture checks straight continuation against three foreign branches,
parallel lines in four rotations, blueprint and committed normalization, endpoint
ownership, tie-breaking, and manual/underground ports. Tunnel tests use untyped
prefabs and a remote source: runtime records, saved pairs, and scene previews must
carry that identity across both endpoints in all four rotations. A straight
extension is accepted while a corner into a foreign-fluid row is rejected.
Pump cases cover both pipe placement beside existing pumps and new pump placement
between two tanks or pipes. Empty/same-fluid endpoints are accepted, foreign-fluid
endpoints are rejected, and both adjacent and overlapping endpoints are tested in
all four rotations. Pump geometry and world lookup are fixtures; the compatibility
and network traversal methods are extracted from production.
Variant cases extract actual prefab/rotation selection and forced alignment. In
four rotations, restoring a straight beside a perpendicular output preserves both
old ports and extends to a tee; a fresh placement may still choose a corner.
Neighbour lookup, acceptance and candidate fluid constraints use extracted
production code; world state is a fixture. Preview/commit comparisons cover
known anchor identities, previously untyped anchors, and a parallel foreign-fluid
blueprint. Both fluid selection and the resulting connection mask must match.
Shared data-only prototypes are tested in every installed/candidate rotation for
straight, corner and tee shapes. Candidate ports must follow the supplied rotation
instead of the runtime record's previous ports. An oil-output corner beside a
water branch additionally exercises actual network compatibility under this alias.
Mixed-fluid baselines remain repairable and tees can still shrink after branch
removal. Actual packed undo and history rollback dispatch preserve shape and
rotation together; runtime activation/persistence are fixture boundaries.
Unity rendering and a full placement transaction are not executed.

An additional regression separates the installed graph from blueprint geometry.
Untyped shared runtime prototypes form a straight line to a remote green source;
a temporary blueprint corner cuts that route while an orange preview branch is
nearby. The extracted installed lookup and `PipeRuntimeRecord` forwarding method
must still select green and keep the candidate straight in all four rotations.
The live graph traversal is a fixture; no fluid is injected into the pipe at the
tested junction. Disabling the installed lookup reproduces the wrong orange
selection. Blueprint caches are also checked for scope isolation.

Load regressions extract `TryResolvePipeLoadPlacement`, saved-mask rotation
resolution and `NormalizeLoadedLegacyPipeVariants`. Saved shapes must survive
temporarily conflicting source/neighbor information before activation and during
post-load normalization. Tests cover every shape/rotation, mask-only and
variant-only metadata, and the slot_01 drill output's persisted straight geometry
(variant 0, quarter-turns 3, mask 10) using the actual prefab's local port masks.
Metadata-free legacy pipes remain eligible for inference; explicit saved shapes
remain protected. World registration and normalization dispatch are fixtures;
Unity chunk loading/rendering is not executed.

Tank-corner regressions use the actual tank-neighbor direction lookup, tank fluid
inference, fixed-connector filtering and final compatible-adjacency alignment.
An untyped preview continuation beside a known tank must form a corner into it
instead of pointing into a different-fluid row. Cases cover four rotations,
closed/open side branches, stored/inferred tank identity, and committed alignment.
Only world/snapshot lookup is replaced; the tank direction is not injected as a
fixed-port fixture.

```powershell
./Tools/PipePlacementHarness/Run.ps1
```
