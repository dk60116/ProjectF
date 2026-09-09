공유 대화와 ProjectF 적용 분석 — 2026-09-09

결론: 중앙 틱 관리, 주기별 분산, 캐시, 풀링은 이미 존재한다. 다음 성능 개선의 우선 대상은 **기존 컨베이어 wake 경로에서 실제 상태 변화 없이 다시 실행되는 작업**이다. 라인 공통 이동 구현은 유지하고, 그 적용 범위를 넓히는 작업을 이어가는 것이 적절하다.

분석 대상은 [공유 대화](https://chatgpt.com/share/6aa07bed-4288-83e8-91a1-1820a99cfac4)의 Factorio/Unity 제안과 현재 작업 디렉터리의 코드다. 미커밋 상태의 라인 소유권 및 경계 시각 예약 구현도 포함했다. 이번 작업은 정적 분석이며 게임 엔진 실행이나 새 FPS 측정은 하지 않았다.

공유 대화의 중앙 관리·필요한 객체만 갱신하는 원칙은 [Unity 공식 가이드](https://activation.unity3d.com/how-to/advanced-programming-and-code-architecture)와도 맞는다. 다만 일반적인 Unity 입문 예제를 현재 프로젝트에 다시 도입할 필요는 없다.

| 대화의 제안 | 현재 코드에서 확인한 상태 | 프로젝트 적용 판단 |
|---|---|---|
| 개별 Update 대신 중앙 관리자 | `MapObjectTickManager`에 등록 집합, 주기별 버킷, 순환 커서, 실제 경과 시간 전달, 대상이 없을 때 비활성화가 있다. | 이미 적용. 새로운 TickManager를 추가하지 않는다. |
| 낮은 빈도의 처리와 작업 분산 | 일반 `InputOutputModule`은 기본 0.1초 간격이며 sleep 시 등록을 해제한다. 버킷은 객체 수와 경과 시간으로 처리량을 계산한다. | 이미 적용. 생산 완료 예약처럼 의미 있는 다음 시각을 아는 작업부터 개선한다. |
| 이벤트·dirty·sleep | 컨베이어에 변경 알림, 지연 큐 병합, 실패 캐시, blocked waiter, retry가 있다. 하지만 기존 경로 일부가 대기 상태를 다시 해제하고 직접 검사를 요청한다. | 부분 적용. 현재 가장 먼저 다룰 영역이다. |
| 벨트 아이템을 라인 단위로 이동 | `ConveyorTransportStore`의 공통 변위와 `ConveyorTransportRun`의 소유권이 실제 이동 경로에 연결되어 있다. | 핵심 구현은 존재한다. 지원 구간이 제한되어 전체 컨베이어의 비용 감소로 이어지는 범위가 작다. |
| 필요한 시각에만 상호작용 | 소유 구간은 입출구의 다음 처리 시각을 저장한다. 빈 입구·막힌 출구는 변경 이벤트를 기다린다. | 적용됨. 모든 기존 블록 경로가 같은 정책을 사용하는 것은 아니다. |
| 참조 캐시·공간 인덱스·풀링 | `BlockDataStore`, 라인/네트워크 캐시, 렌더 버전 캐시, `PortableObjectPool` 등이 있다. | 기존 구조를 활용한다. 잦은 무효화나 전체 재구축이 있는 호출부를 측정한다. |
| Jobs/Burst | 아이템 렌더 행렬 계산에 `IJobParallelFor`와 Burst가 있다. | 이미 일부 적용. 운송 상태와 Unity 객체 접근을 분리하기 전에 운송 전체를 병렬화하지 않는다. |
| ECS 전환 | 현재 라인 저장소는 이미 아이템의 개별 객체 갱신을 줄이는 데이터 구조다. | 지금의 선행 과제가 아니다. 불필요한 wake와 미지원 구간을 먼저 줄인다. |

주요 코드 근거: [중앙 틱 버킷](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Manager/MapObjectTickManager.cs:238), [생산 기본 간격](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InputOutputModule.cs:62), [라인 경계 예약](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/ConveyorTransportRun.cs:88), [렌더 Burst 작업](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Rendering/ConveyorItemTransformJobProcessor.cs:194).

공유 예제를 그대로 적용하면 안 되는 부분도 있다.

- `ProcessPerFrame = 100` 같은 고정 상한은 전체 계산량을 없애지 않는다. 모든 객체를 순환한다면 60 FPS에서 10만 개를 한 번씩 처리하는 데 약 16.7초가 걸린다. 예시의 인자 없는 `TickAI()`를 운송·생산에 대입하면 처리 지연이나 게임 진행 속도 저하가 생길 수 있다. 실제 경과 시간을 전달하더라도 입출력 경쟁과 FIFO를 보존하는 처리가 별도로 필요하다.
- FPS와 UPS는 서로 다른 값이다. 화면이 부드러워져도 같은 실제 시간 동안 생산량이 줄었다면 이번 프로젝트의 최적화 성공으로 볼 수 없다. 현재 중앙 관리자는 한 객체를 한 렌더 프레임에서 최대 한 번 호출하고 경과 시간을 전달하므로, 고정 UPS를 보장하는 관리자와도 구분해야 한다.
- 예시의 삭제는 `List.IndexOf`를 먼저 호출하므로 전체적으로 O(N)이다. 위치 인덱스를 별도로 유지해야 등록 목록의 O(1) 삭제가 가능하다. 마지막 항목과 교환하는 방법을 순서가 중요한 벨트 아이템 목록에 사용하면 안 된다.
- 각 객체의 `Update` 안에서 0.2초 타이머를 검사하면 Unity 콜백 자체는 매 프레임 발생한다. 이 프로젝트의 기존 중앙 관리와 라인 예약을 사용하는 편이 목적에 맞는다.
- C# 이벤트를 추가하는 것만으로 비용이 줄지는 않는다. 실제 변경, 영향을 받는 포트, 유효한 예약을 특정해야 한다. 많은 구독자에게 반복해서 알리면 검사 비용을 알림 비용으로 옮길 뿐이다.

현재 라인 저장소는 정렬 좌표와 공통 변위를 사용하므로 아이템 사이의 상대 간격을 유지하면서 자유 이동 시 루트 상태만 갱신한다. Factorio가 공개한 연속 벨트 묶기·상대 간격 유지와 목적이 같다. 그러나 Factorio의 간격 배열 및 압축 구간 추적 알고리즘을 그대로 복제한 것은 아니다. 현재는 순서를 유지하는 treap으로 삽입·회수·압축을 처리하며, 이 연산들은 기대 O(log N)이다. [Factorio 공식 FFF-176](https://www.factorio.com/blog/post/fff-176)

0.5초는 모든 라인에 공통으로 적용할 상수가 아니다. 아이템 간격이 0.5이고 속도가 1일 때 연속 통과 주기가 0.5초가 된다. 성긴 라인은 다음 아이템의 실제 도착까지 기다려야 하고, 선두 회수·출구 비움·속도 변경은 예약을 바꿔야 한다. 현재 소유 구간의 예약 처리는 이 방향이다. 다만 [운송 관리자](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.ConveyorTransport.cs:13)는 매 프레임 라인과 구간 목록을 순회한다. 예약 전의 비싼 포트 검사를 생략하는 것이며, 관리자 순회까지 사라진 상태는 아니다.

마지막 제공 실측은 **2026-09-09 05:27:58이며, 최신 경계 시각 예약 적용 전**이다. 현재 코드의 성능 측정값으로 취급하면 안 된다.

| 이전 스냅샷 지표 | 값 | 해석 |
|---|---:|---|
| 로드된 MapObject / 중앙 Update 등록 수 | 5,003 / 82 | 5,003개가 모두 이 관리자를 통해 매 프레임 같은 작업을 하는 구조가 아니다. 다른 Unity 콜백의 부재를 뜻하지는 않는다. |
| 로드된 벨트 아이템 / 라인 소유 아이템 | 582 / 101 | 당시 소유 비율 약 17.4%. 전체 아이템으로 최적화 효과가 확장되지 않았다. |
| 소유 구간 | 17 | 구간당 평균 약 5.9개 아이템. 긴 라인에 많은 아이템이 모인 이상적인 조건과 다르다. |
| 컨베이어 전체 / wake 큐 | 5.833 / 5.397ms | 큐는 전체 컨베이어 시간의 약 92.5%. 부모·자식 시간을 합산하면 안 된다. |
| 라인 운송 관리 | 0.369ms | 이 부분만 줄여서는 당시 큐 병목이 남는다. |
| 기존 이동 시도 / 성공 | 225.322 / 0.119회·프레임 | 불필요한 재검사 조사 근거. 라인 내부 공통 이동은 이 성공 수에 포함되지 않아 전체 운송 성공률로 계산하면 안 된다. |
| 해당 프레임 큐 처리 / 상한 | 219 / 512 | 이 자료에서 상한 초과가 원인은 아니다. 상한을 낮추면 처리 지연만 늘 수 있다. |

자료: [사용자 프로파일 스냅샷](C:/Users/dk601/.codex/attachments/66a2ad7a-175c-46ac-988b-ed695483b186/pasted-text.txt:1). 서로 다른 캡처는 렌더 삼각형·청크 수도 달라 전체 FPS만으로 변경 효과를 비교할 수 없다.

다음 순서로 적용하는 것이 적절하다.

1. **현재 구현의 기준 측정과 wake 재현 범위를 확보한다.** 같은 월드·카메라·운전 상태에서 최신 예약 카운터와 큐 시간을 함께 확인한다. 기존 `TransportBoundaryChecks`, `TransportSleepingRuns`, `TransportRebuilds`를 활용하고, 큐 발생 원인과 소유/기존 경로 비중을 구분한다. 전체 큐의 지연 알림 flush와 실제 sleep/retry 연결을 하네스에 포함한다. 기존 하네스는 이 서비스 일부를 대역으로 사용하므로 FPS나 전체 큐 소멸을 증명하지 않는다.

2. **기존 wake의 의미를 끝까지 보존한다.** [WakeConveyorNetwork](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:2093)는 지연 중 `queueWake`를 저장하지 않고 블록만 기록한다. [FlushDeferredConveyorNetworkWakes](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:631)는 나중에 무조건 `QueueConveyorWake`를 호출한다. 따라서 `queueWake=false`도 큐 등록으로 바뀌는 경로가 있다. 즉시 처리와 지연 처리의 의미를 같게 만들고, 같은 블록에 false/true 요청이 합쳐질 때 true를 보존해야 한다. 코드상 확인된 차이지만 실제 비용 기여도는 아직 측정하지 않았다.

3. **변화 없는 기존 구간의 반복 재검사를 제거한다.** [직접 wake 처리](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:2671)는 활성 여부 검사 전에 주변 흐름의 sleep/throttle을 해제한다. [라인 fallback](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:3344)은 아이템이 있으면 현재 tick 불필요 상태여도 직접 wake를 넣는다. 일부 fallback은 해당 프레임 라인 처리 완료 표시 전에 반환한다. 실제 빈칸·입력 변화, 도착 예약, 필요한 모션 진행과 단순 재시도를 구분해야 한다. 후속 알림을 무조건 버리거나 비활성 블록을 모두 제외하면 재개가 끊길 수 있으므로 원인별로 병합·대기시키는 방식이 필요하다.

4. **같은 속도의 운송 구간 소유 범위를 확대한다.** 현재 [채택 조건](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/Block.ConveyorTransport.cs:11)은 비순환 1F 직선 중심이며, [구간 구성](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.ConveyorTransport.cs:64)은 최소 4개 후보 중 양 끝 2개를 기존 포트로 남긴다. 짧은 구간과 코너가 많으면 기존 처리 비중이 커진다. 먼저 제외 사유와 길이 분포를 측정한다. 이후 단순 곡선도 하나의 경로 거리로 표현해 묶고, 실제 분기·합류·속도 변화·특수 모션은 경계로 유지한다. 현재 `Origin + Step * position`은 직선 전용이므로 코너 지원 조건만 풀어서는 안 된다. 경로 좌표 변환·조회·회수·분할을 함께 바꿔야 한다. 상호작용 경계로 남은 블록의 재채택 조건도 검토한다.

5. **로봇팔의 대기를 입출력 사건에 연결한다.** [ShouldRuntimeSleep](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/RobotArm.cs:680)는 주변에 상호작용 대상이 있으면 대기 중에도 깨어 있을 수 있다. 출력 대상이 컨베이어이면 내려놓기 대기에서도 sleep하지 않는 분기가 있다. 일반 건물 존재 여부보다 집을 아이템의 도착·출력 슬롯의 빈칸을 기준으로 대기/재개하는 편이 좋다. 팔의 실제 이동과 잡고 있는 아이템 상태 갱신은 유지한다. 이전 실측의 로봇팔 비용은 약 0.607ms/프레임으로, wake 큐보다 후순위다.

6. **구간 수가 커질 때 예약 큐와 렌더 무효화를 개선한다.** 현재 관리 루프는 전체 라인/구간 수에 비례한다. 구간이 수천~수만 개로 늘어 이 비용이 커지면 기존 예약 자료구조를 활용해 도착할 구간만 꺼내도록 한다. 취소·분할 후 오래된 예약이 실행되지 않도록 식별자와 세대를 검증한다. 동시에 움직이지 않는 라인을 매 프레임 갱신할 이유를 없애고 위치는 필요할 때 시간에서 계산한다. 렌더는 이미 컬링과 버전 캐시가 있지만 활성 블록 집합 변경 시 전체 목록 복사/캐시 조정이 남으므로 변경분 처리가 다음 후보다. 지금 관측된 17개 구간만으로 heap이나 ECS 전환이 필요하다고 결론 내릴 수는 없다.

검증은 평균 FPS뿐 아니라 같은 실제 시간 동안의 출하량·생산량, 입력부터 출력까지의 지연, FIFO·수량 보존, 빈 라인과 정체 상태의 반복 시도, 프레임 p95/p99, GC를 함께 본다. 빈 라인·밀집 이동·출구 막힘·간헐 공급·코너 연속·분기·로봇팔 회수·구조 변경을 같은 조건에서 비교한다. UI 통계나 중요도가 낮은 캐시 정리는 예산에 따라 분산할 수 있지만, 운송·생산을 고정 개수만 처리하면서 진행 지연을 성능 개선으로 집계하지 않는다.

10만 개의 의미도 구분해야 한다. 몇 개의 긴 라인에 담긴 10만 아이템은 공통 이동의 이점을 크게 받는다. 반면 짧은 라인·경계·상호작용이 많은 10만 설치물이나 화면에 표시되는 10만 GameObject는 다른 비용을 가진다. 현재 구현에서 전체 프레임 시간은 라인/구간 관리, 실제 입출력 사건, 기존 블록 경로, 화면에 보이는 아이템, 나머지 월드 처리의 합이다. 아이템 공통 이동이 O(1)이라는 사실로 프로젝트 전체의 60 FPS를 보장할 수 없다.

현재 구현과 기존 하네스 범위는 [라인 소유권 구현 기록](C:/Git/ProjectF/Research/ObjectScale-2026-09-09/LINE-GAP-IMPLEMENTATION.md:1), [하네스 설명](C:/Git/ProjectF/Tools/ConveyorTransportHarness/README.md:1)을 참고한다. 이번 분석에서는 런타임 코드를 변경하거나 테스트를 재실행하지 않았다.
