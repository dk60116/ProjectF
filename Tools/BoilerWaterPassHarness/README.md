# Boiler water supply regression checks

Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/BoilerWaterPassHarness/Run.ps1`.

Requires .NET 9 and the Unity CoreModule reference installed at the path in Run.ps1.
Does not launch Unity. Extracts production shared fluid-output distribution,
boiler water-port routing and full-storage heating checks. Simulates two and three
connected boilers in all four rotations while the first consumes 5 L/s and the pump
supplies 15 L/s. Verifies downstream full-storage heating, upstream water availability,
water conservation and separation from the steam outlet.

Registry lookup, grid placement, storage operations and energy are managed doubles;
live scene connectivity and complete steam/temperature simulation still require an
in-game check.

MapObject Profiler의 `PumpWaterNetwork` 카운터는 공유 수계 수, 캐시 좌표 수,
토폴로지 구축/재사용 횟수와 마지막 구축의 탐색 노드·저장소 수를 표시한다.
