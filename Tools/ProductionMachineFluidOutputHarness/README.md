# Production machine fluid output regression

Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/ProductionMachineFluidOutputHarness/Run.ps1`.

The harness extracts the production completion, output-rate, recipe Count, pressure, managed tick,
sleep decision, persistence and target-filter methods. It also compiles the
shared fluid emitter, receiver selection, transport retention and Pump budget
methods, plus the actual binary state reader/writer and state clone/clear code.

Cases cover disconnected/full/foreign-fluid receivers, partial acceptance,
multiple tanks, fractional output, pipe distance loss, shared and edited Pump
limits, retries in the same tick, completed batches surviving target changes,
draining without further craft energy, pool reset and unchanged solid output.
Fluid batch volume is recipe Count (L/s) multiplied by the Complete/Use duration:
36 seconds at 1 L/s creates 36 L. A full 10 L receiver leaves 26 L pending, including
across save/reload. The actual base tick starts the next craft only after that remainder drains.
Authoritative binary Count 1 overrides a stale generated pair with Count 2.
Live pipe pressure is zero during intake, Working, after drain and when disabled.
Only the active fluid output supplies pressure during Outputting. The production
UI still reports configured L/s during Working so batch gauges keep their scale.
Input Count for a fluid-producing recipe is also a per-second amount: 2 L/s over
36 seconds requires 72 L. Solid recipes retain their per-craft fluid input amount.
The display checks extract the production fluid queries and ItemInfoDescription's
item/gauge rendering, cloning and sibling placement. They verify L/s output,
input batch denominators, conversion from left to right in two colors on one line,
output draining on that same line, live refresh, cached color-layer reuse,
and text rendering above newly created, cached and existing conversion layers,
and cleanup after changing or clearing recipes. Unity UI objects and item metadata
are fixtures; the actual screen layout and gauge animation are not rendered.
No separate bottom output gauge is created. Reception resumes only after the
previous output is completely drained; crafting and output waiting reject both
pipe-driven transfers and maker-driven pulls. The last delivery notifies upstream
senders that input capacity has reopened. ReceiverRun.ps1 also
executes the actual craft-start and ingredient validation methods, checking that
71.999 of 72 L cannot start, all fluid inputs must be full, energy failure preserves
them, and exact completion commits them once.
Version 68 round-trips partially delivered batches; version 67 initializes a
fresh batch because its format has no stored production output remainder.

Placement and connection discovery, receiver storage, energy progression and
recipe startup in the output harness are fixtures. ReceiverRun.ps1 executes the
actual startup and ingredient validation. Storage uses fixed units to avoid float accumulation.
The actual base tick and deterministic unit conversion are extracted. Unity is not launched.
SteamPipePressureHarness separately
executes the production pipe search and verifies directed producer outputs.
