# 벨트 슬롯의 반복 이동 판단을 줄이는 설계 연구

2026-09-09 · 소스 기준 `883cefb` · 입력: 사용자가 제공한 09:30:41 스냅샷

현재 우선 과제는 **기존 슬롯 경로에서 실패 이유를 한 번 계산하고, 그 이유가 바뀌는 사건이나 예약 시각까지 재검사를 하지 않는 것**이다. 새 운송 구간의 공통 이동보다 일반 블록·코너·라인 wake 경로가 비용의 대부분을 차지한다. 기존 목적지 대기자와 완료 시각 힙을 확장하고, 일반 재처리 요청과 강제 상태 무효화를 분리하는 접근을 권장한다.

이 문서는 구현 제안이다. 게임 코드·설정 변경, 엔진 실행·조작, 새 성능 측정은 하지 않았다. 실행한 것은 제공된 텍스트의 수치 정규화 스크립트뿐이다. 현재 소스와 스냅샷 실행 바이너리가 동일하다는 검증은 없으므로 코드 경로의 존재와 그 경로의 실제 비용 귀속을 구분한다.

## 1. 측정 기준과 목표

원본은 [baseline-snapshot.txt](C:/Git/ProjectF/Research/ConveyorSlotScheduling-2026-09-09/baseline-snapshot.txt), 정규화 결과는 [baseline-analysis.json](C:/Git/ProjectF/Research/ConveyorSlotScheduling-2026-09-09/baseline-analysis.json)이다. `Analyze-Snapshot.ps1`을 실행하면 원본 SHA256과 함께 재생성한다. 엔진이나 진단 포트에 연결하지 않는다.

약 980ms 측정창, 벨트 프로파일 프레임 29개. 아래 시간은 `TotalMs / 29`이다.

| 범위 | ms/프레임 | 의미 |
|---|---:|---|
| 벨트 틱 전체 | 11.681 | 부모 범위 |
| 기존 wake 큐 | 11.454 | 벨트 틱의 약 98.1% |
| 라인 wake | 4.542 | 큐 내부 |
| 개별 블록 wake | 4.195 | 큐 내부 |
| 코너 그룹 wake | 2.633 | 큐 내부 |
| 라인 이동 실패 후 처리 | 0.865 | 라인 wake 내부 |
| 새 운송 구간 관리 | 0.040 | 벨트 틱 내부의 별도 범위 |
| 아이템 렌더 준비 | 0.851 | 별도 렌더 범위 |

부모와 자식을 합산하지 않는다. 라인 wake 4.542ms 중 네 개 세부 범위(NoMove/MoveScan/RetryWork/BlockerScan)의 합은 1.847ms이고, 나머지는 2.696ms다. 이 차분에는 미계측 경로와 계측 오버헤드가 포함되며 특정 함수의 시간으로 확정할 수 없다.

일반 이동 시도는 프레임당 594.655회, 성공은 0.828회, 활성 등록 갱신은 301.138회다. 이 시도 수에는 빈 슬롯·아직 준비되지 않은 슬롯의 조기 반환이 포함된다. 공통 이동 등 별도 경로도 있어 전체 운반 성공률로 해석하지 않는다.

마지막 프레임 값은 별도로 해석한다. 라인 wake 307건, 실제 일반 라인 NoMove 35건, 개별 블록 틱 112건 중 진행 없음 111건, 코너 블록 79건 중 진행 없음 78건이다. 라인 wake 307건을 모두 중복이라고 간주할 수 없다. 새 운송 구간 라우팅이나 다른 조기 반환도 있다. `DuplicateFrameTicksSkipped=0`은 블록 틱 계측이며 라인 중복 이월이 없었다는 증거가 아니다.

목표는 안정화된 빈 구간·완전 정체 구간에서 **실제 상태 변화가 없으면 슬롯 판정·후속 탐색·활성 등록 변경이 0회**가 되도록 하는 것이다. 예약 힙의 맨 앞 확인이나 운송 구간 관리 자체까지 시스템 전체 O(1)이라는 뜻은 아니다. 계속 흐르는 공정의 실제 전송과 합류 경쟁 처리는 남아야 한다.

## 2. 현재 코드에서 반복을 만드는 지점

### A. 개별 처리 방식 선택이 강제 수면 해제로 연결됨

[QueueConveyorDirectWake](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:782)는 direct 표시를 넣고 라인 retry를 지운다. [ProcessQueuedConveyorBlockWake](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:2662)는 direct 표시가 있으면 `WakeConveyorMoveAttemptsAlongRuntimeFlowImmediate(false)`를 호출한다.

이 함수는 [현재·이전·다음 블록](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/Block.cs:4799)에 `WakeConveyorMoveAttempts(true, false)`를 적용한다. 첫 인자가 true이므로 해당 블록들의 blocked/cycle 수면을 풀며 retry 시각도 초기화한다. 두 번째 false는 큐 요청 억제일 뿐 상태 검사·해제 비용을 억제하지 않는다.

[라인 실패 후 fallback](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:3344)은 활성 여부가 false여도 아이템이 있으면 direct 처리에 포함한다. 따라서 «이 경로로 처리해야 한다»와 «기존 실패 이유가 무효해졌다»가 섞여 있다. 단순히 이 조건을 지우면 필요한 경계 이동이 누락될 수 있으므로 원인 분리가 선행돼야 한다.

### B. 수면 결정을 위해 이동 가능성을 다시 계산함

[SleepConveyorLaneBlocked](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/Block.cs:7858)는 유효한 목적지 실패 캐시가 없을 때 `CanRetryConveyorLaneMove`를 호출한다. 이는 [CanMoveConveyorLaneUncached](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/Block.cs:9542)를 거쳐 후속 슬롯을 재귀적으로 탐색할 수 있다.

이 읽기 전용 탐색은 `recordPlannedMoves=false`, `countPlanCall=false`, `cacheFailures=false`이다. 따라서 `PlanMoveCalls`에 포함되지 않고, 실제 이동 시도의 실패 정보를 일관되게 재사용하는 계약도 없다. 성공 가능 판단이 나와도 실제 적용 시점에는 목적지 상태를 다시 검증해야 한다.

[UpdateConveyorBlockedRetry](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/Block.cs:8441)는 이동했거나 모션이 있으면 `SleepConveyorMoveAttempts()`와 `WakeConveyorMoveAttempts()`를 연속 호출한다. 후자는 기본값상 모든 수면을 무조건 풀지는 않지만, 같은 슬롯의 상태 판정·네트워크 깨우기·시각 상태 갱신이 다시 발생할 수 있다.

### C. 캐시의 유효 범위가 너무 넓거나 너무 짧음

[CanMove 캐시](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/Block.cs:9585)는 현재 프레임과 전역 상태 버전이 모두 같아야 유효하다. 변하지 않은 정체 상태도 다음 프레임의 캐시 재사용은 불가능하다. 다른 벨트의 상태 변경도 전역 버전을 바꾸면 같은 프레임 캐시를 무효화한다.

[MarkConveyorItemVisualDirty](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/Block.cs:8061)도 전역 이동 판단 캐시를 무효화한다. 시뮬레이션에 영향을 주는 변경이 이 함수를 통해서만 알려지는 호출부가 있을 수 있으므로, 이 한 줄을 먼저 삭제해서는 안 된다. 표시 전용 변경과 점유·이동 준비 변경을 호출부에서 분리해야 한다.

반면 [목적지가 있는 실패 캐시](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/Block.cs:9648)는 이미 출발/목적지 점유 버전과 대기자를 사용한다. 이 경로까지 전역 버전만 본다고 설명하면 부정확하다. 다만 기본 1.2초 만료와 목적지 상태 재조회가 있어 영구적인 사건 대기는 아니다. 만료 시 제거될 뿐 그 자체가 반드시 틱을 발생시키는 것은 아니다.

### D. 지연 처리에서 요청의 의미가 보존되지 않음

[WakeConveyorNetwork](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:2093)는 batch 내부이면 `queueWake`를 전달하지 않고 블록만 deferred set에 기록한다. [FlushDeferredConveyorNetworkWakes](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:630)는 나중에 무조건 `QueueConveyorWake`를 호출한다.

따라서 `queueWake=false` 요청도 이 경로에서는 큐 요청으로 바뀔 수 있다. 해당 경로가 이번 11.454ms 중 차지하는 비율은 계측되지 않았다. 별도 두 번째 큐를 덧붙이기보다 기존 deferred 저장소의 값에 누적 요청 플래그를 보존하는 수정이 적절하다.

또한 [라인 enqueue](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:874)는 현재 pending 범위만 병합한다. 소비된 라인의 재요청은 다시 큐에 들어간 뒤 처리 시점에서 이월될 수 있다. 이 비용을 입구에서 줄일 수 있지만 기존 큐의 512건 예산과 공정성이 달라질 수 있다.

### E. 범위 검사와 등록 갱신이 목적별로 반복됨

[일반 라인 처리](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:3035)는 retry 검사, 실물 모션 검사, 역순 이동 검사, NoMove 정리를 각각 실행한다. 블록/슬롯 조회가 여러 번 발생한다. wake 범위는 이벤트 위치의 양쪽 2칸까지 넓혀지며 min/max 병합은 떨어진 두 이벤트 사이의 영역도 포함할 수 있다. 이번 스냅샷에서 그 확대 비용을 따로 측정한 것은 아니다.

[RefreshConveyorActivityRegistration](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/Block.cs:4539)은 데이터 모션, 활성 처리, 아이템 표시 등록을 다시 판단한다. 이미 Set/Dictionary에 들어 있는지 확인하는 멱등성만으로는 판단에 든 비용이 사라지지 않는다.

## 3. 권장 설계: 실패 이유와 재개 조건을 한 번 결정

기존 bool 결과와 흩어진 retry/sleep 상태를 정리하며, 실제 이동 시도에서 다음과 같은 결과를 반환하도록 바꾼다. 아래 이름은 설계상의 분류이며 새 boolean들을 기존 상태 옆에 추가하라는 뜻이 아니다.

| 결과 | 보관할 정보 | 다음 검사 조건 |
|---|---|---|
| Empty | 현재 점유 세대 | 해당 슬롯에 투입됨 |
| WaitingUntil | 완료 시각·예약 세대 | 모션 완료, hold 만료, 조기 해제 |
| WaitingForDestination | 출발·목적지 핸들/슬롯·의존성 버전 | 관련 목적지의 점유/이동 가능 조건 변경 |
| WaitingForTopology | 연결 버전 | 벨트 설치·삭제·회전·연결 변경 |
| DeferredThisFrame | 이번 프레임 이동 가드 | 다음 시뮬레이션 프레임 한 번 |
| Moved | 바뀐 슬롯과 다음 완료 시각 | 실제 다음 경계 사건 |
| NeedsLegacyEvaluation | 아직 모델링하지 않은 특수 경로 | 제한된 기존 처리 유지 및 별도 계측 |

각 상태는 **다음 작업을 예약하는 이유**를 나타낸다. 저장된 아이템 ID, 이동 모션, 표시 상태의 또 다른 복제본을 만들지 않는다. 핫 경로 결과는 값 형식과 재사용 버퍼로 전달하고, 이벤트마다 객체·delegate·LINQ 결과를 만들지 않는다.

흐름은 다음으로 단순화한다.

```text
상태 변경/기한 도래
  → 해당 슬롯에 원인과 세대를 기록하고 pending 작업을 병합
  → 실제 이동 판정 1회
  → 이동 적용 또는 실패 이유 + 재개 조건 등록
  → 바뀐 점유/스케줄/표시 등록만 반영
  → 새 사건이 없으면 해당 슬롯은 작업 목록에서 제외
```

### 3.1 일반 재요청과 강제 무효화 분리

direct/line/corner는 실행할 처리 방식이다. 수면을 해제해야 하는 원인은 vacancy, ready, inserted, topology, external change 등 별도 의미다. 일반 fallback이나 반복 요청은 기존 실패 이유가 유효하면 반환해야 한다.

기존 큐 payload를 통합된 플래그와 lane mask로 확장해 의미를 보존한다. deferred 단계에서는 플래그를 OR로 병합한다. `queueWake=false`만 들어왔으면 enqueue하지 않고, true가 하나라도 있으면 필요한 범위만 enqueue한다. 큐에 넣는 결정과 네트워크 표시 상태 정리는 분리한다.

이미 처리한 라인의 뒤늦은 요청은 버리지 않는다. 처리한 입력 세대가 같으면 중복으로 합치고, 세대나 범위가 달라졌으면 기존 한 프레임 제한에 맞춰 다음 작업으로 보존한다. 처리 중 더 높은 세대의 이벤트가 왔는데 완료 단계에서 dirty를 지우지 않도록 소비 세대를 저장한다.

이 변경으로 큐에서 소모되는 중복 예산이 줄면 같은 프레임에 실행되는 다른 작업이 늘 수 있다. 단순히 이전 내부 카운터 일치로 검증하지 말고 아이템 순서·이동 가드·합류 공정성과 프레임별 처리량을 비교해야 한다.

### 3.2 실패 판정 결과를 수면 판단에 재사용

`TryMoveConveyorLaneCore`와 계획기가 실패한 이유, 막힌 목적지, 가장 이른 준비 시각을 함께 반환한다. `SleepConveyorLaneBlocked`는 같은 의존성 세대의 결과가 있으면 재귀적인 `CanMove`를 다시 호출하지 않는다. 상태가 그 사이 바뀌었다면 결과를 폐기하고 재평가한다.

범위 내 슬롯 결과를 재사용하는 동안 성공한 이동이 다른 슬롯의 결론을 바꾸는 경우도 있다. 이동 적용 후 관련 의존성 버전을 갱신하고, 다른 항목의 오래된 결과를 성공 근거로 사용하지 않는다. 루트 기준 순환 판단과 `ignoreMoveAttemptThrottle` 모드도 캐시 계약에 포함해야 한다.

목적지가 점유돼 있다는 사실만으로 실패를 단정하지 않는다. 현재 코드는 앞 아이템을 함께 밀거나 루트로 돌아오는 순환 계획을 처리할 수 있다. 기존 계획기를 유지한 채 결과 재사용부터 도입하는 것이 1차 범위다.

### 3.3 변하지 않은 정체는 이벤트로, 시간에 따른 변화는 예약으로

기존 [목적지 대기자](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:269)를 확장한다. 이미 1,006개 source 대기자가 기록된 장면이므로 대기자 체계가 없는 상태가 아니다.

기존 [모션 완료 힙](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:2238)을 스케줄링 기반으로 재사용한다. hold-only처럼 현재 active data motion이 없는 상태도 예약할 수 있도록 일반화하거나 해당 예약에 연결해야 한다. 비슷한 타이머 관리자를 병렬로 새로 만들지 않는다.

같은 슬롯의 예약이 바뀌면 핸들 세대와 예약 세대로 이전 이벤트를 무효화한다. 무효 이벤트가 무한히 쌓이지 않도록 인덱스 기반 갱신 또는 정리 기준을 둔다. 한 프레임 이동 가드 해제는 같은 시각의 힙에 즉시 재삽입해 무한 반복하지 않고 다음 프레임 작업으로 보낸다.

표시 좌표 보간과 논리적 이동 가능 판단은 분리한다. 실물 아이템 Tween/코너 모션에 매 프레임 진행이 필요한 경우 그 경로는 유지하며, 논리 상태 전환에 완료 알림을 연결한다. 시간을 명확히 계산할 수 없는 특수 모션을 무기한 재우지 않는다.

새 [ConveyorTransportRun](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/ConveyorTransportRun.cs:88)은 이미 입출구 예약과 이벤트 대기를 사용한다. 이 원칙을 기존 블록·코너 경계에 적용하는 것이 우선이며, 0.040ms짜리 공통 이동을 먼저 더 최적화할 이유는 약하다.

### 3.4 대기 의존성은 바로 다음 빈칸만으로 끝나지 않음

```text
A → B → C(정체)
```

A의 실패가 C에 의존한다면, A/B를 모두 매 프레임 다시 검사할 필요는 없다. 그러나 C의 아이템이 나가거나 준비 완료되어 B가 움직일 수 있게 되면 A에도 변화가 도달해야 한다. 점유가 아직 그대로여도 가능성이 바뀔 수 있다.

단일 후속 슬롯 경로에서는 직전 의존자의 역방향 구독과 결과 버전 전달을 사용한다. C의 의미 있는 변경을 B에 전달하고, B의 결과가 바뀌었을 때 A를 깨운다. raw 이벤트마다 전체 상류를 재귀적으로 순회하지 않는다. pending 병합과 버전으로 중복 전달을 줄인다. 최악에는 영향받는 상류 길이만큼 작업이 필요하며 이를 O(1)이라고 주장하지 않는다.

분배기·대체 출구는 필요한 모든 의존성을 보존해야 한다. 현재 한 source가 한 destination을 구독하는 저장소를 무조건 그대로 확대 적용할 수는 없다. 첫 단계는 단일 후속 경로부터 전환하고, 여러 출구 경로는 기존 판단을 유지하며 별도 설계한다.

꽉 찬 순환 경로는 ‘누군가 먼저 빈칸을 만들 때까지 전원 수면’ 정책으로 바꾸면 멈출 수 있다. 기존 루트 순환 계획을 유지하고, 추후 순환 그룹의 준비 시각/외부 변경을 한 곳에서 처리한다. 그룹 생성은 토폴로지 변경 때 수행하고 매 tick 강연결요소를 재계산하지 않는다.

### 3.5 국소 버전과 표시 변경 분리

기존 `LaneOccupancyVersions`, 실패 목적지 핸들, 블록 핸들 세대를 재사용한다. 점유 외에 readiness/hold/모션 완료/연결 변경이 결과를 바꿀 수 있으므로 의존성 버전 또는 예약 시각에도 반영한다. 프레임 키만 제거하고 두 슬롯의 점유 버전만 비교하는 패치는 올바르지 않다.

후속 탐색에서 얻은 결과는 읽은 의존성을 통해 유효성을 보장해야 한다. 긴 dependency 배열을 매번 만들기보다 위의 구독/결과 버전 전달을 사용한다. 전환되지 않은 복잡한 경로의 전역 캐시는 유지하고, 전환 완료 영역부터 제거한다.

`MarkConveyorItemVisualDirty` 호출부를 점유 변경, 모션 상태 변경, 디버그 색/순수 표시 변경으로 감사한다. 실제 시뮬레이션 변경에 명시적인 버전 증가가 생긴 후 표시 전용 경로의 전역 무효화를 제거한다. 디버그 색이 바뀌었다는 이유로 먼 공정의 이동 가능 캐시가 폐기되지 않는 것을 검사한다.

## 4. 재개 이벤트의 필수 계약

| 사건 | 재개/취소해야 할 대상 | 검증할 현재 경계 |
|---|---|---|
| 투입·아이템 교체 | 해당 source 결과/예약, 필요한 상류 의존자 | SetConveyorItemAtLane, 외부 투입, 라인 adapter |
| 실제 빈칸 발생 | 역방향 대기자, 분배기 입력 | NotifyConveyorLaneVacated, Clear/Move/ItemClear |
| 목적지가 점유된 채 준비 완료 | 목적지와 그 readiness에 의존한 source | NotifyConveyorMotionSettled, 모션 완료 힙 |
| hold 연장·해제·자연 만료 | 해당 예약 취소/변경과 의존자 | Hold/ClearConveyorLaneMovement, hold-only 예약 |
| 같은 프레임 이동 제한 | 다음 프레임 한 번 | Was/MarkConveyorItemMovedThisFrame |
| 구조·방향·속도·레인 변경 | 연결 캐시·예약·대기 관계 재구축 | 배치/철거/회전, transport release, line cache clear |
| 블록 파괴·재사용·로드 | 오래된 핸들 폐기, 새 세대 등록 | coordinate-only 대기자와 RuntimeHandle 수명 |
| 저장 후 복원 | 아이템·잔여 모션에서 대기 관계 재생성 | 임시 예약/구독은 세이브 진실 원본으로 만들지 않음 |
| 중간 회수·라인 분할 | 제거 슬롯과 인접 경계의 예약 | Block.ItemClear, transport 소유권 전환 |

전송의 clear/set 도중 임시 빈칸을 즉시 처리하지 않는다. 현재처럼 전체 전송 적용 후 실제 비워진 source를 통지하고, batch 완료 상태를 소비하도록 유지한다. callback이 동기적으로 상태를 읽는 경우 변경 전 알림과 변경 후 알림의 순서를 명시해야 한다. `IncrementConveyorLaneOccupancyVersion`는 현재 transport 알림도 호출하므로 범용 알림을 여기에 무조건 덧붙이면 변경 전 데이터를 읽을 수 있다.

토폴로지 변경은 특히 주의한다. 현재 `ClearConveyorLineCache`는 대기자도 지운다. 재구축 뒤 필요한 슬롯을 한 번 재평가해 구독을 복원해야 한다. `WakeConveyorBlockedLaneWaitersForPotentialVacatedCoordinate`는 현재 검색상 정의만 있으므로, 이 헬퍼의 존재를 연결 변경 알림이 완성됐다는 근거로 삼지 않는다.

## 5. 단계별 구현과 제거할 기존 코드

| 순서 | 작업 | 완료 조건 | 함께 정리할 기존 구조 |
|---|---|---|---|
| 0 | 큐 유입 원인·미계측 라인 구간·읽기 전용 탐색 계측 | 실제 반복 생성자가 구분됨 | 중복 시간 측정/매 호출 문자열 생성 금지 |
| 1 | deferred 요청 의미 보존, 처리 방식과 수면 해제 분리 | false-only 요청은 큐 0건, 필요한 외부 변경은 재개 | 블록만 저장하는 deferred 의미 손실, 불필요한 앞뒤 강제 wake |
| 2 | 실패 이유 반환과 같은 의존성 세대 내 결과 재사용 | 실패 후 수면 확인용 중복 탐색 0회 | 같은 실패의 CanMove 재탐색 분기 |
| 3 | 단일 후속 경로를 이벤트/기한 대기로 전환 | 정체·빈 상태 안정화 후 반복 판정 0회 | 전환 영역의 주기적 실패 만료/재시도·안전 wake |
| 4 | 국소 캐시 무효화와 변경 시에만 등록 갱신 | 무관한 공정/표시 변경으로 재탐색하지 않음 | 전환 영역의 전역 캐시 무효화·무조건 Refresh |
| 5 | 코너·순환·분배기 경계로 확대 | 기존 운반 규칙·공정성 유지 | 검증 완료한 기존 fallback·retry 상태 |

각 단계가 끝날 때 이전 처리 상태와 새 상태를 동시에 진실 원본으로 유지하지 않는다. 안정적으로 전환한 범위의 죽은 분기·헬퍼·타이머를 제거한다. 코드는 `ProjectF.Conveyors`의 작은 결과/스케줄 보조 타입에 모으고 `Block`에는 읽기·적용·알림 연결점만 남기는 방향이 적합하다.

라인의 반복 순회는 위 단계 후 남은 비용을 보고 줄인다. 토폴로지·실물 모드 변경 때 캐시할 수 있는 blocker를 매번 검색하지 않고, 현재 dirty lane만 처리하도록 한다. 떨어진 요청 때문에 min/max가 넓어지는 비중이 크다면 비트셋 또는 재사용 interval 목록을 검토한다. 목록 관리 비용보다 절감이 큰지 먼저 확인하고, 독립적인 두 번째 dirty 체계를 추가하지 않는다.

현재 안전 스캔 기본 간격은 0.25초이고 마지막 프레임에는 33건의 wake를 넣었다. 이벤트 계약이 완성되기 전에 끄지 않는다. 전환 영역에서는 대기 구독/예약의 정합성을 낮은 빈도로 검사하는 복구 진입점으로 바꾸고, 정상 성능 측정에서는 자동 복구가 몇 번 발생했는지 별도 기록한다. 정상 운반이 안전 스캔에 의존하면 이벤트 누락으로 취급한다.

## 6. 필요한 추가 계측

전체 슬롯마다 Stopwatch를 두면 계측 비용이 병목이 될 수 있다. 시간은 단계별로, 세부 원인은 고정된 정수 카운터로 모은다. 진단 꺼짐의 빠른 분기는 유지한다.

| 계측 제안 | 답할 질문 |
|---|---|
| WakeRequestsByReason + Enqueued/Merged/Deferred/Unchanged | 실제 변경 없이 누가 다시 넣는가? |
| WakeLine.RouteOwned / DuplicateDefer / Fallback / RangeSetup | 라인 시간 차분 2.696ms는 어디에 있는가? |
| DirectWake.SleepClears / NeighborTouches | 처리 방식 때문에 몇 슬롯의 수면이 풀리는가? |
| DeferredQueueSuppressed / EscalatedToQueue | false 요청이 얼마나 enqueue로 바뀌는가? |
| Plan.ReadOnlyCalls / NodeVisits / ActualMoveCalls | 현재 PlanMoveCalls가 누락하는 탐색량은 얼마인가? |
| CanMoveCacheMiss.Frame / GlobalVersion / Dependency / Time | 캐시는 왜 무효해지는가? |
| SleepDecision.Reused / Replanned | 이미 실패한 일을 또 계산하는가? |
| ActivityRefresh.Attempts / MembershipChanges / DeadlineChanges | 등록 갱신 중 실제 변화 비율은 얼마인가? |
| PendingAgeMax/P95, DueLate, PerLineStarvation | FPS를 위해 운반을 뒤로 미룬 것은 아닌가? |

마지막 프레임 값과 창 평균을 분리한다. 프로파일링 켬/끔 모두 동일 세이브·카메라·시간 조건으로 비교한다. 현재 스냅샷에는 유효 GPU 시간이나 개별 프레임 p95/p99가 없으므로 전체 프레임 병목 비율과 안정적인 60FPS를 확정하지 않는다.

## 7. 하네스 검증 계획

기존 하네스의 경계와 한계를 먼저 지킨다. `ConveyorWakeOptimizationHarness`는 이전 큐 순서와 카운터까지 비교한다. 스케줄 의미를 바꾸는 이번 작업은 그 동등성만으로 승인할 수 없다. 이전 내부 호출 횟수를 맞추기 위해 불필요한 연산을 유지하지 말고, 외부 동작 불변식을 독립 참조 모델로 확인한다.

기존 `ConveyorTransportHarness`의 World/Wake/TimingChecks와 `ConveyorStraightTransferHarness`를 확장하고, 전체 scheduler·실제 sleep/CanMove/deferred flush 메서드를 함께 연결한 검사를 보강한다. 해당 영역을 대역으로 바꾸면 이번 결함을 놓친다. 초기 상태가 같은 고정 시드 비교, 예약 경계 검사, 작업량 검사로 나눈다.

| 시나리오 | 동작 조건 | 비용 목표 — 실측 결과가 아닌 합격 기준 |
|---|---|---|
| 빈 벨트/막힌 종단 체인 100·1천·1만·10만 슬롯 | 준비 후 10초, 외부 사건 없음 | 일반 슬롯 평가/읽기 전용 계획/등록 변경 0회; 초기화 비용 별도 |
| 독립 정체 공정 A + 움직이는 공정 B | B의 점유·표시 변화가 A를 바꾸지 않음 | A 재평가 0회 |
| 출구 회수/중간 회수 | 기존 순서·수량·간격, 필요한 재개 지연 보존 | 영향받는 슬롯만 평가; 전체 월드 스캔 0회 |
| hold-only, 모션 완료, hold 연장/조기 해제 | 정확한 시각 전에는 이동 금지, 이후 재개 | 기한 전 반복 계획 0회, 오래된 예약 미실행 |
| 목적지 점유 상태의 readiness 변화 | 앞 아이템 밀기 가능해지면 재개 | vacancy가 없다는 이유로 영구 수면하지 않음 |
| 순환 벨트: 꽉 참/부분 점유/한 아이템 회수 | 기존 순환·동시 전송 규칙 보존 | 정체 판단의 전역 반복 스캔 제거 가능 범위 확인 |
| 코너·T 합류·분배기·1F/2F | 합류 순서, 분배 공정성, 교차 간격 유지 | 큐 축소로 특정 입력이 굶지 않음 |
| 실제 이동 중 batch + false/true 혼합 요청 | false-only는 enqueue하지 않음; true 요청 보존 | 중복 알림은 하나로 병합 |
| 같은 프레임 늦은 다른 범위 이벤트 | 이벤트 유실 없음, 중복 이동 없음 | 세대별 필요한 재평가만 수행 |
| 512건 초과 burst | 예산·프레임별 처리량·최대 대기 지연 관측 | 처리량을 낮춰 FPS만 높이지 않음 |
| 블록 삭제/재사용·방향/속도 변경·로드 | 낡은 예약 취소와 의존성 복원 | 새 대상에 옛 이벤트 적용 0회 |
| 로봇팔 착지·실물 전환·세이브 왕복 | 이동 위치·아이템 ID·수량 보존 | 안전 스캔이 없어도 재개 |
| 디버그 색/표시 토글 | 게임 상태와 처리량 불변 | 표시 전용 변경이 계획 캐시를 무효화하지 않음 |

warm-up 후 반복 구간 관리형 할당도 측정한다. 대기자 리스트는 재사용하고, 장시간 예약 변경 후 heap/구독 크기가 수렴하는지 확인한다. 검사에서 stale 이벤트를 의도적으로 주입하여 블록 핸들 재사용과 세대 무효화가 실제로 작동하는지 검증한다.

엔진 실행이 허용되는 후속 단계에서는 동일 공정과 카메라에서 프레임별 p50/p95/p99, 16.67ms 초과 비율, 운반량/초, ready-to-move 지연을 함께 비교한다. 하네스의 10만 슬롯 결과는 Unity 전체 10만 시설물 FPS와 동등하지 않다.

## 8. 우선하지 않을 접근과 기대치

- retry 간격만 늘리기: 호출 수는 줄지만 입력·출구 반응과 처리량이 나빠질 수 있다. 이미 알려진 기한 또는 실제 사건을 사용한다.
- 수면/실패 캐시를 무조건 영구화하기: 모션 완료, 같은 프레임 가드, 토폴로지 복원, 꽉 찬 순환 경로를 놓칠 수 있다.
- 큐 상한을 낮추기: 프레임 비용을 지연으로 옮길 뿐이다.
- 새 운송 구간 공통 이동부터 더 빠르게 만들기: 이번 창에서 0.040ms이므로 절감 가능한 절대 시간이 작다.
- 멀티스레드부터 적용하기: 중복 처리·공유 wake 상태·순서 의존성이 남는다. 독립 구간과 외부 동작 계약이 정리된 후 검토한다.

가장 먼저 구현할 묶음은 **원인 계측 + deferred 요청 의미 보존 + direct 처리 방식과 강제 wake 분리**다. 다음으로 실패 결과 재사용과 단일 후속 경로의 사건 대기를 도입한다. 이 단계가 기존 큐 11.454ms를 얼마나 줄일지는 새 측정 전에는 수치로 예측하지 않는다. 큐 전체를 없앨 수 있다는 가정도 하지 않는다.

전체 프레임 31.601ms에는 로봇팔과 다른 시스템 비용도 포함된다. 이번 창에서 로봇팔 두 종류의 누적 시간은 벨트 프로파일 프레임으로 정규화하면 약 7.05ms다. 서로 측정창이 정확히 같다는 보장은 없으며 벨트 최적화만으로 안정적인 60FPS를 보장하지 않는다.

## 9. 외부 자료와 적용 범위

- [Factorio FFF-148, 2016-07-22](https://www.factorio.com/blog/post/fff-148): 할 일이 없는 대상을 업데이트에서 제외하고, 벨트 구간을 묶어 작업량을 줄이는 원칙. ProjectF의 개별 슬롯 이벤트 설계는 이 원칙을 현재 코드에 적용한 제안이다.
- [Factorio FFF-176, 2017-02-03](https://www.factorio.com/blog/post/fff-176): 아이템 간격과 구간 경계로 공통 이동을 표현하고 코너를 운송 구간에 통합. 현재 ProjectF의 라인 운송 확대 방향에 참고하되 자료의 성능 배율을 ProjectF 예상치로 사용하지 않았다.
- [Factorio FFF-364, 2021-01-29](https://www.factorio.com/blog/post/fff-364): 비활성 로봇팔의 운송 라인 구독, wake 요청 병합, 활성화 순서의 중요성. ProjectF도 합류 순서와 공정성을 검사해야 한다는 근거이며 팩토리오와 같은 내부 구현이라고 주장하지 않는다.

연구 산출물은 이 보고서, 원본 스냅샷 복사, 정규화 JSON과 재생성 스크립트다. 런타임 구현·행동 변경 검증·성능 개선 실측은 수행하지 않았다.
