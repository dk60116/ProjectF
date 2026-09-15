# Facility ECS Harness

유체·전력 설비를 전역 시뮬레이션 시계의 개별 대상 대신 하나의 월드 대상으로
스케줄링하는 계약을 검사해. 안정 ID Plan/Apply 순서, 0.1초 간격, Sleep/Wake,
전력망 1회 준비, 유체 전용 Tick의 전력망 생략을 포함해.

```powershell
dotnet run --project Tools/FacilityEcsHarness/FacilityEcsHarness.csproj -c Release
```

Unity나 실제 세이브 슬롯은 실행하지 않아.
