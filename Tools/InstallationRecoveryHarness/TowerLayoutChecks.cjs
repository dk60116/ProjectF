const fs = require('node:fs');
const path = require('node:path');
const YAML = require('../PlayerAnimationHarness/node_modules/yaml');
const source = fs.readFileSync(path.join(process.argv[2], 'FactorioProject/Assets/MapObject/Battle Object/Tower/Tower.prefab'), 'utf8');
const markers = [...source.matchAll(/^--- !u!\d+ &(-?\d+)(?: stripped)?\r?\n/gm)];
let checked = false;
for (let i = 0; i < markers.length; i++) {
  const marker = markers[i];
  const doc = YAML.parseDocument(source.slice(marker.index + marker[0].length,
    i + 1 < markers.length ? markers[i + 1].index : undefined), { intAsBigInt: true });
  if (doc.errors.length) throw new Error(doc.errors.join('\n'));
  const value = doc.toJS().MonoBehaviour;
  if (!value || value.m_Script?.guid !== 'e791f4625b0074e4b99c94e84a424fbe') continue;
  const placements = value.rectGridPlacements;
  const itemFile = path.join(process.argv[2], 'FactorioProject/Assets/Data/Items/Item_134_Tower.asset');
  const itemGuid = fs.readFileSync(itemFile + '.meta', 'utf8').match(/^guid: (\w+)/m)[1];
  if (value.itemDefinition?.guid !== itemGuid) throw new Error('Tower must bind its ItemDefinition for monster combat');
  const objects = placements.filter(cell => cell.blockType === 1n);
  const inputs = placements.filter(cell => cell.blockType === 3n);
  if (objects.length !== 1 || inputs.length !== 1) throw new Error('Tower needs one physical Object cell and one ammunition input');
  if (objects[0].x === inputs[0].x && objects[0].y === inputs[0].y) throw new Error('Tower body and ammunition input must not overlap');
  for (const cell of placements) {
    if (cell.x < 0n || cell.y < 0n || cell.x >= value.rectGridWidth || cell.y >= value.rectGridHeight)
      throw new Error('Tower placement outside configured grid');
  }
  checked = true;
}
if (!checked) throw new Error('Missing Tower component');
console.log('Tower prefab YAML, physical Object cell and separate ammunition input verified.');
