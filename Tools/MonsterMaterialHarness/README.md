# Monster Material Checks

Checks the saved Level 1 Monster Pack material bindings to `Custom/ToonCharacter`.
All 20 materials and 16 prefab references must resolve. Opaque variants use normal
depth writes; the five ghost materials use straight-alpha blending, the transparent
queue, and disabled shadow/depth passes. The shared shader is not modified.

```powershell
./Tools/MonsterMaterialHarness/Run.ps1
./Tools/MonsterMaterialHarness/Run.ps1 -SourceBackup 'C:/path/to/backup'
```

The optional source backup comparison verifies original texture, tiling, offset,
tint, and material GUID preservation. This harness is file-based; it does not run
Unity, compile GPU shaders, or confirm the visual result.
