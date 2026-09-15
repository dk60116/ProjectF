# Tick / GameObject 분리 — 전역 객체 경계 진행 기록

작성일: 2026-09-14

## 현재 상태

전역 객체 식별·좌표 인덱스와 표시 객체 생명주기를 분리했고, 데이터 전용 설치물을 개별 GameObject 없이 배치 렌더링할 수 있게 했어. 동물 월드도 생성 시 GameObject를 요구하지 않고 표시 View를 별도로 붙이게 바꿨어.

하지만 Tick/GameObject 완전 분리는 아직 아니야. 일반 생산 설비, 전력망, 열차, 개별 동물 컨트롤러의 실행 상태가 MonoBehaviour에 남아 있어. 따라서 원거리 시각 청크를 생략하는 로드는 아직 활성화하면 안 돼.

## 이번 변경

### 전역 객체 월드의 씬 생명주기 제거

- `VirtualObjectWorld`를 MonoBehaviour에서 `IDisposable` 관리 서비스로 바꿨어.
- 생성 과정에서 `FindObjectOfType`, `new GameObject`, `AddComponent`를 제거했어.
- `GameManager`가 서비스를 명시적으로 생성하고 해제해.
- 설치물과 자원 Component 참조를 제거하고, 데이터 상태·핸들·좌표·표시 인스턴스 ID만 보관해.
- 핸들 generation을 프로세스 전역 epoch로 발급해 월드를 다시 만들었을 때 예전 핸들이 우연히 살아나는 경우를 막았어.

### 상태와 View 결합 경계 명시

- 데이터 전용 설치물 등록은 항상 `Virtual` residency로 기록해.
- 표시 객체 연결은 `AttachInstallationView` 하나로 모았고, 인스턴스 ID와 위치·회전 값만 전달해.
- 표시 객체 위치 변경은 연결된 인스턴스 ID가 일치할 때만 반영해.
- View 제거는 시뮬레이션 레코드와 핸들을 삭제하지 않아.
- BlockStateStore의 live 레코드와 저장 레코드는 같은 상태 객체를 가리키게 해 View 연결 때문에 실행 상태 복사본이 하나 더 생기지 않게 했어.

### 데이터 전용 설치물 표시

- `StaticMapObjectBatchRenderer`가 활성 InstallationObject 목록뿐 아니라 `VirtualObjectWorld`의 데이터 전용 설치물 레코드도 읽어.
- 데이터 전용 레코드는 저장된 pose와 핸들로 인스턴스 행렬을 만들며, 설치물마다 Transform이나 GameObject를 만들지 않아.
- 컨베이어·파이프·로봇팔은 기존 전용 데이터 렌더러가 있으므로 일반 배치에서 제외해 중복 표시를 막았어.
- 표시 View가 다시 연결되면 같은 핸들의 데이터 전용 슬롯을 live source 슬롯으로 교체해.

### 동물 월드 생성과 표시 연결 분리

- `AnimalAIWorld.EnsureFor(GameObject)`를 제거하고 `AnimalAIWorld.Ensure()`로 바꿨어.
- `AttachView(Transform)`을 별도 호출로 분리해 시뮬레이션 월드는 표시 부모 없이 생성·Tick·해제할 수 있어.
- View를 떼고 다시 붙여도 스케줄러 등록, Needs 시간과 컨트롤러 상태가 유지돼.
- 개별 `AnimalAIController`가 아직 MonoBehaviour인 점은 남아 있어.

## 검증

Unity 에디터·게임·PC UI와 실제 SaveSlot2는 실행하거나 조작하지 않았어.

| 검사 | 결과 |
| --- | --- |
| Assembly-CSharp 보충 참조 빌드 | 경고 115 / 오류 0 |
| SimulationCoreHarness | 244개 통과 |
| VirtualObjectWorldHarness | 20개 통과 |
| WorldPersistenceHarness | 52개 통과 |
| AnimalAIOptimizationHarness | 68개 통과 |
| CheckBoundaries | 순수 코어, 4개 월드 수명, 전역 객체 월드와 데이터 전용 표시 경계 통과. 직접 설치물 Tick 소유자 5개를 명시적 부채로 고정 |

새 `VirtualObjectWorldHarness`는 Unity 씬이나 GameObject를 만들지 않고 데이터 설치물 등록, View 연결·해제, 좌표 조회, 논리 교체, Dispose, 월드 재생성 뒤 stale handle 차단을 검사해.

## 완전 분리까지 남은 작업

1. InputOutputModule과 파생 설비의 인벤토리·진행률·연료·입출력 예약을 `InstallationRuntimeWorld` 같은 순수 실행 저장소로 이관해야 해.
2. 전력 공급/소비와 전봇대 연결을 GameObject 참조가 아닌 안정 ID와 포트 데이터로 계산해야 해.
3. Fluidtank, Bucket, LoggingMachine, SteamTrain 등 직접 Tick 등록하는 설치물 상태를 순수 실행 레코드로 옮겨야 해.
4. AnimalAIWorld의 컬렉션 원소를 `AnimalAIController`에서 순수 동물 상태 핸들로 바꾸고 Controller를 View 어댑터로 내려야 해.
5. 이관이 끝난 뒤 전체 엔티티 등록·연결 복원과 근거리 View 생성 경로를 실제로 분리하고 Slot2를 측정해야 해.

다음 구현 단위는 일반 설비 공정이 가장 적합해. 공통 생산 상태와 입출력 포트부터 데이터 월드로 옮긴 뒤 보일러·펌프·채굴기 같은 파생 설비를 하나씩 같은 실행 경계에 붙이는 순서가 안전해.
