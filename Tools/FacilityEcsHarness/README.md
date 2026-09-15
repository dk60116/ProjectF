# Facility ECS Harness

유체·전력 설비를 전역 시뮬레이션 시계의 개별 대상 대신 하나의 월드 대상으로
스케줄링하는 계약을 검사해. SimulationId 기반 Tick 위상 분산, 현재 Tick 버킷만 조회하는 스케줄러,
안정 ID Plan/Apply 순서, 0.1초 간격, Sleep/Wake, 유체 설비 SoA Plan/Apply 경계,
전력망 1회 준비, 유체 전용 Tick의 전력망 생략, 600개 설비 버킷의 정상상태 무할당을 포함해.

```powershell
Tools/FacilityEcsHarness/Run.ps1
```

매 실행마다 임시 출력 폴더에서 생산 코드를 직접 컴파일해 이전 실행 파일 잠금의 영향을 받지 않아.

Unity나 실제 세이브 슬롯은 실행하지 않아.
