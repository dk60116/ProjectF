# ProjectF native extension

The first porting slice is implemented as a Godot 4.7 GDExtension written in C++.
It provides five registered scene classes:

- `ProjectFPlayer`: camera-relative top-down movement on a `CharacterBody3D`.
- `VirtualJoystick`: Unity-style floating touch/mouse joystick without per-frame allocation.
- `PlayerSpawner`: creates exactly one player from an assigned `PackedScene`.
- `FactorySimulationBridge`: owns the Godot-independent C++ simulation and fixed tick.
- `FactoryRenderBridge`: extracts a render snapshot and submits render-chunk MultiMeshes
  directly through `RenderingServer`.

Factory machines are compact native records addressed by generational `EntityId` handles.
They are not `Node3D`, `MeshInstance3D`, or `PhysicsBody3D` objects. The current placeholder
prototype displays 10,000 machines while keeping the SceneTree node count constant.

## Dependency

Place a `godot-cpp` checkout containing the Godot 4.7 API at:

```text
Godot/native/godot-cpp
```

The dependency is intentionally not vendored into this repository. `SConstruct`
selects `api_version=4.7`; its generated and compiled files are ignored by
`Godot/.gitignore`. The verified dependency revision is recorded in
`godot-cpp.commit`.

From `Godot/native`, a reproducible local checkout can be prepared with:

```powershell
git clone https://github.com/godotengine/godot-cpp.git godot-cpp
git -C godot-cpp checkout 05057de73de4b99f114d36c40d84ca46926c0e25
python -m pip install --user scons
```

## Build

From `Godot/native`, build the extension with the SCons environment supported by
the selected `godot-cpp` checkout. For a Windows editor/debug build:

```powershell
scons platform=windows target=template_debug extension tests -j4
```

For a Windows release build:

```powershell
scons platform=windows target=template_release extension benchmark -j4
```

The SConstruct output names match `projectf.gdextension`. After the matching
library exists under `Godot/native/bin`, opening the Godot project registers the
all four native scene types.

`scenes/main.tscn` is the configured main scene. It spawns `scenes/player.tscn`
at the world origin. Movement accepts W/A/S/D, arrow keys, and the floating touch/mouse
joystick, then converts the combined vector relative to the fixed isometric camera.
The HUD also provides zoom buttons; mouse wheel and two-finger pinch use the same
smoothed orthographic zoom target (`3..10`) as the Unity camera.

## Automated check

After building the debug library, run the isolated spawn and movement harness:

```powershell
godot --headless --path .. --script res://tests/player_spawn_test.gd
```

Success prints `PLAYER_INPUT_UI_ZOOM_TEST_OK` and exits with code `0`.

The pure C++ tests and CPU benchmark do not link godot-cpp:

```powershell
.\bin\factory_core_tests.windows.template_debug.x86_64.exe
.\bin\machine_benchmark.windows.template_release.x86_64.exe
```

The bridge smoke test validates native storage, fixed ticks, sleep/wake behavior,
render extraction, and that 10K machines do not create 10K SceneTree nodes:

```powershell
godot --headless --path .. --script res://tests/factory_bridge_test.gd
```

Run the D3D12 rendering baseline without `--headless`, because headless timing is not a
representative GPU measurement:

```powershell
godot --path .. --script res://tests/render_benchmark.gd
```
