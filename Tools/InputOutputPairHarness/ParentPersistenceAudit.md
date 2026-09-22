# Parent IOModule 전수조사 — 2026-09-21

## 결론

이번 조사에서 사용자가 직접 수행한 **부모 지정 → Unity 종료 → Unity 재실행** 1회는 통과했어.
MK2의 부모는 MK1로 유지됐고, 각 단계의 MK2 프리팹 전체 텍스트도 동일했어.
조사 중 게임·에디터 소스에 추가 패치는 하지 않았어.

이전 실패의 정확한 쓰기 호출은 기존 로그에 없어 확정하지 못했어.
이번 성공만으로 이전 실패를 사용자 조작 문제로 돌리거나, 모든 재발 가능성이 사라졌다고 판단하진 않아.

## 추가 재현: MK4 — 로드 불일치 확인, 재시작 재검증 대기

MK2 확인 후, 사용자가 MK4의 부모를 MK3로 지정하고 재시작했을 때 **화면에서 None이 되는 현상**을 보고했어.
같은 시점의 [MK4 프리팹](<C:/Git/ProjectF/FactorioProject/Assets/MapObject/InputOutputModule/Production machine (MK4)/Production machine (MK4).prefab:349>)에는
`{fileID: 11400000, guid: cdc8e149b82cd7947b30ef76b473abfc, type: 2}`가 그대로 남아 있었어.
이 GUID는 유효한 MK3 ItemDefinition이고, 그 정의의 MK3 프리팹 참조도 유효해.

따라서 MK4의 현재 증거는 **파일의 None 저장이 아니라 파일과 로드된 객체/화면 간 불일치**야.
MK2의 성공으로 전체 문제가 해결됐다고 볼 수 없어.
사용자가 컴퓨터 조작 제한을 해제한 뒤, 재지정·재컴파일 전에 Unity Pipeline으로 실제 값을 읽었어.
이번 추가 조사에서도 게임·에디터 소스는 변경하지 않았어.

### 실행 중인 Unity에서 확인한 증거

2026-09-21 19:26 KST, Unity 6000.4.0f1 / 프로세스 78904에서 읽었어.

| 비교 대상 | MK4 부모 값 |
| --- | --- |
| 프리팹 원본 YAML | MK3 GUID 유지 |
| Item Data 캐시의 SerializedProperty | null, instance ID 0, 미적용 변경 없음 |
| 캐시가 가리키는 실제 ProductionMachine | null, dirty=false |
| 새 SerializedObject로 읽은 같은 컴포넌트 | null, instance ID 0 |
| MK4 ItemDefinition.mapObject | 위와 동일한 영속 컴포넌트 |
| LoadPrefabContents로 별도 로드한 프리팹 | MK3 참조 정상 |
| 해당 MK4 프리팹만 ForceUpdate 임포트 후 | MK3 참조 정상, dirty=false |

캐시와 실제 객체가 같은 MK4 영속 GUID/fileID를 가리켰고, 부모 선택 목록에도 MK3가 정상적으로 있었어.
따라서 잘못된 선택 대상, 표시 옵션 누락, SerializedObject 캐시만의 문제는 아니었어.
재임포트 전후 원본 프리팹 전체 텍스트는 동일했어. 부모를 다시 대입하거나 저장해서 복구한 것이 아니야.

전체 ItemDefinition의 IO 모듈 21개를 네이티브 객체와 원본 기준으로 대조했어.
MK4 재임포트 후에도 `Sturdy wooden box → Wooden Box`에 같은 불일치 1개가 남아 있었어.
이 프리팹도 원본과 LoadPrefabContents는 정상인데 영속 로드 객체만 null이었고,
해당 프리팹만 재임포트하자 원본 텍스트 변경 없이 부모가 복원됐어.

**확정 범위:** 원본 데이터가 아니라 로드된 영속 프리팹의 상태가 원본과 달랐어.
**아직 미확정:** 이전 검증 코드가 남긴 상태인지, 현재 시작 과정에서도 다시 생기는지.
기존 수정이 완료된 상태에서 재시작 검증을 해야 하므로, 재임포트 성공만으로 해결 완료라고 판단하지 않아.

재현을 위한 읽기 전용 진단 스크립트: `CaptureLiveParentState.cs`의 `Main`과 `AuditAll`.

## 실제 재현 대조

| 단계 | 디스크의 MK2 부모 | 확인 |
| --- | --- | --- |
| 조사 시작 | `fileID: 0` | 화면 표시만의 문제가 아니라 파일도 None이었어 |
| 사용자가 MK1 지정 | MK1 GUID | 19:01:49에 저장된 값을 읽었어 |
| 사용자가 Unity 종료 | MK1 GUID 유지 | 지정 직후와 프리팹 전체 텍스트가 동일했어 |
| 사용자가 Unity 재실행 | MK1 GUID 유지 | 파일 동일, 사용자도 화면의 `유지됨`을 확인했어 |

확인한 부모 GUID: `a74ba1d8dd011f34ca2a0c33b1aa5725`

대상: [MK2 프리팹](<C:/Git/ProjectF/FactorioProject/Assets/MapObject/InputOutputModule/Production machine (MK2)/Production machine (MK2).prefab:394>)

## 조사 범위와 결과

| 경로 | 확인 결과 |
| --- | --- |
| Assets 전체 메타 3,161개 | 숨김·ignore 경로 포함, GUID 중복 0개 |
| ItemDefinition 에셋 115개 | 아이템 이름·ID 중복 0개 |
| MapObject 참조가 있는 정의 53개 | 대상 GUID와 컴포넌트 fileID 누락 0개 |
| 부모 필드가 있는 프리팹 21개 | 이번 조사 시작 시 연결된 부모는 MK3→MK2, MK4→MK3, Sturdy wooden box→Wooden Box였어. 지정 후 MK2→MK1도 복원됐어 |
| 런타임 소스와 실제 `Library/ScriptAssemblies/Assembly-CSharp.dll` | 부모 필드는 private + SerializeField야. 컴파일된 런타임 전체에서 해당 필드에 직접 대입하는 IL 명령은 0개였어 |
| 이전 OnValidate 수정 반영 | DLL에 새 선택 검증 메서드가 있고, 옛 `HasCircularParentReference`와 참조 삭제 경고 문자열은 없었어 |
| 부모 선택·드롭 | 사용자가 바꿀 때 SerializedProperty를 수정하고, 순환 참조를 검사해 |
| Item Data 저장 | ApplyModifiedProperties 이후 SavePrefabAsset을 호출해. 실제 지정 직후 파일 저장도 확인했어 |
| Item Data OnEnable/OnDisable | 선택·캐시 초기화와 이벤트 정리야. 부모에 None을 쓰거나 JSON을 자동으로 불러오지 않아 |
| Rebuild/ProductionMachine 자동 채움 | Local Pair 목록을 수정해. 부모 필드에 직접 None을 대입하지 않아 |
| JSON 가져오기 | 부모 필드를 덮어쓰는 경로가 남아 있어. 단, Load JSON 버튼으로만 실행돼 |
| 중복 정의 정리 | 참조를 새 정의로 교체한 뒤 중복 에셋을 삭제해. 현재 중복은 없고, 아래의 중첩 참조 누락 위험은 별도로 존재해 |
| 종료·파괴 콜백 | InputOutputModule/InstallationObject의 등록·캐시 정리에서 부모 필드 쓰기는 없었어 |
| 기존 Editor/AssetImportWorker 로그 | 이번 유실의 쓰기 호출 스택이나 Parent IOModule 저장 실패 로그는 발견하지 못했어 |

## 별도로 발견한 위험 경로

아래는 코드로 확인한 위험이지만, **이번 재시작 유실의 원인으로 확정한 것은 아니야.**
전수조사 요청에 맞춰 기록만 했고, 재현되지 않은 원인에 패치를 추가하지 않았어.

1. **JSON 가져오기가 미해결 부모를 None으로 덮어써.**
   [ApplyInputOutputModuleJson](C:/Git/ProjectF/FactorioProject/Assets/Editor/ItemDataEditorWindow.cs:8451)에서
   JSON에 부모가 없거나 참조 해석에 실패하면 기존 값과 관계없이 null을 넣어.
   저장된 JSON은 파생 데이터이며, 자동 시작 로드 경로는 없어서 이번 종료·재실행 테스트와는 분리해야 해.

2. **임시 프리팹의 컴포넌트를 반환한 직후 해당 프리팹을 Unload하는 중복 코드가 있어.**
   [ItemManager](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Manager/ItemManager.cs:1228)와
   [ItemDataEditorWindow](C:/Git/ProjectF/FactorioProject/Assets/Editor/ItemDataEditorWindow.cs:8833)가
   sourceMatch를 못 찾으면 instanceMatch를 반환하지만 finally에서 임시 프리팹을 해제해.
   이 fallback 결과는 영속 MapObject 참조로 쓸 수 없어. 재빌드에서
   [mapObject를 조회 결과로 무조건 교체](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Manager/ItemManager.cs:2930)하는 부분과 함께 검증할 필요가 있어.
   이번에는 MK1~MK4를 포함한 MapObject 참조 53개가 모두 유효했어.

3. **중복 정의 정리가 중첩된 아이템 참조를 순회하지 않아.**
   [ReplaceItemDefinitionReferences](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Manager/ItemManager.cs:3275)는
   첫 순회 이후 enterChildren=false라서 Pair의 inputs/outputs 내부 참조를 교체하지 못해.
   부모는 최상위 필드라 이 문제의 직접 대상은 아니지만, 중복 에셋 삭제 후 레시피 참조가 끊길 위험은 있어.

## 검증 한계

- 기존 35개 하네스 검사는 순수 Pair 로직과 추출한 InputOutputModule.OnValidate 검증이야.
  Unity 네이티브 직렬화나 ProductionMachine의 모든 상속 콜백, 종료·재실행을 대신하지 않아.
- 이번에는 별도로 실제 사용자가 종료·재실행한 1회와 디스크 내용을 대조했어.
- 재빌드, JSON 가져오기, Undo/Redo, Prefab Mode 동시 편집을 섞은 재시작 시나리오는 실행하지 않았어.
- MK2의 최초 종료·재실행은 사용자가 수행했어. 이후 사용자가 조작 제한을 해제해,
  MK4 진단은 실행 중인 Unity에서 직접 읽고 두 프리팹을 대상으로 재임포트했어.
  Unity 종료·재실행은 아직 에이전트가 수행하지 않았어.
