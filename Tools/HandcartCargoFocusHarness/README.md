# Handcart cargo focus harness

```powershell
& ./Tools/HandcartCargoFocusHarness/Run.ps1
```

Extracts the production handcart stack-layout and focus-stack methods. It verifies
that every visible item in the selected physical stack shares one focus group,
overflow and mixed-item stacks remain separate, and cargo mutations publish a new
focus generation so an existing outline can release its previous members safely.

The source checks also require add, pickup, and visual restoration paths to rebuild
the focus layout, and require pickup preview to attach the chosen stack to its top
portable object. Unity rendering itself is not launched by this harness.
