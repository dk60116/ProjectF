# Save item ID remapping harness

Run `./Tools/SaveItemIdRemapHarness/Run.ps1` from the repository root with .NET 9 installed. This compiles the complete production `SaveGameItemIdRemapper.cs` against engine-free data doubles. It does not start Unity or modify save files.

The regression models pipe item 51 passing from a production machine through box storage to another machine after loading a save. It checks box filters (including allow-all), pending output, input bindings, stored items, arm-held items, inventory and belt runs. It also covers repeated loads, renamed numeric IDs, an old catalog that explicitly identifies rail 51, an ambiguous catalog and missing catalog data.

`-Baseline` runs the same checks against the remapper from Git HEAD. Before the fix, it fails because the unconditional legacy rail alias overwrites a valid pipe catalog mapping.

The fix prevents further corruption; it deliberately does not reinterpret existing rail items or filter bits as pipes. Those identities cannot be recovered unambiguously from an already-corrupted save.
