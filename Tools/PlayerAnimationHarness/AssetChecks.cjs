const fs = require('node:fs');
const path = require('node:path');
const YAML = require('yaml');
const root = process.argv[2];
const read = name => fs.readFileSync(path.join(root, 'FactorioProject/Assets', name), 'utf8');
let checks = 0;
function check(value, message) {
  checks++;
  if (!value) throw new Error(message);
}
function documents(source) {
  const markers = [...source.matchAll(/^--- !u!(\d+) &(-?\d+)\r?\n/gm)];
  const result = new Map();
  for (let i = 0; i < markers.length; i++) {
    const marker = markers[i];
    const doc = YAML.parseDocument(source.slice(marker.index + marker[0].length,
      i + 1 < markers.length ? markers[i + 1].index : source.length), { intAsBigInt: true, uniqueKeys: true });
    check(doc.errors.length === 0, `Valid YAML with unique fields: ${marker[2]} ${doc.errors}`);
    check(!result.has(marker[2]), `Unique Unity object ID: ${marker[2]}`);
    result.set(marker[2], { doc, value: doc.toJS() });
  }
  return result;
}
const controller = documents(read('Animation/Character/Player.controller'));
function object(id, type) { return controller.get(String(id)).value[type]; }
const layers = object('9100000', 'AnimatorController').m_AnimatorLayers;
check(layers.length === 2 && layers[1].m_Name === 'UpperBody', 'Player has one independent upper-body layer');
check(Number(layers[1].m_BlendingMode) === 0 && Number(layers[1].m_DefaultWeight) === 0,
  'Upper-body override is inactive at rest');
const base = object(layers[0].m_StateMachine.fileID, 'AnimatorStateMachine');
const upper = object(layers[1].m_StateMachine.fileID, 'AnimatorStateMachine');
const names = machine => machine.m_ChildStates.map(child => object(child.m_State.fileID, 'AnimatorState').m_Name);
check(!names(base).includes('Pick') && !names(base).includes('Throw') && names(base).includes('Digging'),
  'Old duplicate full-body Pick/Throw are removed while digging remains on the base layer');
check(names(upper).join(',') === 'Empty,Pick,Throw', 'Upper layer owns only its empty and action states');
check(object(upper.m_DefaultState.fileID, 'AnimatorState').m_Name === 'Empty', 'Upper layer starts in an empty pose');
for (const child of upper.m_ChildStates) {
  const state = object(child.m_State.fileID, 'AnimatorState');
  check(Number(state.m_WriteDefaultValues) === 0, `Upper-body state does not reset unanimated bones: ${state.m_Name}`);
  for (const transition of state.m_Transitions) {
    const exit = object(transition.fileID, 'AnimatorStateTransition');
    check(exit.m_DstState.fileID === upper.m_DefaultState.fileID && exit.m_Conditions.length === 0
      && Number(exit.m_HasExitTime) === 1, `Upper-body action exits without depending on leg movement: ${state.m_Name}`);
  }
}
const upperTriggers = upper.m_AnyStateTransitions.map(ref => object(ref.fileID, 'AnimatorStateTransition').m_Conditions[0].m_ConditionEvent);
check(upperTriggers.includes('tPick') && upperTriggers.includes('tThrow'), 'Existing action triggers route to the upper layer');
for (const ref of base.m_AnyStateTransitions) {
  const events = object(ref.fileID, 'AnimatorStateTransition').m_Conditions.map(condition => condition.m_ConditionEvent);
  check(!events.includes('tPick') && !events.includes('tThrow'), 'Base layer no longer consumes upper-body triggers');
}
function validateReferences(value) {
  if (!value || typeof value !== 'object') return;
  if ('fileID' in value && !('guid' in value) && value.fileID !== 0n)
    check(controller.has(String(value.fileID)), `Resolved controller reference: ${value.fileID}`);
  for (const child of Object.values(value)) validateReferences(child);
}
for (const doc of controller.values()) validateReferences(doc.value);

const maskDocs = documents(read('Animation/Character/Player UpperBody.mask'));
const mask = maskDocs.get('31900000').value.AvatarMask;
const maskGuid = read('Animation/Character/Player UpperBody.mask.meta').match(/^guid: (\w+)/m)[1];
check(layers[1].m_Mask.guid === maskGuid, 'Controller mask GUID points to the authored asset');
const weights = new Map(mask.m_Elements.map(element => [element.m_Path || '', Number(element.m_Weight)]));
check(weights.get('') === 0 && weights.get('mixamorig:Hips') === 0, 'Root and hips are never overridden');
for (const [bone, weight] of weights)
  check(weight === (bone.startsWith('mixamorig:Hips/mixamorig:Spine') ? 1 : 0), `Mask preserves the upper/lower-body boundary: ${bone}`);
for (const clip of ['Pick', 'Digging']) {
  const paths = new Set([...read(`Animation/Character/${clip}.anim`).matchAll(/^    path: (.+)\r?$/gm)].map(match => match[1].trim()));
  for (const bone of paths) check(weights.has(bone), `Mask covers the authored generic-rig curve: ${bone}`);
}
const playerController = read('Scripts/Character/Player/PlayerController.cs');
check(/else if \(hasManualMovementInput\)\s*\{\s*HandleManualKnifeMovement\(\);\s*\}/.test(playerController),
  'Manual input starts nearby swings without being zeroed during a pending knife attack');
console.log(`PASS: ${checks} player controller/mask asset checks`);
