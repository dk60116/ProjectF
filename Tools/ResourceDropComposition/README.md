# Resource Drop Composition

`ResourceDefinition.DropItems` contains entries, and every entry contains an `Items` list.

- `Add Entry` creates another growth-condition group.
- `Add Item` adds another item to the selected entry.
- Growth limits belong to the entry.
- Amount and probability belong to each item.

## Ore and oil

- `Composition Weight (%)` is normalized across all valid items in growth-matched entries.
- One resource unit resolves to exactly one entry.
- `Stone 90` and `Quartz 10` allocate the complete deposit before mining starts. A deposit with 1,000 units contains exactly 900 Stone and 100 Quartz.
- The sequence is derived from the resource coordinate, definition, initial reserve, and depletion ordinal.
- Manual harvesting and mining machines read the same next entry.
- Save and load preserve the sequence because the initial reserve and remaining reserve are saved.

The sequence uses deterministic balanced distribution across the complete deposit. It does not roll a new random value when a unit is mined. The resource seed changes the starting phase, while weighted items stay evenly spaced instead of forming long clusters. For a 90/10 deposit, Quartz appears about once per ten units and the final whole-deposit amount remains exact. Small deposits use the nearest representable ratio.

## Trees

Tree items keep their existing independent drop chance behavior because several items in growth-matched entries may drop from one harvest.

## Validation

Run `Tools/ProjectF/Validation/Resource Drop Composition` in Unity. The harness creates one entry containing Stone and Quartz, verifies exact 9/1, 90/10, and 900/100 whole-deposit allocations, checks that Quartz stays distributed without consecutive clusters or long Stone gaps, and confirms that repeated resolution returns the same sequence.
