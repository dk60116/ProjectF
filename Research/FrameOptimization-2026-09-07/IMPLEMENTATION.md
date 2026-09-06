# 동작 보존 최적화 적용 — 2026-09-07

기존 실행 빌드 조사에 이어 소스 최적화를 적용했다. 벨트 아이템의 이동 시점과 시설물 동작을 유지하기 위해 큐 처리 순서, 프레임 처리 예산, retry 시간, sleep/wake 조건은 변경하지 않았다. 새 빌드 실행이나 Unity 조작은 하지 않았다.

## 적용 내용

- `TerrainGenerator.Conveyors.cs`, `TerrainGenerator.cs`: 코너 wake 수거용 List를 재사용한다. 수거 중인 버퍼는 풀에 반환하지 않으며, 풀은 최대 256개다. 직선 wake의 연속된 retry 조회를 한 함수로 합쳤다. Queue와 Defer의 만료 상태 처리 차이는 유지했다.
- `MapObjectTickManager.cs`: 프로파일러 통계 객체를 재사용하고 Update 그룹 키와 해시를 캐시한다. 활성 대상 HashSet 순회 중 박싱을 제거했다. 실제 틱 스케줄러 부분은 변경 전과 동일하다.
- `RobotArm.cs`: 입력 필터 Predicate를 인스턴스별로 재사용한다. 필터 판정은 매번 현재 설정을 읽는다. 파트 행렬을 재질마다 반복 조회하지 않는다.
- `PortableItemRenderer.cs`, `PortableObject.cs`: 로봇팔의 반복 요청은 원래 LateUpdate 시점에 전체 등록 아이템의 렌더 데이터를 확인한다. 배치 키, 정확한 행렬, mesh bounds, 수면 표시 색이 같으면 배치 재구축만 생략한다. 다른 아이템의 부모 이동도 확인하며, 명시적 dirty와 매 프레임 그리기는 유지한다. 변경 확인 결과를 재구축에도 사용하므로 이동 중에도 렌더 데이터 getter는 객체당 한 번만 호출한다.

큐에 넣기 전에 중복 wake를 다음 프레임으로 넘기는 방식은 적용하지 않았다. 같은 프레임에서 소비하는 큐 예산과 다른 벨트의 처리 시점을 바꿀 수 있기 때문이다. 진행 없는 벨트의 fallback과 sleep 정책도 유지했다.

## 검증 결과

| 검사 | 결과 |
|---|---|
| ConveyorWakeOptimizationHarness | 변경 전 실제 메서드와 현재 메서드의 차등·소유권 단언 25,682개 통과 |
| MapObjectProfilerHarness | 변경 전 기준 JSON 스냅샷 129개 전체 일치 |
| RobotArmVisualHarness | 변경 전 렌더 재구축과 프레임별 비교 등 1,372개 통과 |
| ConveyorPathHarness | 경로 24개, 이동 간격 파이프라인 8개, UV 연결 2,592개, 밀도 576개, 2F 장벽 1,440개 통과 |
| SplitterHarness | 분배·연동·Wheel 단언 4,850개 통과 |
| RobotArmIoHarness | 표준 IO 및 운반 상태 저장 검사 47개 통과 |
| ConveyorPlacementHarness | 로봇팔 배치 후 벨트 동작 관련 검사 18개 통과 |
| 런타임 C# 컴파일 | 기존 참조 목록을 사용하는 독립 Roslyn 컴파일: Editor 조건 E, Player 조건 P 모두 exit 0 |
| diff 검사 | 변경한 런타임 파일의 `git diff --check` 통과 |

관리형 하네스에서 워밍업 후 확인한 할당량이다. 실제 Unity/IL2CPP 프레임당 할당량이나 FPS 개선율로 해석하지 않는다.

| 반복 구간 | 변경 전 | 변경 후 |
|---|---:|---:|
| 코너 2개 그룹 처리 × 10,000회 | 1,440,000 B | 0 B |
| 프로파일러 활성 그룹 갱신 × 1,000회 | 328,000 B | 0 B |

렌더 하네스의 정지 상태 120프레임에서는 배치 재구축 0회, 그리기 120회, 반복 refresh의 관리형 할당 0 B를 확인했다. 움직이는 프레임과 정지 프레임 모두 등록 객체의 렌더 데이터 조회가 한 번임을 검사했다.

## 재실행

저장소 루트에서 실행한다. Unity를 실행하지 않는다.

```powershell
./Tools/ConveyorWakeOptimizationHarness/Run.ps1
./Tools/MapObjectProfilerHarness/Run.ps1
./Tools/RobotArmVisualHarness/Run.ps1
./Tools/RobotArmIoHarness/Run.ps1
./Tools/ConveyorPlacementHarness/Run.ps1

dotnet run -c Release --project Tools/ConveyorPathHarness/ConveyorPathHarness.csproj '-p:UnityManagedDirectory=C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine'
dotnet run -c Release --project Tools/SplitterHarness/SplitterHarness.csproj '-p:UnityManagedDirectory=C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine'
```

새 하네스는 실제 변경 메서드를 추출하고 변경 전 기준과 비교한다. 청크·핸들 저장소, 블록 틱 경계, Transform/Mesh/Material, GPU 제출 등은 테스트 대역이다. 엔진 내 애니메이터 순서, 실제 영상, 전체 프레임 스케줄러를 포함한 장시간 플레이의 완전한 동일성은 이 검사만으로 보장하지 않는다.

현재 실행 중인 빌드에는 이 소스 변경이 반영되지 않았다. 다음 빌드에서 같은 세이브·카메라·컬링·프로파일러 설정으로 FPS와 CPU 시간을 다시 측정해야 실제 개선량을 판단할 수 있다. 우선 확인할 장면은 벨트 중간 아이템 제거, 출구 막힘과 재개, T자 및 1F/2F 합류, 분배기 교차 이동, 로봇팔 배치 직후 이동과 장시간 대기다.
