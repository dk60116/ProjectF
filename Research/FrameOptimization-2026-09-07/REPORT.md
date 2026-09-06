# 실행 빌드 프레임 최적화 조사 — 2026-09-07

현재 장면에서는 **카메라 컬링을 유지하면서, 정체 벨트의 중복 깨우기와 재시도를 먼저 줄이는 것**이 가장 근거가 강하다. 벨트 처리는 약 4.2–4.3ms/프레임이며 대부분 wake queue 처리다. 벨트 모델 또는 벨트 아이템을 숨기는 것으로는 큰 개선이 재현되지 않았다. 카메라 컬링을 끄면 약 79FPS에서 59FPS로 내려가고, 복원하면 78FPS로 돌아왔다.

게임 런타임 소스나 빌드는 수정하지 않았다. 이미 실행 중인 빌드와 MapObjectProfiler를 읽고 기존 진단 토글로 비교했다. 변경한 토글과 프로파일러 갱신 간격은 원래 값으로 복원했다. 새 게임/Unity 프로세스 실행, 세이브·로드·아이템 생성은 하지 않았다.

## 실행 환경과 근거

- 게임: `C:\Git\ProjectF\Build_PC\FactorioProject.exe`, PID 701200.
- 프로파일러: `C:\Git\ProjectF\Build_PC\Tools\MapObjectProfiler\MapObjectProfiler.exe`, PID 703204.
- AMD Ryzen Z1 Extreme, 8코어/16스레드, AMD Radeon 내장 GPU, 1920×1080.
- Player.log: Unity 6000.4.0f1, 실제 그래픽 API Direct3D 11, GPU Resident Drawer 생성.
- IL2CPP `GameAssembly.dll` 수정 시각: 2026-09-07 01:10:44. Unity 에디터와 다른 앱도 실행 중인 실제 PC 환경이다.
- `TargetFrameRate=-1`, `VSyncCount=0`, 화면 주사율 59.94Hz. 따라서 표시 FPS는 60으로 제한되지 않았다.
- 빌드 백업 Portable PDB의 SHA256과 현재 핵심 소스 **8/8개 일치**. 결과는 `build-source-verification.json`, 재현은 `Verify-BuildSources.ps1`.
- 수집: MapObjectProfiler와 동일한 `127.0.0.1:50877`의 `status` 및 `perf 256`. 원본은 각 `*.jsonl`, 최초 탐색은 `initial.json`.
- 주 비교 구간의 status 설치물 수는 463개, 벨트 아이템 468–495개. 별도 World 카운터는 로드된 MapObject 3,662개, 벨트 310개, 설치물 501개, 청크 494개로 보고한다. 서로 다른 집계 경로이므로 두 설치물 숫자를 같은 정의로 합치지 않았다.

## 비교 결과

각 행은 3초 안정화 후 약 1초 간격으로 25회 읽은 결과다. `status`의 FPS/FrameMs 자체는 약 0.5초 평균이다. 아래 최저 FPS는 **샘플링된 평균 중 최저**이며 개별 프레임 최저·1% low·p99가 아니다.

| 조건 | 평균 FPS | 평균 FrameMs | 샘플 최저 FPS |
|---|---:|---:|---:|
| 기본, 측정 켬 1차 | 78.72 | 12.77 | 64.8 |
| 측정 끔 | 82.79 | 12.15 | 65.4 |
| 기본, 측정 켬 2차 | 79.64 | 12.60 | 69.3 |
| 벨트 모델 숨김 | 81.48 | 12.34 | 68.9 |
| 벨트 아이템 숨김 | 76.92 | 13.07 | 63.9 |
| 카메라 컬링 끔 | 58.73 | 17.07 | 52.4 |
| 기본 설정 복원 | 78.33 | 12.81 | 68.8 |

모든 주 비교 샘플에서 playerSpeed=0이었다. 시뮬레이션은 계속 진행하여 아이템 수와 시간대가 변했고, 낮에서 밤으로 전환되었다. 무작위 순서의 반복 벤치마크가 아니므로 작은 FPS 차이를 기능의 확정 효과로 해석하면 안 된다. 특히 벨트 아이템 숨김의 낮은 평균은 숨기기가 성능을 악화시킨다는 증거가 아니다. 반면 컬링 끔→복원의 큰 차이와 렌더 카운터 변화는 방향이 일치한다.

| 렌더/CPU 카운터 | 기본 2차 | 컬링 끔 | 복원 |
|---|---:|---:|---:|
| MainThreadMs, 최근 프레임 평균 | 12.99 | 17.84 | 13.21 |
| SetPassCalls | 141.44 | 387.08 | 140.71 |
| Triangles | 약 144만 | 약 443만 | 약 149만 |
| RenderedChunkSurfaces | 28 | 494 | 28 |
| 벨트 기어 Transform 읽기/프레임 | 778 | 1,282 | 778 |
| 해당 Transform 갱신/프레임 | 0 | 0 | 0 |

카메라 컬링은 현재 유효하다. GPU 시간은 측정되지 않았으므로 위 변화 전체를 GPU 절감 또는 CPU 절감 하나로 단정하지 않는다.

추가로 UI 갱신 간격을 10초로 바꾼 30샘플은 평균 75.19FPS였다. 이 구간은 프로파일러 창을 전면에 둔 뒤이며 시간도 달라졌으므로, 앞의 게임 화면 조건과 직접 비교하여 갱신 간격의 효과를 결론 내리지 않았다. 원본은 `profiler-ui-10s.jsonl`에 보존했다. 최종 갱신 간격은 1,000ms로 복원했다.

## 벨트 처리: 가장 큰 측정된 개선 후보

기본 2차의 유효 스냅샷 5개, 합계 277프레임으로 정규화했다. 복원 구간에서도 비슷했다.

| 범위 | ms/프레임 | 호출/프레임 |
|---|---:|---:|
| ActiveConveyor 전체 | 4.293 | 1 |
| 내부 ProcessWakeQueue | 4.248 | 1 |
| 내부 WakeLine | 1.980 | 92.30 |
| 내부 WakeBlock | 1.413 | 41.76 |
| 내부 WakeCornerGroup | 0.821 | 34.26 |
| RobotArm Update 별도 | 0.978 | 27.47 |
| Virtual Belt Render 별도 | 0.471 | 1 |

**부모·자식 시간이 중복되어 있으므로 표 전체를 합산하지 않는다.** 벨트 전체가 약 4.3ms이고, 이 중 약 4.25ms가 큐 처리다. 일반 이동 시도는 프레임당 237.56회, 성공은 0.144회였다. 직선 fast path는 별도 카운터이며 시도/성공 각각 0.206회였다. 이 수치는 모든 아이템 이동을 합한 처리량이 아니다.

코드와 실측이 맞아떨어지는 경로:

1. **이미 처리한 라인이 같은 프레임에 재삽입된다.** `TerrainGenerator.Conveyors.cs:2557`에서 dequeue 직후 range를 제거하고, 같은 라인은 `:3024`의 소비 단계에서야 중복 확인 후 defer된다. 초기 50프레임에서 WakeLine 4,784회 대비 실제 RetryWork 852회로, 약 82%가 내부 조사 전에 빠졌다. 모든 조기 반환이 중복 분기라는 확정은 추가 카운터가 필요하다. enqueue 단계에서 이미 처리한 라인을 바로 deferred range에 병합하는 것이 우선 후보다.
2. **NoMove fallback이 sleep/retry를 다시 해제한다.** `Block.cs:4459`에서 motion state만 있어도 retry work가 될 수 있다. `TerrainGenerator.Conveyors.cs:3123`의 no-move 후 direct fallback, `:3334`의 `ShouldTickActiveConveyor || itemCount > 0`, `:785`의 retry 삭제, `:2672`→`Block.cs:4755`의 주변 sleep 해제가 연결된다. 단순히 아이템이 존재한다는 이유로 대기 슬롯을 매 프레임 깨우는 경로를 줄여야 한다. 라이브 callstack으로 그 비중을 측정한 것은 아니다.
3. **코너 그룹 리스트가 지속 할당된다.** `TerrainGenerator.Conveyors.cs:817`에서 새 List, `:2736`에서 dictionary 제거, `:2755`에서 Clear만 수행한다. 리스트/배열을 재사용하면 동작 변경 없이 할당을 줄일 수 있다. 처리 중 다시 들어오는 wake를 잃지 않도록 처리 버퍼와 대기 버퍼 소유권을 구분해야 한다.
4. **batch 경로가 queueWake=false를 보존하지 않는다.** `TerrainGenerator.Conveyors.cs:2095`에서 deferred set으로 넘길 때 bool이 사라지고 `:664`의 flush에서 무조건 QueueConveyorWake한다. 이 경로는 중복 깨우기 후보지만 초기 스냅샷에서 vacancy wake는 0이어서 현재 4ms의 주원인으로 단정하지 않는다.

개선 시 고정 지연을 새로 추가하는 대신 기존 완료 시각/대기자 알림을 재사용해야 한다. `Block.cs:8636`의 완료 시각 계산과 `TerrainGenerator.Conveyors.cs:2233`의 due-time heap이 이미 있다. 빈칸 생성, 로봇팔 착지 완료, 토폴로지 변경은 즉시 깨워야 한다.

## 로봇팔·렌더 캐시: 추가 계측 후 개선

- **정지한 손 아이템도 전역 dirty를 올린다.** `RobotArm.cs:2586`→`PortableObject.cs:262`→`PortableItemRenderer.cs:333`, 이어서 `:366/:412`에서 등록된 실물 아이템 전체를 재구축한다. 움직이는 손 아이템은 별도 dynamic 목록으로, 나머지는 개별 dirty 갱신으로 분리하는 것이 적절하다. 이 실물 렌더 분기는 현재 MapObjectProfiler의 Conveyor Item 행과 다르며 별도 비용이 누락되어 있다.
- **로봇팔 대기 중 매 프레임 탐색.** `RobotArm.cs:167`의 0.001초 tick, `:724`의 주변 3×3에 MapObject가 있으면 수면 금지, `:696`의 벨트 출구 대기 시 수면 금지 때문에 정체 구간에서 계속 실행될 수 있다. tick 내 후보 탐색 결과 공유와 입출력 변화 이벤트를 사용하고, 실제 팔 이동 구간은 프레임별 갱신을 유지한다.
- **고정 벨트 기어의 Transform 비교.** `ConveyorBelt.cs:2015`의 Gear/Gears 추적과 `VirtualConveyorBeltRenderer.cs:629`에서 현재 778회 읽기/0회 갱신이다. 실제 애니메이션이 작동하는 기어만 추적하고 고정 기어는 연결·배치 변경 시 갱신하는 후보다. Virtual Belt Render 0.47ms 전체가 사라진다는 뜻은 아니다.
- **로봇팔 렌더 배치 전체 재구축.** `RobotArmRenderBatcher.cs:66`에서 매 LateUpdate 전체 배치를 비우고 팔을 다시 추가하며 컬링은 이후에 적용된다. 고정 받침대·정지 자세 캐시와 준비 단계의 공간 컬링을 검토한다. 이 시간도 MapObjectProfiler 별도 행으로 드러나지 않는다.
- **전력망 비용의 귀속 주의.** `RobotArm.cs:2449`→`UtilityPole.cs:520/:3005`에서 프레임 최초 소비자가 전력망 전체 갱신 비용을 부담할 수 있다. RobotArm의 0.98ms나 순간 최대값을 팔의 탐색 비용만으로 해석하지 않는다.

## 프로파일러를 먼저 가볍게 만들 필요

`GameManager.cs:1974`의 perf 수집에서 `TerrainGenerator.Conveyors.cs:4437`은 로드된 블록 전체를 순회한다. 각 MapObject에 대해 `:4790/:4816/:4850`에서 Transform·Renderer·Collider를 각각 `GetComponentsInChildren`로 조사한다. 3,662개 MapObject이면 대략 1.1만 번의 계층 탐색 호출에 해당한다. 이 진단 작업은 메인 스레드에서 실행되지만 자신의 비용은 Rows에 별도로 보이지 않는다. List scratch를 사용하므로 검색 횟수 자체를 같은 횟수의 배열 할당으로 해석해서는 안 된다.

또한 `MapObjectTickManager.cs:207/:662/:1028`은 Measure가 켜져 있으면 매 프레임 활성 그룹을 다시 집계하고 GroupStats 객체를 생성한다. descriptor 캐시, JSON/Base64 응답 생성, 세부 Stopwatch 기록에도 비용이 있다. 측정 끔 구간은 약 0.45–0.62ms의 평균 FrameMs 차이를 보였지만, UI의 perf 수집도 함께 멈추므로 각 원인의 독립 비용은 아니다. ProfilerRecorder는 Measure off에서도 남아 있어 완전히 진단 없는 빌드와 같은 조건도 아니다.

권장 개선은 빠른 프레임/큐 통계와 무거운 월드 개수 조사를 별도 요청으로 나누기, 정적 컴포넌트 개수 캐시, GroupStats 재사용, `Diagnostics.Collect` 자체 계측이다. 실제 프레임별 시간 히스토그램을 추가하여 p95/p99와 16.67ms 초과 프레임 비율을 기록해야 한다.

## 계측 한계와 해석 규칙

- perf는 조회 후 전역 누적 통계를 reset한다. 기본 UI 1초 폴링과 조사 스크립트의 5초 perf가 측정창을 나눠 가진다. 원본의 WindowMs와 BeltLoopProfileFrames를 사용했고 10프레임 미만 구간은 집계에서 제외했다. 전체 25초를 연속 CPU trace로 수집한 것은 아니다.
- Rows는 누적 inclusive 시간이다. AvgUs는 호출 평균, MaxUs는 단일 호출 최대다. 프레임 평균으로 정규화하지 않은 수백 ms를 한 프레임 비용으로 읽으면 안 된다.
- ProfilerRecorder는 최대 128프레임 평균이고 status는 약 0.5초 평균이므로 둘의 FPS/시간값은 정확히 같은 창이 아니다.
- FrameTiming Samples=0, GPU recorder는 유효 핸들이지만 평균/last가 모두 0이다. **유효한 GPU 프레임 시간은 확보하지 못했다.** 현재 ProjectSettings의 enableFrameTimingStats는 0이며 이 설정 파일에는 기존 사용자 변경도 있다. 해당 빌드의 설정 동일성까지 PDB로 검증되는 것은 아니다.
- 빠진 recorder 6개: RenderThreadMs, CameraRenderMs, RenderLoopDrawMs, DrawCalls, Batches, GcAllocFrameKB. 정확한 드로콜·할당량·렌더 스레드 시간을 이번 결과로 제시할 수 없다.
- 초기 Windows GPU 엔진 카운터는 게임 3D 사용률 약 50%였다. 단발 측정이며 GPU 병목 부정 근거로 사용하지 않는다.
- Gen0/1/2 카운터가 동일하게 증가한 것을 합산해 GC 3회로 해석하지 않는다. GC 시간 0도 할당이나 수집 비용이 없다는 보장은 아니다.
- 최초 화면과 후속 화면 위치가 달랐다. 주요 25초 비교들에서는 이동 속도 0, 설치물 수 일정, 컬링 켬의 청크 표시 28개가 일치했지만 카메라 행렬을 매 샘플 기록하지 않았다. 카메라가 완전히 동일한 결정적 재현으로 주장하지 않는다.

## 다음 구현과 검증 순서

1. **측정 보강:** Diagnostics.Collect, 전력망 평가, RobotArmRenderBatcher, 실물 PortableObject Rebuild를 독립 계측. UI는 ms/frame과 부모·자식 관계를 표시하고 무거운 컴포넌트 집계를 분리한다.
2. **낮은 동작 위험부터:** 같은 프레임 line wake의 enqueue 병합, 코너 그룹 리스트 재사용, batch의 queueWake 의미 보존. 기존 하네스에 실제 scheduler 프레임 순서 검증을 보완한다.
3. **대기 처리 정리:** 진행 완료 시각 대기와 목적지 점유 대기를 구분하고, 정체된 범위의 itemCount 기반 전체 fallback을 줄인다.
4. **렌더 캐시 정리:** 손 아이템 개별 dirty와 동적 분리, 고정 기어 추적 제외, 로봇팔 정지/화면 밖 파트 준비 비용 줄이기.

검증은 포화된 직선·코너·T자·2F·분배기·순환 벨트를 10초 이상 정체시키고 wake/할당이 안정되는지 확인한 뒤, 중간 아이템 하나 제거 및 로봇팔 투입 직후 다음 simulation tick에서 흐름이 재개되는지 검사한다. 서로 다른 벨트 속도, 순서·수량 보존, 겹침/순간이동/간격 벌어짐, 청크 언로드/재로드도 포함해야 한다. 평균 FPS만 올리고 과거의 “앞이 비었는데 멈춤”을 재발시키는 변경은 통과시키지 않는다.

개선 후 절감 ms는 이번 조사로 확정하지 않았다. 우선 동일 세이브·같은 카메라·같은 시간·같은 앱 전면 상태에서 반복 측정하고, 일반 빌드의 프레임 분포와 동작 회귀를 함께 확인하는 것이 완료 기준이다.

## 산출물과 복원 상태

- `Capture-Live.ps1`: 기존 localhost 진단 프로토콜 수집기. Before/After 토글을 사용할 때 After는 finally에서 실행한다. UI와 동시 perf는 reset 간섭이 있으므로 후속 정식 벤치마크에서는 수집자를 하나로 제한한다.
- `Summarize.ps1`, `summary.json`: 샘플별 FPS 및 프레임 수로 가중한 inclusive 범위 분석.
- `Verify-BuildSources.ps1`, `build-source-verification.json`: 실제 빌드 PDB의 원본 SHA256 비교.
- `initial.json`, 조건별 `*.jsonl`, `*-initial-status.txt`: 원본 측정 데이터.
- 최종: hideBelts=0, hideBeltItems=0, disableCameraCulling=0, mapObjectTickProfiling=1, UI Interval=1000ms. 게임 화면으로 복귀했다.
