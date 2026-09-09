# Belt Split harness

Run from the repository root: `dotnet run --project Tools/BeltSplitHarness`.

Links the production component graph, Block adapter and conveyor topology resolver.
Checks weak components of directed transfers, cycles, deterministic representatives,
removal/rebuild, world reset and a 100,000-vertex chain. Regression scenarios cover:

- Upper/lower paths crossing in one Block remain separate.
- Connecting those paths externally merges them; removing that link splits them.
- Both splitter outputs are included, independent of current arbitration.
- Side entry, corner entry and missing bridge exit.
- Grouping without item storage; normal simulation still validates its storage.
- Upper/lower debug segments retain their respective heights.

Engine geometry/lookup methods are stubbed. Actual scene placement, material
rendering and frame time require Unity verification.

In play mode, enable **GameManager > Show Belt Split** or **EditorTool > Show Belt
Split**. Connected transport paths share a translucent color. Install/remove the
middle of a chain, attach both splitter outputs, and cross a 2F bridge over a ground
belt: the crossing alone must not merge the two colors. Empty/full/stopped belts
retain their group. Check the toggle in both directions and confirm item movement.

Grouping uses (Block, lane) vertices and actual possible slot transfers. A one-way
join still merges groups. Robot arms, mere adjacency and shared Block/installation
ownership do not create connections. Both potential splitter outputs are included;
temporary occupancy and filter/arbitration choices do not split its topology.
Groups cover loaded runtime cells; coordinate+lane keys are not persistent save IDs.
This is transport connectivity metadata only. Independent paths can still share
Block state, which must be separated before actually running them on workers.
