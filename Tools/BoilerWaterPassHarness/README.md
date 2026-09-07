# Boiler water supply regression checks

Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/BoilerWaterPassHarness/Run.ps1`.

Requires .NET 9 and the Unity CoreModule reference installed at the path in Run.ps1.
Does not launch Unity. Extracts production pump network search, storage distribution,
boiler water-port routing and full-storage heating checks. Simulates two and three
connected boilers in all four rotations while the first consumes 5 L/s and the pump
supplies 15 L/s. Verifies downstream full-storage heating, upstream water availability,
water conservation, connector direction and separation from the steam outlet.

Registry lookup, grid placement, storage operations and energy are managed doubles;
live scene connectivity and complete steam/temperature simulation still require an
in-game check.
