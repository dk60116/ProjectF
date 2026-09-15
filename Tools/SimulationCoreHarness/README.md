# Simulation core 하네스

Unity/게임을 실행하지 않고 실제 `Simulation/Core` 소스를 컴파일해 검사해. 코어 asmdef는 `noEngineReferences: true`이고 `Assembly-CSharp`를 역참조하지 않아. 기존 Tick 인터페이스 이름은 호출부 호환을 위해 유지했어.

저장소 루트에서 실행해.

```powershell
dotnet run --project Tools/SimulationCoreHarness/SimulationCoreHarness.csproj -c Release
& ./Tools/SimulationCoreHarness/RunProductionAdapter.ps1
& ./Tools/SimulationCoreHarness/CheckBoundaries.ps1
dotnet build Tools/SimulationCoreHarness/SimulationCore.csproj -c Release -v:q
dotnet build FactorioProject/Assembly-CSharp.csproj --no-restore -v:q -p:BuildProjectReferences=false -p:CustomAfterMicrosoftCommonTargets=C:/Git/ProjectF/Tools/SimulationCoreHarness/CompileRuntime.targets '-clp:ErrorsOnly;Summary'
```

마지막 명령은 이미 생성된 Unity csproj에 새 소스와 독립 코어 DLL 참조를 보충하는 컴파일 검사야. 코어를 먼저 빌드해야 해. Unity csproj를 직접 편집하거나 Unity를 실행하지 않아. 다른 체크아웃에서는 targets 절대 경로를 바꿔 줘.

## 검사 범위

- 씬 없이 생성 → 등록 → Step → 해제.
- 기존 60Hz, SimulationId 정렬, 전체 Plan 이후 Apply, 주기별 경과 시간.
- 중복 등록, 제거, Plan 도중 제거, 일시정지, 시계 복원.
- Tick 경계 명령 큐: 경계 처리 중 생성한 명령은 다음 경계에 실행해. 큐는 단일 시뮬레이션 스레드에서 사용하고 작업 스레드에서 직접 Enqueue하지 않아.
- 30/60/113/144/240fps 입력 시간을 같은 Tick 수로 환산한 결과.
- 워밍업 이후 10,000 Tick 동안 현재 스레드 관리 할당 0.
- 프리팹/표현 없이 방향 마스크와 지하 파이프 원격 연결 조회.
- 제작 배치의 진행·정전·출력 대기·체크포인트 복원, 정수 전력 배분.
- 월드 달력·주야·계절 이벤트·일시정지·시간 배율·관찰자 제거 후 진행.
- 차량 가감속·역전·속도 제한, 선로 거리 샘플링·중복 점·경계값.
- 로딩 중 여러 씬 요청이 들어오면 아직 시작하지 않은 요청을 최신 대상으로 교체하고, 진행 중 요청 뒤의 교체 요청을 보존하는지 검사해.
- 제작 및 선로 샘플링 루프의 관리 할당 0.

5단계 로드 준비 상태와 캡처 경계는 `Tools/WorldPersistenceHarness`가 실제 연결부를 포함해 검사해. `CanCaptureCheckpoint`는 Tick 실행 중이거나 미적용 명령이 남아 있으면 false야. 명령 큐를 디스크에 저장하는 구현은 아니며, 현재 포맷으로 이 명령을 조용히 누락시키지 않도록 캡처를 거부해.

`SimulationTickWorld`는 스케줄러야. 모든 생산 설비가 엔진 밖으로 이전됐다는 뜻은 아니야. 명령 큐는 원자적 롤백이나 파일 저장을 제공하지 않고, 모든 게임 입력이 이 큐를 사용하도록 바뀐 것도 아니야.

`RunProductionAdapter.ps1`은 실제 InputOutputModule의 상태 필드/접근자와 Begin/Update/Complete/Clear를 추출해서 코어 연결을 검사해. 레시피 정의·전력 공급·출력 포트·깨움은 테스트 대역이야. 실제 인벤토리·채굴 공정, 바이너리 저장 복원 전체를 검증하지 않아. `ProductionAdapterChecks.cs`는 별도 진입점이므로 기본 코어 하네스에서 제외해.

`CheckBoundaries.ps1`은 코어 엔진 참조와 세 물류 월드 및 동물 AI 월드의 생명주기 경계를 검사하는 소스 계약 검사야. 실제 Unity에서 View를 생성·파괴한 실행 검사는 아니야. 동물 월드의 실제 스케줄러/View 분리 경로는 AnimalAIOptimizationHarness가 Unity API 대역과 함께 추가 검사해.

벨트 데이터 실행과 저장 복원은 `Tools/BeltJobsHarness`가 실제 생산 코드로 별도 검사해. 해당 하네스의 NativeArray/Job/Block은 테스트 대역이고 Unity Burst/safety/렌더링 검증을 대신하지 않아.
