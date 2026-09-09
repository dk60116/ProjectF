# 10만 오브젝트 규모 성능 분석

2026-09-09 · 기준 커밋 `b8aa85a` · Unity 6000.4.0f1 / URP 17.4.0

현재 코드로 **10만 개의 시설물이 계속 작동하면서 안정적인 30~60FPS를 유지한다고 기대하기는 어렵다.** 로봇팔 비중이 높으면 한 자릿수 FPS 이하까지 떨어질 위험이 있다. 반면 정적인 오브젝트가 대부분이고 보이는 수와 활성 시뮬레이션 수가 작다면 같은 총개수에서도 훨씬 유리하다. 정확한 FPS 범위는 현재 빌드의 규모별 실측 없이 확정할 수 없다.

엔진·게임·프로파일러를 실행하거나 조작하지 않았다. 현재 소스와 기존 측정 파일을 읽었으며, 아래 숫자는 과거 실측 / 코드로 확정되는 작업량 / 가정에 따른 계산을 구분했다. 이 문서 외 게임 코드와 설정은 변경하지 않았다.

## 1. 먼저 구분해야 할 개수

- Unity GameObject 수: 프리팹의 자식, 시각 파트, 보조 호스트까지 포함한다.
- MapObject 수: 자원·설치물 등 게임의 논리적 배치 대상 수다.
- 활성 틱 대상 수: 현재 실제로 갱신되는 시설물 수다. 수면 대상은 틱 등록에서 제외될 수 있다.
- 벨트 위 아이템 수: 가상 아이템 데이터 경로가 있어 항상 같은 수의 GameObject를 의미하지 않는다.
- 화면에 보이는 인스턴스 수: 렌더링 비용을 판단하는 별도 축이다.

2026-09-07 최초 스냅샷은 MapObject 3,662개에 그 계층의 Transform 14,082개를 보고한다. 벨트만 보면 310개에 Transform 5,947개다. 이 카운터는 해당 계층을 순회한 수이며 전체 씬의 고유 GameObject 총수는 아니다. 같은 구성의 단순 비율은 MapObject당 약 3.85개, 벨트당 약 19.18개다. 따라서 **시설물 10만 개와 자식 포함 GameObject 10만 개는 전혀 다른 부하**다. 같은 구성 비율을 유지한다는 가정에서 MapObject 10만 개는 계층 Transform 약 38.5만 개에 해당하지만 실제 프리팹 구성에 따라 크게 달라진다.

아래 시설물 관련 분석은 별도 표시가 없으면 로드된 논리적 오브젝트 수를 기준으로 한다.

## 2. 저장소에 있는 실측 기준선

현재 PC의 읽기 전용 하드웨어 조회는 AMD Ryzen Z1 Extreme 8코어/16스레드와 AMD Radeon Graphics다. 기존 보고서의 CPU/GPU 명칭과 일치하지만 전력 제한, 온도, 백그라운드 부하는 현재 측정하지 않았다.

2026-09-07 실행 빌드, 1920×1080 기준:

| 조건/항목 | 과거 측정값 |
|---|---:|
| 기본 2차 평균 FPS / FrameMs | 79.64 / 12.596ms |
| 측정 끔 평균 FPS / FrameMs | 82.79 / 12.15ms |
| 카메라 컬링 끔 | 58.73FPS / 17.07ms |
| 로드된 MapObject / 설치물 / 벨트 | 3,662 / 501 / 310 |
| 벨트 처리 전체 | 4.2933ms/프레임 |
| 그 내부 wake queue 처리 | 4.2479ms/프레임 |
| 로봇팔 Update | 0.9784ms/프레임, 27.47호출/프레임 |
| 가상 벨트 렌더 처리 | 0.471ms/프레임 |

벨트 전체와 내부 큐 시간을 합산하면 안 된다. status의 설치물 463개와 World의 501개는 집계 정의가 달라 혼용하지 않는다. 유효한 GPU 시간과 개별 프레임 p95/p99는 없다.

이 측정 이후 리스트·통계 객체 재사용, 로봇팔 렌더 조회 중복 제거, 정지 아이템 배치 재구축 생략 등이 적용됐다. 따라서 79.64FPS는 **현재 소스 실측이 아니다.** 전체 FrameMs를 `100000 / 3662`배 하는 계산은 고정 비용, 활성 비율, 가시성, 큐 상한을 무시하므로 사용하지 않는다.

근거: [기존 보고서](<C:/Git/ProjectF/Research/FrameOptimization-2026-09-07/REPORT.md>), [수치 요약](<C:/Git/ProjectF/Research/FrameOptimization-2026-09-07/summary.json>), [후속 구현](<C:/Git/ProjectF/Research/FrameOptimization-2026-09-07/IMPLEMENTATION.md>).

## 3. 현재 구조의 주요 병목

### 활성 로봇팔: 매 프레임 시뮬레이션과 전체 렌더 준비

[RobotArm.cs:159](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/RobotArm.cs:159>)의 틱 간격은 0.001초다. [MapObjectTickManager.cs:239](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Manager/MapObjectTickManager.cs:239>)는 프레임마다 버킷 전체 개수 이하로 실행하므로 보통의 FPS에서는 깨어 있는 팔당 프레임에 한 번 호출된다. 1초에 1,000번씩 따라잡는 구조는 아니다. 60FPS에서 활성 팔 10만 개는 초당 약 600만 회의 ManagedUpdateTick 호출이다.

주변 상호작용 대상이 있거나 들고 있는 아이템의 목적지가 벨트인 경우 수면하지 않는 경로가 있다([RobotArm.cs:680](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/RobotArm.cs:680>)). 팔이 움직이지 않는다고 항상 틱이 사라지는 것은 아니다. 주변 wake 조회는 좌표별 인덱스를 사용하므로 모든 팔을 전역 검색한다고 해석하지 않는다.

[RobotArmRenderBatcher.cs:66](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/RobotArmRenderBatcher.cs:66>)는 매 LateUpdate 배치 행렬을 비우고 등록된 팔 전체의 파트 데이터를 다시 추가한다. 카메라 컬링은 이후 배치 렌더 단계다. 따라서 화면 밖 팔도 이 준비 비용을 유발할 수 있다. 파트 행렬의 재질별 중복 조회는 이미 제거됐지만 전체 팔 순회는 남아 있다.

과거 inclusive 평균은 호출당 약 35.6µs다. 여기에는 프레임 최초 전력망 평가와 당시 상태·진단 비용이 섞일 수 있어 순수 팔 비용으로 단정하지 않는다. 단지 같은 평균이 유지된다는 가정이면 활성 팔 1,000개만으로 약 35.6ms가 되어 다른 비용을 제외해도 약 28FPS가 상한이다. 이것은 외삽 예시이며 현재 빌드의 1,000개/10만 개 실측 결과가 아니다.

### 생산기: 10Hz 분산은 있지만 시간 예산 보장은 없음

[InputOutputModule.cs:62](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InputOutputModule.cs:62>)의 기본 간격은 0.1초다. 모두 깨어 있는 기본 생산기 10만 개를 60FPS로 갱신한다면 평균 약 16,667회/프레임, 초당 약 100만 회다. 개별 파생형은 간격과 작업량이 다를 수 있다.

틱 관리자는 오브젝트 개수와 경과 시간으로 분량을 정하고, 전체 틱에 대한 ms 상한을 두지는 않는다. 제작·유체·전력 처리의 평균 비용이 1µs만 되어도 60FPS 기준 시뮬레이션만 약 16.7ms다. 5µs면 목표 10Hz를 유지하는 데 초당 약 5초의 단일 스레드 시간이 필요해 한 코어에서 처리할 수 없다. 저FPS에서는 프레임당 객체당 한 번 제한 때문에 실제 호출 빈도가 목표보다 낮아질 수 있다. 타이머에 경과 시간을 전달하는 것과 모든 제작/운반 이벤트의 처리량을 보존하는 것은 별도로 검증해야 한다.

수면한 생산기는 틱 등록을 해제하므로 총개수보다 활성 개수가 중요하다.

### 컨베이어: FPS와 운반 지연을 함께 봐야 함

[TerrainGenerator.Conveyors.cs:3368](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:3368>)는 큐 처리량을 최대 512건으로 제한한다. GameScene 직렬화 값이 4,096이어도 유효 상한은 512다. 일반 큐 처리 루프는 시작 시점 큐 크기와 이 상한까지 처리한다([같은 파일:2534](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:2534>)).

서로 병합되지 않는 독립 wake 10만 건이 한꺼번에 들어오고 추가 이벤트도 없다면 최소 196프레임, 60FPS에서도 약 3.27초가 필요하다. **wake 1건은 아이템 1개와 같지 않다.** 직선 라인·코너 그룹 하나가 여러 블록을 처리할 수 있으므로 이 수치는 실제 아이템 처리율이 아니다.

상한은 wall-clock 시간 예산도 아니다. 한 라인 안의 조사량에 따라 512건 미만에서도 비용이 커질 수 있다. 완료시각 처리 경로는 `max(유효 wake 상한, 활성 motion 블록 수)`를 사용하므로 모든 컨베이어 작업이 512개로 제한되는 것도 아니다([같은 파일:2233](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:2233>)).

안전 스캔은 0.25초 간격이며 활성 벨트 수의 약 1/16을 조사한다. 활성 벨트 10만 개면 스캔 시 약 6,250개를 순회한다([같은 파일:2942](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:2942>)). 전체 FPS가 유지되더라도 큐 적체, 후속 투입 지연, 벨트 처리량 저하가 생기는지 봐야 한다.

### 화면 밖 시각 갱신과 전력망에도 전체 순회가 남음

[WorldVisualUpdateManager.cs:49](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Rendering/WorldVisualUpdateManager.cs:49>)는 등록된 설치물 전체를 순회한다. 각 [InstallationVisualState.Tick](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Rendering/InstallationVisualState.cs:38>)은 Transform 행렬을 읽고 Bounds를 검사한 뒤 화면 안 대상만 시각 갱신한다. Animator·파티클 생략은 이미 적용됐지만 비가시 대상의 검사 비용까지 사라지지는 않는다.

[UtilityPole.cs:3005](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/UtilityPole.cs:3005>)는 필요 시 프레임당 한 번 전력망을 평가한다. 망별 소비 설비를 순회해 수요를 계산하고, [소비자 매핑](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/UtilityPole.cs:3091>)도 전체 재작성한다. 같은 설비가 여러 망에 포함되면 구성에 따라 반복 방문한다. 모든 소비자가 매번 전력망 전체를 계산하는 O(N²) 경로라고 해석하지는 않는다. 전봇대 개별 LateUpdate의 dirty flush 호출도 남아 있다.

### 철도·저장·탐험: 순간 끊김과 누적 부하

- [SteamTrain.cs:4650](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Vehicle/SteamTrain.cs:4650>)의 경로 기본 그래프 구축은 레일 모든 쌍을 검사한다. 유효 레일 1만 개면 49,995,000쌍, 10만 개면 4,999,950,000쌍이다. 매 프레임 실행은 아니며 초기 요청 또는 레일/역 변경으로 dirty가 된 뒤 경로 조회 시 재구축된다. 대규모 철도에서는 평균 FPS보다 해당 프레임의 긴 멈춤이 먼저 문제가 될 수 있다.
- [SaveManager.cs:103](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Manager/SaveManager.cs:103>)는 상태 수집과 파일 쓰기를 동기 호출한다. 10만 개 상태 저장은 저장 요청 시 끊김 후보지만 현재 소스만으로 정지 시간을 정할 수 없다.
- [TerrainGenerator.cs:2374](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.cs:2374>)의 일반 이동 청크 갱신은 새로운 청크를 추가하며 거리 기반 제거 단계가 없다. 확인한 로딩 경로에서는 멀어졌다는 이유만으로 상주 데이터가 줄지 않는다. 화면 컬링과 별개로 탐험에 따라 메모리와 전역 순회 대상이 누적될 수 있다.
- [AppendRuntimeProfilerCounters](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:4427>)는 월드를 조사하고 객체별 Transform·Renderer·Collider 계층을 조회한다. 10만 개에서 대략 30만 번의 계층 조회가 될 수 있다. 호출은 진단 요청에 따른 것으로 매 프레임이라고 볼 수 없으며 scratch List 사용을 같은 횟수의 배열 할당으로 해석하지 않는다. 이 작업 때문에 측정 자체가 프레임을 흔들 수 있다.

## 4. 이미 적용된 대규모 처리 장점

- 중앙 틱 관리자, 생산기 0.1초 분산, 수면/좌표별 wake.
- 컨베이어 라인 처리, 완료시각 기반 motion 관리, 가상 아이템 데이터와 렌더 캐시.
- 벨트 Transform 접근 전 공간 셀 컬링.
- BatchRendererGroup 경로와 RenderMeshInstanced fallback, 데이터 버전에 따른 업로드 관리.
- 벨트 아이템 행렬 계산용 Burst/Jobs와 영속 NativeArray. 기본 설정상 128개 이상에서 Job 사용을 시도한다. 시뮬레이션 전체가 병렬화된 것은 아니다.
- 화면 밖 설치물 Animator·파티클 제어, 일부 정지 렌더 배치 재사용.

따라서 이 프로젝트를 '모든 오브젝트가 개별 Update를 갖는 구조'로 진단하면 틀린다. 남은 문제는 활성 시뮬레이션 작업량, 전체 순회, 갱신 그래프의 복잡도, 상주 네이티브 계층이다.

GPU 인스턴싱은 같은 메시의 렌더 제출을 줄여 주지만 팔/생산 로직 비용의 측정값을 대신하지 않는다. RenderMeshInstanced는 호출 내 인스턴스를 그룹 Bounds로 컬링·정렬하므로 공간 배치가 중요하며, 10만 오브젝트가 곧 10만 드로콜인 것도 아니다. [Unity 6.4 공식 API](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Graphics.RenderMeshInstanced.html).

## 5. FPS를 숫자로 해석하는 방법

60FPS 예산은 16.67ms, 30FPS 예산은 33.33ms다. 10만 대상에 매 프레임 반드시 수행하는 메인 스레드 작업 하나가 있다고 가정하면:

| 대상당 평균 비용: 가정값 | 10만 개 처리 시간 | 이 작업만 고려한 FPS 상한 |
|---:|---:|---:|
| 0.1µs | 10ms | 100FPS |
| 0.5µs | 50ms | 20FPS |
| 1µs | 100ms | 10FPS |
| 5µs | 500ms | 2FPS |

이는 **실측 FPS 예측표가 아니라 비용 민감도 계산**이다. 나머지 CPU 작업과 GPU/대기가 추가되면 실제 FPS는 더 낮아질 수 있다. 생산기 10Hz처럼 분산되는 대상에는 프레임당 실제 호출 수를 넣어야 한다.

60FPS에서 다른 시스템에 10ms를 쓴다는 가정이면 남는 6.67ms를 10만 개로 나눈 값은 대상당 66.7ns다. 현재의 복합 시설물 로직 전체를 이 예산 안에서 프레임마다 실행하는 목표는 현실적이지 않다. 다만 10ms는 현재 빌드에서 분리 측정한 고정 비용이 아니다.

| 10만 개의 구성 | 현재 코드로 내릴 수 있는 판단 |
|---|---|
| 자식 포함 GameObject 총 10만, 정적 비중 높음 | 총개수만으로 FPS 산정 불가. 가시 수·Renderer·Collider·메모리 확인 필요 |
| 정적 MapObject 중심, 활성 시설은 적고 화면 일부만 표시 | 다른 구성보다 유리. 메모리 누적과 남은 전역 검사 때문에 60FPS 보장은 불가 |
| 가상 벨트 아이템 10만, 화면 일부만 표시 | 네이티브 아이템 10만보다 유리한 구조. 활성 라인·정체·wake 처리량을 별도 검증 |
| 실제 작동하는 생산기·로봇팔 등 시설물 10만 | 안정적 30~60FPS를 기대하기 어려움. 로봇팔 비중이 높으면 한 자릿수 FPS 이하 위험 |
| 화면 안 복잡한 모델 10만 | 메시/재질/그림자/GPU 측정이 없어 수치 확정 불가. CPU 최적화만으로 보장 불가 |
| 대규모 레일 변경 후 경로 재계산 | O(R²) 쌍 검사 때문에 긴 순간 멈춤 위험이 명확함 |

## 6. 권장 개선과 실제 검증 순서

1. **규모별 하네스:** 1천/1만/3만/10만 단계, 논리 오브젝트·네이티브 계층·활성 틱·가시 인스턴스를 별도 기록한다. 고정 카메라와 이동 카메라, 수면/작동/정체/정체 해제 상태를 분리한다.
2. **로봇팔:** 배치 준비 전에 공간 컬링, 고정 파트/정지 자세 캐시, 논리 회전 상태와 시각 Transform 의존 분리. 이벤트 기반 대기를 확장하되 집기·놓기 완료 시점과 수량 보존을 유지한다.
3. **컨베이어:** 중복 wake의 실제 비중과 큐 대기 시간을 계측하고 라인 단위 작업을 줄인다. 상한만 줄여 FPS를 올리거나 상한만 늘려 적체를 없애는 변경은 피한다. 품목 수량·순서·속도와 막힘 해제 후 재개 지연을 함께 검증한다.
4. **전력/시각 관리자:** 수요 변화분 반영, 소비자 매핑 무효화 범위 축소, 정적 Transform의 변경 기반 캐시와 공간 셀 관리.
5. **철도:** 기존 좌표/접속 정보로 인접 후보를 제한하고 변경 주변 그래프만 갱신한다. 모든 레일 쌍 검사를 없애는 것이 큰 규모의 우선 과제다.
6. **장기 상주/저장:** 시뮬레이션 데이터를 유지하면서 원거리 시각/물리 객체를 회수하는 경계 검토. 저장은 일관된 상태 스냅샷과 파일 쓰기 단계를 분리할 수 있는지 확인한다.

정식 결과에는 평균 FPS뿐 아니라 개별 프레임 p95/p99, 16.67/33.33ms 초과 비율, GPU 시간, GC 할당/수집, 활성 큐 길이와 최장 대기 시간, 실제 생산량·운반량을 포함해야 한다. 무거운 월드 카운터 수집은 안정 구간 밖에서 하고, 플레이어 빌드의 진단 켬/끔을 나눠 비교한다. 이미 있는 하네스는 동작 회귀 검증에 활용하되 엔진 전체 FPS의 대체 측정으로 사용하지 않는다.

이번 작업은 정적 분석이다. 현재 빌드의 FPS·메모리·GPU 시간·규모별 처리량을 새로 측정하거나 테스트하지 않았다.
