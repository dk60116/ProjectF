# 벨트 반복 판단 최적화 적용

2026-09-09 · 변경 전 소스 `883cefb`

기존 슬롯 경로의 불필요한 수면 해제와 큐 등록을 제거하고, 실제 이동 계획에서 얻은 실패 결과를 같은 프레임의 수면 판단에 재사용하도록 변경했다. 게임 엔진을 실행·조작하지 않았다.

## 변경 내용

1. `TerrainGenerator`의 지연 네트워크 요청을 `HashSet<BlockHandle>`에서 `Dictionary<BlockHandle, bool>`로 교체했다. `queueWake`를 OR로 병합해 false-only 요청은 상태만 갱신하고, true가 하나라도 있으면 큐에 전달한다. 나중에 들어온 false가 true를 지우지 않는다. 재사용 버퍼로 flush 중 새 pending 요청을 보존한다.
2. `ProcessQueuedConveyorBlockWake`에서 직접 처리라는 이유로 자신·앞뒤 블록의 수면과 retry를 풀던 호출을 제거했다. 기존 슬롯 활성 판단은 그대로 사용한다. 빈칸·모션 완료·외부 변경의 명시적 깨우기 경로는 유지한다.
3. 일반 라인 fallback에서 `아이템이 있으면 처리` 조건을 제거했다. 실제 `ShouldTickActiveConveyor`가 true인 블록만 등록한다. 따라서 움직일 준비가 없는 아이템을 다시 등록한 후 수면을 푸는 경로를 줄인다.
4. `TryMoveConveyorLaneCore`의 계획 실패와 유효한 실패 캐시 반환 경로에서 기존 CanMove 캐시에 false를 기록한다. retry/cycle 처리가 기존 캐시를 무효화한 뒤 기록한다. 같은 프레임·같은 전역 상태 버전·같은 throttle 모드일 때만 재사용한다. 초기 readiness 거절은 계획 실패로 캐시하지 않는다.

새 수면 boolean이나 별도 실패 저장소를 덧붙이지 않았다. 이번에는 전역 캐시 유효성, 시간 만료, 안전 스캔, 슬롯 이동/순환 계획기, 완료 예약, 큐 예산을 교체하지 않았다. 영구 사건 대기와 전역 버전 분리는 후속 설계 범위다. 새 동작으로 불필요한 큐 항목이 줄면 같은 예산 안에서 다른 작업이 처리될 수 있으므로 실제 프레임 순서의 통합 확인은 여전히 필요하다.

## 새 진단 카운터

`ActiveConveyor` 그룹에 다음 두 값을 추가했다. 기존 카운터와 마찬가지로 마지막 틱 기준이며 시간 절감량 자체는 아니다.

| 이름 | 의미 |
|---|---|
| DeferredNetworkWakeSuppressed | 지연 네트워크 상태는 갱신했지만 false-only 요청이어서 시뮬레이션 enqueue를 하지 않은 블록 수 |
| DirectWakeInactiveSkips | 직접 처리 요청을 받았으나 기존 활성 판단상 실행할 일이 없어 tick 전에 반환한 블록 수 |

후속 동일 공정 스냅샷에서 `Process Wake Queue`, `Wake Block`, `Wake Corner Group`, `Line No Move`, `ActivityRefreshCalls`와 함께 비교한다. 카운터가 0이더라도 해당 경로 요청 자체가 없어졌을 수 있으므로 성능 효과가 없다는 의미는 아니다.

## 검증 결과

| 검사 | 결과 |
|---|---|
| ConveyorResearchHarness | 실제 dispatch/batch/수면/캐시/빈칸 알림 메서드 연결, 118개 단언 통과 |
| ConveyorTransportHarness | 저장소 11,041,204개, 소유권·입출구·예약 등 17,777개 단언 통과 |
| ConveyorWakeOptimizationHarness | 기존 큐·범위·retry·코너 순서 25,682개 단언 통과 |
| ConveyorStraightTransferHarness | 73,556개 단언, 성공 전송 458개 비교 통과 |
| ConveyorLineCacheHarness | 프록시 수명·결과 검사 5,854개 통과 |
| ConveyorPlacementHarness | 착지/배치 18개 검사 통과 |
| RobotArmIoHarness | 표준 IO/전송 저장 검사 143개 통과 |
| RobotArmBeltPickupHarness | 픽업 우선순위 6개 검사 통과 |
| SplitterHarness | 분배·공정성·깨우기 등 4,850개 단언 통과 |
| 독립 Roslyn 컴파일 | Unity가 생성한 참조/소스 목록 사용, Player(P)·Editor(E) 구성 DLL 생성 성공, 오류 없음 |

컴파일 결과는 임시 디렉터리에 출력했고 Library의 게임 어셈블리를 덮어쓰지 않았다. 기존 obsolete API 경고가 남는다. 새 런타임 소스 파일은 추가하지 않았다.

통제된 하네스에서 확인한 작업량:

- 준비 후 지연 요청 batch 10,000회: 반복 관리형 할당 0B, false-only enqueue 0회.
- 정체 블록 직접 요청 100회: 슬롯 tick 0회, 이웃 수면 강제 해제 0회. 실제 활성 판단 메서드를 사용한다.
- 이동 계획 실패 100회 후 수면/각성 판단: 추가 읽기 전용 계획 0회. 계획기의 결정 자체는 대역이며 실제 캐시·수면 호출 경로를 검사한다.
- 빈칸 predecessor/waiter 알림 뒤 source가 재개되고, 그 과정에서 불필요하게 더 먼 이웃의 수면을 해제하지 않는 것을 확인했다.

픽업 하네스는 변경 전부터 `GetBodyWorldPosition()`을 제거 함수에 직접 전달하는 이전 호출 형태를 요구했다. 현재 코드는 후보 선택기가 벨트 선택 시 body 기준을 반환하고 공통 제거 함수가 이를 받는다. 이 현재 계약을 검사하도록 오래된 문자열 검사를 갱신했으며, 로봇팔 런타임은 수정하지 않았다.

## 영향과 남은 확인

시뮬레이션 처리 요청과 같은 프레임의 실패 재사용에 영향이 있다. 저장 포맷·아이템 ID·레인 위치·운반 속도는 변경하지 않았다. 하네스에는 월드·렌더·물리·일부 모션/계획 대역이 있으므로 실제 Unity/IL2CPP 실행의 완전한 동등성 또는 FPS 개선량으로 해석하지 않는다.

실제 게임에서 최신 빌드로 정체 해제, 코너/순환/분배기, 로봇팔 착지, 긴 프레임 정지 뒤 재개를 확인하고 동일 장면의 프레임 시간과 운반량을 비교해야 한다. 이번 변경으로 11.454ms의 wake 큐가 얼마나 줄었는지는 아직 측정하지 않았다.
