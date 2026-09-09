# Interaction focus regression checks

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/InteractionFocusHarness/Run.ps1
```

Requires .NET 9; does not launch Unity. Extracts production candidate orchestration,
single-target arbitration, interaction-button caching, and logical footprint distance.
Tests reordered candidates, equal-distance stability, immediate nearer-target switches,
late watering candidates, standing input areas, multi-cell markers, inactive targets,
and consistent button/visual selection. It also verifies that every filter panel is
routed through the clicked map-object selection without player-distance discovery or
mouse-hover activation.

Candidate discovery, Unity types, rendering, and marker application are test doubles.
In-game interaction, mouse selection, animations, and InfoPanel rendering need manual verification.
