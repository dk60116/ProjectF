# World visual update harness

Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/WorldVisualUpdateHarness/Run.ps1` from the repository root. Requires .NET 9. Does not start Unity.

Compiles the actual InstallationVisualState and WorldVisualUpdateManager against managed scene/Animator/ParticleSystem doubles. Verifies visibility transitions, latest effect intent, toggle restoration, external Animator ownership, pool reuse, allocation-free steady-state dispatch, and registration/removal. Frustum math remains covered by ConveyorCameraCullingHarness. Does not measure native animation, GPU/particle costs, or actual FPS.

