# Conveyor wake optimization equivalence checks

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/ConveyorWakeOptimizationHarness/Run.ps1
```

.NET 9만 사용하며 Unity나 실행 중인 게임을 조작하지 않는다. 임시 디렉터리에서 현재 `TerrainGenerator`의 실제 코너 큐·수거·정렬·틱·반환·초기화 메서드와 직선 큐·defer·retry·promote 메서드를 추출해 컴파일한다. 변경 전 메서드는 `LegacyScheduler.cs`에 고정했다. 범위·retry·코너 그룹의 자료형도 현재 원본에서 추출한다.

동일한 입력에 대해 두 버전의 반환값, 큐 순서, 범위 병합, retry 상태와 시도 횟수, 모든 해당 진단 카운터, 블록 처리 순서, batch 경계, 수면 알림·재삽입을 비교한다.

- blocked/ready retry의 대기·만료·정확한 만료 시각, 범위 전체·내부·겹침·분리, 여러 라인의 병합과 defer/promote
- 없는 라인·잘못된 ID·남은 큐 엔트리, 무한/NaN retry 시각에 대한 이전 결과 유지
- 코너 중복 wake, direct wake 전환, 사라진 핸들·그룹·토폴로지 변경, 비활성 블록, 실행 안 된 틱과 진행 없는 틱
- 수거 중 재진입, 틱 중 재삽입, 수거 중 전체 초기화, 그룹 ID 재사용, downstream-first 순서
- 동시에 300개 그룹을 사용한 뒤 256개 pool 상한 및 버퍼 소유권 확인
- 두 코너 그룹을 10,000회 처리해 워밍업 이후 관리형 할당을 변경 전과 비교

블록 실제 이동, Unity의 청크/핸들 저장소, 프레임 전체 예산과 fairness 스케줄러는 대역 경계다. 경로·간격·분배기·로봇팔의 별도 하네스 및 게임 내 확인을 대체하지 않는다. 이 검사는 wake 요청의 의미를 바꾸거나 틱 시점을 이동하는 최적화를 승인하는 근거가 아니다.
