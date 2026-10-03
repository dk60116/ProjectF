# Utility pole ECS regression harness

`powershell -ExecutionPolicy Bypass -File Tools/UtilityPoleEcsHarness/Run.ps1`

`powershell -ExecutionPolicy Bypass -File Tools/UtilityPoleEcsHarness/RunLifecycle.ps1`

Extracts the actual connection candidate search, three-pass topology builder,
union-find, terminal selection and consumer wire/supply selection methods.
Only Unity authoring, object lifetime, transforms and profiling are doubled.

`WireFixtures.txt` was captured from the component implementation before conversion.
It covers 256 seeded layouts, both pole radii, four rotations, replacement/ordinary
blueprint previews, insertion-order reversal, terminal capacity and absence of cycles.
Each layout also checks 64 multi-cell consumers against installed and preview supply areas.

The lifecycle harness runs the actual UtilityPoleWorld with native engine boundaries
doubled. It checks 10,000 data registrations, shared templates, loaded block rebinding,
save dispatch, swap removal and world disposal, plus actual numerical terminal/curve methods.

The fixture recorder is for deliberate connection rule changes. Do not regenerate
the fixtures simply to make a failed migration pass.

The harness runs without launching Unity. It does not verify shader appearance,
physics contacts, pointer interactions or the in-game save/load sequence.
