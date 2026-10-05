# StaticInstallationSyncHarness

실제 `VirtualObjectWorld`와 `StaticMapObjectBatchRenderer`를 Unity 실행 없이 검증해.
GPU 호스트와 엔진·아이템 정의 경계만 대역으로 교체해.

```powershell
dotnet run --configuration Release --project Tools/StaticInstallationSyncHarness/StaticInstallationSyncHarness.csproj
```

타입별 변경 동기화, 라이브 뷰 전환, 마지막 인스턴스 제거, 월드 초기화·교체,
활성화·로딩 중 렌더 중단 전환, 벤치마크 중 변경·취소, 실패 후 재시도를 검사해.
지원하지 않는 설치물 10만 개가 있어도 변경된 지원 타입만 복사하는지 확인하고,
워밍업 이후 렌더러의 미변경·지원 불가 타입 갱신 검사에서 관리형 할당이 없는지 확인해.
실제 GPU 출력과 게임 FPS는 별도로 측정해야 해.
