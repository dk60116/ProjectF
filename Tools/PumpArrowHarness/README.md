# Pump arrow color regression

Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/PumpArrowHarness/Run.ps1`.

The harness compiles the production Pump visual lifecycle and color refresh methods.
It verifies fluid changes, hiding arrows in empty networks, restoring arrows when fluid returns, refresh throttling, placement changes,
visible return, re-enable, optional renderer handling and the prefab arrow reference.
Network results and Unity renderer/time APIs are fixtures; Unity is not launched.
