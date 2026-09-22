# Crude oil refinery transport regression

Run `./Tools/CrudeOilRefineryTransportHarness/Run.ps1` from the repository root.
The harness extracts the production pipe-retention, per-output connection lookup,
and refinery pressure methods. It tests separate output fluids, 0/50/100-pipe
routes, full storage, and agreement between delivered volume and the pressure
shown at a distant tank. It also extracts and executes the production continuous-refining tick. Each output
has an independent transport limit: disconnected/full/incompatible or partially
accepted byproducts are discarded without stopping other ports. Inputs and energy
remain required, and discarded quantities are not queued for later delivery.
All-blocked, partial-energy, missing-input and shared-receiver cases are included.
Steady half-rate input and whole-liter deliveries at 0.75 L/s verify that the
refinery builds a startup reserve, then keeps working at proportional throughput.
The tests also cover drawing from generic StoreFluid stock, using observed
delivery rate when pipe pressure is unavailable, dividing the configured 30 L
capacity between dedicated recipe input buffers, and scaling electricity demand
and outputs. A connected tank bypasses local startup buffering and can supply
the refinery continuously at its pressure limit. An adjacent producer also
supplies its rate when no pipe node is present.
Port configuration, storage acceptance and energy supply are fixtures; Unity
placement and live flow are not simulated.

Pump rate cases use the production transport ratio and rate limiter: a slower
source stays unchanged, a faster source is capped at 5 L/s, downstream pipe loss
is applied after the cap, and edited/zero pump settings change the transfer rate.
