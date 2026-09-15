# WorldPersistenceHarness

Unity/게임/PC UI를 실행하지 않고 실제 로드·저장 제어 코드를 검사해.

```powershell
& ./Tools/WorldPersistenceHarness/Run.ps1
```

- WorldRestoreProgress와 TerrainChunkStreamingScheduler는 원본 전체를 컴파일해.
- TerrainGenerator의 복원 시작/최종 확정, SaveManager의 캡처/IsLoading, Tick 드라이버의 로드 대기 조건, SaveGameBinarySerializer의 파일 교체 메서드는 원본에서 추출해.
- Unity 코루틴·시계·Job 작업량, 토폴로지/플레이어/DTO 내용은 테스트 대역이야. 코루틴 시작 시 첫 yield까지 즉시 실행되는 규칙도 반영해.
- 준비 완료 전에 연결/체크포인트 복원, 실패 시 Tick 차단, 명시적 재시도, 스트리밍 중 캡처 거부, 미적용 명령 누락 방지를 검사해.
- 청크 일괄 처리 예산은 가상 시간으로 검사해. 2ms 작업 10개와 5ms 예산에서 청크마다 대기하지 않고 3/3/3/1개씩 처리하는지 확인해. 실제 게임 로딩 벤치마크가 아니야.
- 생성 순서/중복 방지, 내부 yield, 취소 시 Dispose, 실패한 청크의 완료 오인과 남은 코루틴 핸들도 검사해.
- 파일 생성·교체와 실패 검사는 Windows 임시 디렉터리에 만든 테스트 파일만 사용해. 직렬화 실패·파일 잠금 때 이전 파일 보존을 확인하고 테스트 파일을 정리해. 실제 슬롯 파일은 읽거나 덮어쓰지 않아.
- 활성 로딩 화면이 최신 씬 요청을 받아들이는지, 이전 슬롯 pending 데이터가 폐기되는지, 새 슬롯 데이터가 씬 요청 승인 뒤 게시되는지도 소스 계약으로 검사해.

이 하네스의 DTO 대역은 전체 저장 포맷의 직렬화 검증을 대신하지 않아. 실제 Assembly-CSharp 직렬화기와 DTO의 압축 파일 생성/교체/읽기는 확장한 SplitterSaveHarness로 별도 검사해.

```powershell
dotnet run --project Tools/SplitterSaveHarness/SplitterSaveHarness.csproj -c Release -p:UseAppHost=false
```

이 명령 전에 SimulationCoreHarness README의 코어/게임 코드 빌드를 먼저 완료해야 해. 실제 Instantiate·Mesh/Job safety·SaveSlot2 전체 공정과 시간 측정은 포함하지 않아.
