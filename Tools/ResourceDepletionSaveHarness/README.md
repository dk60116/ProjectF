# Resource depletion persistence harness

`Run.ps1` compiles the production depletion transition and persistence entry point against small engine-free data doubles. It verifies that the zero-count resource state is recorded at its owning coordinate before deactivation can detach it from the block. Unity is not launched and save files are not modified.

The harness reads the ECS `ResourceInstance` implementation and its owning resource world's terrain reference; `Resource` is the prefab authoring type.
