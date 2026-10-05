# Production ECS managed harness

Unity나 게임을 실행하지 않고 실제 생산 상태 코드와 저장 직렬화 코드를 검증한다.

```powershell
powershell -ExecutionPolicy Bypass -File Tools/ProductionEcsHarness/Run.ps1
powershell -ExecutionPolicy Bypass -File Tools/ProductionEcsHarness/Run.ps1 -FluidBoundary
powershell -ExecutionPolicy Bypass -File Tools/ProductionEcsHarness/Run.ps1 -FurnaceIntegration
powershell -ExecutionPolicy Bypass -File Tools/ProductionEcsHarness/Run.ps1 -OilDrilling
powershell -ExecutionPolicy Bypass -File Tools/ProductionEcsHarness/Run.ps1 -BenchmarkInputs
```

- 기본 모드: 생산 시계, 재료 소비, 출력 예약, 필터, 저장과 수명 검증.
- `FluidBoundary`: 실제 생산 월드의 유체 입출력 코드를 도달 가능한 배관 그래프와 저장소 대역으로 검증.
- `FurnaceIntegration`: 실제 일반·강철 용광로 에셋, 생산 엔티티, `FacilitySimulationWorld`, 전역 `SimulationTickWorld`, 바이너리 저장 직렬화를 함께 검증.
- `OilDrilling`: 실제 일반·전기 시추기 ItemDefinition의 생산·에너지 수치를 읽고 렌더링 archetype 참조를 확인한다. 실제 시추 상태 코드, 생산 월드 등록 코드, 두 스케줄러, 출력 경로 캐시와 바이너리 저장 직렬화를 검증한다.
- `BenchmarkInputs`: 실제 자동 보충 서비스·ECS 및 장면 모듈의 재료 공급 어댑터·변경 알림 진입점을 검증한다. 초기 채움, 외부 추출 후 즉시 보충, 언로드된 저장 스택, 기존 아이템 보존, 재료·연료의 가상 소비, 해제 후 실제 소비와 반복 보충의 GC 할당을 확인한다. 저장소와 이동 연출은 경계 대역이다.

시추기 검증에는 4방향 채굴 좌표, 수동 제작 매뉴얼 없이 채굴, 부분 전력, 연료 소모, 자원 고갈·스트리밍, 배관 분리·재연결, 펌프 용량, 잘못된 유체, 다른 생산기의 유체 선택 변경 시 재개, ECS 재료 수신기, 용량 변경 시 재개, 저장 후 엔티티 복원, 강제작동의 원유 생성·유출량과 실제 채굴 버퍼 보존이 포함된다.

시추기는 별도의 월드를 복제하지 않고 `ProductionWorld`의 저장·연료·전력·선택·렌더링 경로를 공유한다. `OilDrillingProcess`와 엔티티의 OilDrilling partial이 채굴 상태를 맡는다. 정상 채굴의 미전송 원유는 기존 `productionOutputFluidUnits`, 소수 채굴 진행량은 기존 `oilDrillingProgressUnits`에 저장한다. 편집 프록시도 두 값을 보존한다. 기존 저장 포맷을 변경하지 않는다.

시추기 전체는 `OilDrillingBatch` 한 개로 시설 스케줄러에 등록한다. 활성 목록은 추가·재개 시에만 설치 순서로 정렬하고, 유체 입출력은 메인 스레드에서 처리한다. 생산률은 전력·작업 대상·압력 비율이 바뀔 때 정수 단위로 캐시한다. 출력 용량과 압력은 한 번의 순회에서 확인하고, 배관 거리의 고정 압력 감쇠는 경로 생성 때 계산한다.

등록 순서와 설치 순서가 다른 두 시추기가 공유 탱크의 마지막 용량을 두고 경쟁할 때 설치 순서로 채굴하는지, 먼저 처리된 시추기 고갈 후 대기하던 시추기가 용량 알림으로 재개되는지도 검증한다.

강제작동에서 수신 경로와 실제 채굴 버퍼가 모두 없는 시추기는 생산률 합계로 유출량을 계산한다. 반복 틱에서 개별 엔티티 순회·정렬·전력 스냅샷 계산이 필요 없다. 배관 연결 알림이나 모드 전환 때 개별 처리로 복귀한다. 틱이 증가한 후 배치 실행 전에 연결 알림이 들어오는 경우에도 마지막 합산 구간을 한 번만 정산하는지 검증한다. 저장·아이템 초기화·엔티티 제거도 검증에 포함한다. 수신 경로가 있거나 실제 미전송 원유가 있으면 이 합산 경로를 사용하지 않는다.

시추기 모드의 일반 채굴 1천·1만·10만 개와 배관·자원이 없는 강제작동 10만 개 측정은 공유 템플릿을 사용한 엔티티 생성과 워밍업 후 실제 스케줄러의 120틱 평균·GC 할당량을 출력한다. 일반 채굴은 독립된 단일 배관들이 공유 탱크에 연결되는 대역으로 구성한다. 강제작동은 유출량과 논리적 처리 엔티티 수, 정렬 횟수도 검증한다. 배관 그래프·자원·전력·저장소는 대역이다. 생성 측정은 전체 설치 등록·블록 바인딩을 제외하며, 틱 측정도 Unity 렌더링·충돌·실제 배관 BFS·전력망·세이브 비용을 제외한다. 이 결과를 게임의 UPS로 해석하지 않는다.

Unity API 중 행렬 생성, 오브젝트 생존 검사와 플레이 상태는 관리 코드 호스트에서 실행할 수 없어 대역으로 바꾼다. 생산·채굴·스케줄링 로직은 원본을 사용한다. 그래픽 품질, 화면 선택·충돌, 실제 게임에서의 UPS는 별도 실행 검증이 필요하다.

런타임 프로파일러의 `OilDrillingECS/Entities`로 전환된 시추기 수를, `ProductionECS/FluidRouteBuildSearches`로 출력 경로 생성·재생성 시 성공한 탐색 횟수를 확인할 수 있다. 경로는 배관 토폴로지가 바뀔 때 다시 만들고, 시추기는 같은 경로에서 저장소 용량과 유체 종류만 확인한다. 일반 레시피 생산기의 매 틱 유체 종류 검증 탐색은 이 카운터에 포함하지 않는다.

`Facility ECS Scheduling`은 버킷 처리·정렬을, `Facility ECS Plan`은 계획·전력 준비를 분리해서 측정한다. `Oil Scheduling`, `Oil Apply`, `Oil Forced Empty Output`은 배치 내부 비용을 보여준다. `OilDrillingECS/ForcedEmptyOutputEntities`, `ScheduledEntities`, `LastProcessedEntities`, `MembershipSorts`로 합산 대상과 활성 시추기 수를 확인한다. 시설 스케줄러의 직접 처리 수는 시추기 수 대신 배치 하나를 센다. `ProductionECS/ProcessedUpdates`에는 합산된 논리적 엔티티 틱도 포함된다. 일반 채굴 10만 개의 실시간 유지 여부는 별도 성능 목표이며, 합산 강제작동의 성능과 구분해서 측정해야 한다.

전체 런타임·에디터 C# 컴파일 검증:

```powershell
powershell -ExecutionPolicy Bypass -File Tools/CraftingTreeQuantityHarness/Compile.ps1
```

네이티브 편집 프록시의 원유 버퍼·압력 검증:

```powershell
powershell -ExecutionPolicy Bypass -File Tools/OilDrillingMachineHarness/Run.ps1
```
