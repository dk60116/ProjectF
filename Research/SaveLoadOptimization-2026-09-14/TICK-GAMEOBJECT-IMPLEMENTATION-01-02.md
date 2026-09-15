# Tick / GameObject 분리 — 1·2단계 진행 기록

작성일: 2026-09-14

## 현재 상태

**1단계의 실행 코어·하네스, 2단계의 벨트 상태 소유권·물류 View 수명 분리를 적용했어. 원래 기획의 1·2단계 전체 완료는 아니야.** 공통 IO 계약, 청크 없는 토폴로지 복원, 팔/설비 대상 조회의 Block 의존 제거는 남아 있어.

SaveSlot2 파일, 저장 포맷, 전체 청크 복원 범위는 바꾸지 않았어. 원거리 공장을 위해 필요한 기존 복원을 그대로 유지했고 근거리만 로드하는 최적화는 켜지 않았어. 이번 변경만으로 수분 로딩 문제가 해결됐다고 판단하면 안 돼.

## 적용한 코드

### 1. 독립 Tick 실행부

- `Simulation/Core/SimulationTickWorld.cs`로 기존 스케줄러를 추출했어. 60Hz, SimulationId 정렬, 주기 버킷, 전체 Plan 이후 Apply를 유지해.
- `SimulationTickContracts.cs`에 기존 인터페이스와 정수 시간 단위를 옮겼어. 코어는 `ProjectF.Simulation.Core.asmdef`의 `noEngineReferences: true`로 분리했어.
- 생성·등록·제거·Step·일시정지·시계 복원·해제를 Unity 씬 없이 실행할 수 있어. Tick 경계 명령 큐와 프로파일 관찰자 계약도 추가했어.
- 명령 큐는 단일 시뮬레이션 스레드용이야. 경계에서 확보한 명령 수만 처리하고 처리 도중 추가된 명령은 다음 Tick으로 넘겨. 기존 설치·철거·UI 입력 전체를 명령화한 것은 아니야.
- `MapObjectTickManager`는 Unity 시간·로딩·일시정지와 계측을 연결하고 실제 실행은 코어에 위임해. 아직 미이전 MonoBehaviour를 위해 생존 검사 어댑터를 유지해.
- 기존 실행 코드를 삭제해 두 스케줄러가 중복 구동되지 않게 했어. ID는 매 Tick 정렬 때 읽으므로 별도 `RefreshSimulationIdentity`와 호출부를 제거했어.
- 로딩/스트리밍 중 Tick 정지와 로딩 시간 누적 폐기 규칙은 그대로야.

### 2. 벨트 데이터 실행부

- `Simulation/Logistics/BeltSimulationWorld.cs`가 기존 NativeArray 버퍼와 Job 수명을 소유해. 기존 `BeltSimulationJob` 알고리즘은 바꾸지 않았어.
- `BeltLaneId(X,Y,Lane)`로 레인, 출발점, 합류 커서를 식별해. 게임 호스트의 재구성·대기 쓰기 키도 Block 객체에서 ID로 바꿨어.
- 게임 호스트에는 `beltJobViews`를 별도 어댑터 배열로 남겼어. 같은 좌표에 다른 Block 어댑터가 결합돼도 기존 Native 상태와 이동 진행률이 유지돼.
- 체크포인트의 출발점/커서를 찾을 때 로드된 Block을 검색하지 않아. 빈 레인 복원도 ID로 대기열에 넣어.
- 순수 데이터 포트로 삽입/회수할 수 있고 중복 수락을 막아. 데이터 포트는 현재 코어 하네스에서 사용하며, 기존 게임의 모든 Block IO를 이 포트로 교체한 것은 아니야.
- Job 완료와 Tick 확정을 구분했어. 기존 호스트는 결과 반영 후 `CommitStep`, 독립 실행은 `Step`을 호출해. 코어 체크포인트/데이터 IO는 미확정 Tick 중 접근을 거부해.
- 그룹/분배기/연결/필터 구간을 검증해 서로 다른 작업의 소유 영역을 넘는 토폴로지를 거부해.

### 3. 물류 레코드와 View 수명

- `RobotArmWorld`, `PipeWorld`, `ConveyorWorld`를 MonoBehaviour에서 명시적으로 해제하는 런타임 서비스로 바꿨어.
- 각각 `Simulation/Presentation/*WorldView.cs`가 표현용 GameObject와 프레임 콜백을 맡아. View 비활성화/파괴는 Tick 등록이나 레코드 삭제를 수행하지 않아.
- `AttachView`/`DetachView`로 표현을 결합·해제해. 로봇팔의 렌더러·카메라·Collider는 View로 이동했어. 컨베이어/파이프의 렌더 캐시는 아직 서비스 내부에 있어.
- 로봇팔 Tick 등록은 월드 생성/해제에만 연결했고, 타깃 활성 판정에서 `World.isActiveAndEnabled`를 제거했어.
- Terrain 소멸 시 세 서비스를 명시적으로 해제해. **Terrain 자체의 OnDisable까지 View 분리로 간주하지 않아.** 현재 Terrain 비활성화는 여전히 벨트 실행부를 정리해.
- 파이프 연결 방향을 등록 시 읽기 전용 `PipeConnections`로 굽도록 바꿨어. 런타임 연결 조회는 프리팹을 다시 읽지 않아. 실제 유체량 이동은 아직 `InputOutputModule`의 IO 경로가 맡아.
- 기존 세 월드 스크립트 GUID를 참조하는 직렬화된 scene/prefab/asset은 저장소 검색에서 발견되지 않았어.

## 남아 있는 상태/의존 경계

| 영역 | 지금 상태 원본/어댑터 | 아직 필요한 작업 |
| --- | --- | --- |
| Tick | 독립 SimulationTickWorld, Unity 호스트 | 전체 입력 명령화, 미이전 타깃 생존 어댑터 제거 |
| 벨트 | Native 버퍼 + 기존 동기 IO 예약 미러 | Block 없는 연결/기하 굽기, IO의 단일 쓰기 소유권 |
| 파이프 | 독립 연결 데이터 + PipeWorld 레코드 | 순수 유체 포트와 실제 수량 전달 분리 |
| 로봇팔 | ResourceStateSlots + RobotArmInstance | 대상/레인/차량 화물/전력 조회 계약 분리 |
| 저장 | 기존 BlockStateStore 및 DTO | 전체 데이터 등록 → 연결 → View의 독립 부트스트랩 |
| 정의 | 기존 ItemDefinition/제작 트리 원본 유지 | 읽기 전용 실행 정의 스냅샷, 통합 조회 계약 |

기존 ResourceStateSlots의 세대 핸들과 SimulationId를 유지해. 새로운 전역 영속 ID 체계를 추가하지 않았어. BeltLaneId는 기존 저장 포맷의 좌표/레인 식별을 값 타입으로 표현한 것이고, 앞으로 설치물의 삭제·재설치 세대와 분리해서 다뤄야 해.

## 남은 이전 경로 목록

현재 Tick 구현 검색에는 TerrainGenerator, WorldTimeService, RobotArmWorld, AnimalAIWorld, ResourceInstance, TreeInstance, InputOutputModule, Bucket, Fluid tank, LoggingMachine, SteamTrain 계열이 잡혀. 상속된 구현과 부분 클래스도 함께 조사해야 해.

프레임/콜백 검색에는 PortableObject, AnimalAIController, UtilityPole, FreightCar/SteamTrain, InstallationPlacementController 등이 남아 있어. 이 목록은 검색 출발점이고, 모두 영속 상태를 바꾼다는 판정이나 전체 전이 경로 감사 완료를 뜻하지 않아. 렌더러 전용 Update는 별도로 분류해야 해.

1·2단계 완료를 위해 다음 순서로 이어가야 해.

1. 레인·인벤토리·유체·전력·화물의 공통 접근/예약/변경 알림 계약과 읽기 전용 정의를 정하고, 기존 상태 저장소의 쓰기 권한을 하나로 정리해.
2. `EnsureBeltSplitGroups`, `AppendBeltJobConnections`, 경로 길이/속도 굽기를 설치 레코드 기반으로 옮겨. 기존 코너·2F 교차·분배기 기하 하네스를 연결해.
3. `QueueBeltJobWrite`의 `LegacySource`와 Block 슬롯 미러 기반 입력을 데이터 포트로 바꿔. 일부 기존 변경 통지는 실제 필드 변경보다 먼저 발생하므로 단순히 통지 시점에 값을 캡처하면 안 돼.
4. `RobotArmInstance`의 Block/설비 조회를 데이터 포트로 대체하고 순수 팔→벨트→컨테이너 공정을 검증해. 미이전 생산 설비만 좁은 어댑터로 연결해.
5. 실제 유체 전달망도 데이터 포트로 구동하고, 전체 물류 레코드를 먼저 등록한 뒤 연결을 일괄 복원해. 전체/일부/무표시 및 재결합 결과를 비교해.

이 작업들이 끝나기 전에는 2단계 완료나 원거리 청크 생략을 선언하면 안 돼.

## 검증 결과

Unity/게임/UI를 실행하지 않았어. 소스 컴파일과 엔진 밖 하네스만 실행했어.

- 독립 코어 DLL: .NET Standard 2.1 빌드, 경고 0 / 오류 0.
- 실제 게임 `Assembly-CSharp`: 새 코어 DLL 참조와 보충 targets로 컴파일, 경고 113 / 오류 0. Unity 에디터 임포트/플레이 모드 검증은 아니야.
- SimulationCoreHarness: 22개 통과. ID 변경 정렬, 관찰자 분리, 10,000 Tick 관리 할당 0 포함.
- CheckBoundaries: 코어 엔진 참조와 세 물류 월드의 View 수명에 대한 소스 계약 4개 통과.
- BeltJobsHarness: 3,087개 통과. 실제 커널/호스트/독립 데이터 월드 소스, 직렬·역순·병렬 결과 비교, 저장/복원과 어댑터 재결합 포함.
- RobotArmEcsHarness: 420개 통과.
- AnimalAIOptimizationHarness: 63개 통과.
- PipeFlowHarness: 32개 통과.
- SprinklerStorageHarness: 48개 통과.
- SeedPlanterHarness: 18개 통과.
- PipeEcsHarness: 27개 소스 통합 계약 통과.
- RobotArmVisualHarness: 16개 원본 애니메이션 트랙과 표현 경계 소스 계약 통과.

일부 기존 하네스의 추출 경로를 새 코어로 바꾸고, 빠진 프로덕션 헬퍼/테스트 대역을 보완했어. 테스트 하네스의 대체 스케줄러는 실제 SimulationTickWorld로 교체했어.

별도로 확인한 MapObjectProfilerHarness는 기존 테스트 대역에 Mathf.Clamp가 없고, 이를 일시 보완해도 기대 JSON에 renderFrames/simulationTicks/p95Us/p99Us가 없어 실패했어. 보완 실험은 되돌렸고 기대값을 덮어쓰지 않았어. 프로파일러 본문 29,944자는 변경 전과 동일함을 비교 확인했어.

하네스의 NativeArray/Job/Block 대역은 Unity safety, Burst 기계어, 실제 GameObject 파괴/재생성, 씬 렌더링을 검증하지 않아. 독립 벨트의 할당 검사는 병렬 테스트 대역 자체의 할당을 제외한 동기 스케줄링에서 수행했어. SaveSlot2 로딩/저장 시간과 실제 원거리 전체 공정은 이번에 재측정하지 않았어.
