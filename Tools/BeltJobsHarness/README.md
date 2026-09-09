# 벨트 Jobs 시뮬레이션

## 적용 구조

- `BeltSimulationJob`: 실제 이동, 막힘 전파, 합류 우선순위, 분배기 필터와 교대 배출, 순환 라인 이동을 계산한다.
- `TerrainGenerator.ConveyorJobs`: 기존 Belt Split 그룹을 좌표와 슬롯 순서로 정렬하여 영구 `NativeArray`를 구성한다. 독립 그룹 하나가 `IJobParallelFor`의 작업 단위다.
- `Block.ConveyorJobs`: 기존 설치·플레이어·로봇팔·I/O API의 슬롯 예약과 계산 결과 표시를 연결한다.
- `TerrainGenerator.ConveyorJobs.Save`와 `BeltSimulationSnapshot`: 고정 틱, 남은 이동 시간, 이동 출발점과 합류 커서를 저장한다. 세이브 버전은 59다. 분배기 교대 상태는 기존 설치물 저장 필드를 사용한다.

2F 교차 구간도 같은 Block 안의 독립 슬롯이면 서로 다른 그룹에서 계산한다. 분배기의 양쪽 입력·출력은 하나의 그룹에 포함된다. 로봇팔은 그룹을 연결하지 않고 입출력 요청을 낸다.

Unity 오브젝트와 렌더링은 메인 스레드에서 처리한다. Job에는 관리 객체 참조가 들어가지 않는다. 작업 완료 후 슬롯 캐시를 먼저 모두 반영하고 관찰자에게 좌표 순서로 통지한다. 막힌 그룹은 슬롯 재검사를 중단하며 입출력 또는 토폴로지 변경으로 깨어난다.

벨트 아이템은 항상 데이터 슬롯과 인스턴스 렌더러를 사용한다. 데이터 계산과 맞지 않는 이전 `virtualizeConveyorItems` 선택 옵션은 제거했다.

기존 프레임별 벨트 스케줄러와 transport-run 생성 경로는 제거했다. 기존 설치/시각화/편집용 API에서 참조하는 기하·캐시 도우미는 유지한다.

## 결정론 계약

계산은 초당 60틱이며 한 틱은 65,536 정수 단위다. 이동 판정과 충돌 해결에 실수·프레임 시간·난수를 사용하지 않는다. 기하 길이와 속도는 토폴로지 구축 시 밀리미터 단위로 양자화한다. 실수 위치 보간과 점프 곡선은 표시용이다.

같은 양자화된 토폴로지, 초기 상태, 같은 순서로 수락된 입출력 요청, 같은 틱 수가 주어지면 작업 실행 순서에 관계없이 결과가 같다. 기존 동기식 삽입/회수 API는 즉시 슬롯을 예약해 중복 수락을 막으며, 실제 계산 배열에는 다음 계산 틱 경계에서 반영한다. 여러 요청으로 같은 슬롯을 갱신하면 최종 예약 상태를 좌표·슬롯 순서로 반영한다. 저장과 체크섬은 예약 상태를 확정하는 명시적 경계이기도 하다.

향후 멀티플레이 드라이버는 다음과 같이 연결한다.

```csharp
terrain.BeltSimulationExternallyClocked = true;
// 동일한 틱에 해당하는 명령을 모든 컴퓨터에서 같은 순서로 기존 API에 적용한다.
terrain.StepBeltSimulation();
ulong checksum = terrain.ComputeBeltSimulationChecksum();
```

체크섬에는 양자화된 경로/속도, 아이템, 남은 시간, 합류 및 분배기 상태가 들어간다. 표시용 좌표 보간은 제외한다. 시작 시에도 체크섬을 비교하여 토폴로지 일치를 확인할 수 있다.

로봇팔, 플레이어 입력, I/O, 맵 스트리밍을 포함한 전체 월드의 네트워크 명령 동기화는 이 변경에 포함하지 않는다. 현재 프레임에서 이들이 요청을 생성하는 시점까지 컴퓨터 간 동일해지는 것은 아니다. 멀티플레이에서는 틱별 입력 순서와 로드된 맵/양자화된 토폴로지를 동기화해야 한다. 엔진 밖 하네스 결과만으로 Burst의 서로 다른 CPU 대상 실행을 검증했다고 간주하지 않는다.

자동 시간 구동은 한 프레임당 최대 8틱을 따라잡고 남은 지연을 보존한다. 느린 프레임 때문에 계산 틱을 버리지 않는다. 벨트 속도를 편집하면 경로를 다시 구축하며 새 속도는 다음 구간부터 사용한다. 속도 0은 이동 중 아이템도 정지시킨다.

## 자동 검사 실행

저장소 루트에서 실행한다. Unity를 실행하지 않는다.

```powershell
dotnet run --project Tools/BeltJobsHarness -c Release
dotnet run --project Tools/BeltSplitHarness -c Release
dotnet build FactorioProject/Assembly-CSharp.csproj --no-restore -v:q /clp:ErrorsOnly
dotnet run --project Tools/SplitterSaveHarness -c Release
```

마지막 검사는 앞 단계에서 컴파일된 실제 게임 어셈블리의 저장 코드를 사용하므로 빌드 완료 후 실행한다. Unity가 생성한 csproj와 설치된 라이브러리가 있는 개발 환경이 필요하다.

`BeltJobsHarness`는 실제 계산 커널과 호스트의 구축·입출력 반영·저장 복원 코드를 연결한다. 엔진 할당/스케줄링/씬 객체만 스텁으로 대체한다. 512슬롯의 16개 그룹을 실제 .NET 병렬 실행, 직렬 실행, 역순 실행으로 비교한다. 막힘, 가득 찬 순환 벨트, 합류 교대, 분배기 필터, 막힌 출구의 대체 배출, 외부 삽입 지연, 토폴로지 재구축, 저장 후 연속 계산도 검사한다.

Unity Job safety 검사, Burst 기계어 생성, 실제 씬 API와 렌더링, 실제 성능은 별도 실행 확인 대상이다.

## 게임에서 확인할 항목

프로파일러의 `BeltJobs` 항목에는 그룹/슬롯 수, 가장 큰 그룹, 수면 그룹, 대기 중 입력, 마지막 틱 이동 수, 변경 슬롯, 프레임 처리 틱, 누적 지연과 토폴로지 재구축 횟수가 표시된다. Unity Profiler에서는 `Belt Jobs.Bake`, `Belt Jobs.Tick`, `Belt Jobs.Publish`와 `BeltSimulationJob`을 확인한다.

1. 직선·커브·합류·분배기·2F 경사/교차에 아이템을 흘려보낸다. 로봇팔 및 OutputArea 입출력도 함께 확인한다.
2. 출구를 막았다가 아이템을 회수하여 다시 흐르는지 확인한다. 분배기는 한쪽 출구만 막아 반대쪽 배출도 확인한다.
3. 아이템이 이동하는 중간에 벨트를 추가·회전·철거하고 저장/로드한다.
4. Show Belt Split 색과 `BeltJobs.Groups`를 비교한다. 한 개의 거대한 연결 그룹은 한 작업이므로 여러 코어로 내부 분할되지 않는다.
5. 같은 장면에서 전체 프레임 시간과 계산/반영/렌더링 시간을 비교한다. 이 변경만으로 특정 FPS 향상을 보장하지 않는다.

읽기/쓰기 범위 제한 해제는 각 그룹에 배정한 배열 구간에만 적용한다. 관련 API 계약: [Unity NativeDisableParallelForRestriction](https://docs.unity3d.com/cn/6000.0/ScriptReference/Unity.Collections.NativeDisableParallelForRestrictionAttribute.html).
