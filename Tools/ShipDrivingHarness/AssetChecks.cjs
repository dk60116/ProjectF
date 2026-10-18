const fs = require('node:fs');
const path = require('node:path');
const YAML = require('../PlayerAnimationHarness/node_modules/yaml');
const root = process.argv[2];
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
function documents(file) {
  const source = read(file);
  const markers = [...source.matchAll(/^--- !u!\d+ &(-?\d+)(?: stripped)?\r?\n/gm)];
  return new Map(markers.map((marker, i) => {
    const doc = YAML.parseDocument(source.slice(marker.index + marker[0].length,
      i + 1 < markers.length ? markers[i + 1].index : undefined), { intAsBigInt: true });
    if (doc.errors.length) throw new Error(doc.errors.join('\n'));
    return [BigInt(marker[1]), doc.toJS()];
  }));
}
function check(condition, message) { if (!condition) throw new Error(message); }
const itemPath = 'FactorioProject/Assets/Data/Items/Item_135_Raft.asset';
const item = documents(itemPath).get(11400000n).MonoBehaviour;
const prefab = documents('FactorioProject/Assets/MapObject/Sheep/Raft/Raft.prefab');
const raft = prefab.get(item.mapObject.fileID).MonoBehaviour;
const itemGuid = read(itemPath + '.meta').match(/^guid: (\w+)/m)[1];
check(raft.itemDefinition.guid === itemGuid, 'Raft resolves its canonical ItemDefinition');
check(item.interactionButtonList.length === 2
  && item.interactionButtonList[0].guid === '4fade2dbe45025648acb64b3e0bfe229'
  && item.interactionButtonList[1].guid === '7b654ce797ed8754f8f5bfc320ab51fb', 'Raft exposes Boarding/Alighting icons in HUD order');
check((raft.mapFilter & 2n) !== 0n, 'Raft permits water placement');
check(raft.playerPoint.length > 0 && raft.playerPoint.every(ref => prefab.get(ref.fileID)?.Transform), 'Boarding points are authored and bound');
const hull = prefab.get(raft.drivingCollider.fileID)?.BoxCollider;
check(hull && hull.m_Enabled === 1n && hull.m_IsTrigger === 0n && hull.m_GameObject.fileID === raft.m_GameObject.fileID,
  'Ship driving uses a real collider authored on the root');
check(hull.m_Size.x >= 1.1 && hull.m_Size.z >= 1.3 && hull.m_Size.y > 0, 'Collider fits existing raft mesh bounds');
const rootObject = prefab.get(raft.m_GameObject.fileID).GameObject;
check(rootObject.m_Component.some(ref => ref.component.fileID === raft.drivingCollider.fileID), 'Root collider is part of prefab component list');
const controller = read('FactorioProject/Assets/Scripts/Character/Player/PlayerController.cs');
check(controller.includes('!(vehicle is Train) && !(vehicle is Ship)') && controller.includes('TryResolveClearVehicleDismountPosition('), 'Ship dismount routes through safe land resolution');
check(controller.includes('interactionPointSnapVehicle.HandleMountedInput(moveDirection, mountedMoveSpeed, Time.deltaTime, player);')
  && controller.includes('ApplyInteractionPointSnap();'), 'Existing controller input drives and follows the mounted vehicle');
console.log('PASS Raft prefab, hull, boarding icons and controller wiring contracts');
