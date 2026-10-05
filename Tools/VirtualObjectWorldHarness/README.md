# VirtualObjectWorldHarness

실제 `VirtualObjectWorld`와 `MapObjectHandle` 소스를 Unity 씬 없이 컴파일해 실행해.

검사 범위는 관리형 서비스 수명, 데이터 전용 설치물 등록, View 결합·해제 중 ID 유지,
좌표 색인, 논리 엔티티 교체와 월드 교체 후 stale handle 차단이야.

```powershell
dotnet run --project Tools/VirtualObjectWorldHarness/VirtualObjectWorldHarness.csproj -c Release
```

차량 이동과 저장 색인 회귀 검사까지 실행하려면 아래 명령을 사용해.
`BlockStateStore`의 실제 이동, 저장 키 결정, 좌표 색인, 개수 갱신 메서드를 추출하고
실제 `VirtualObjectWorld`와 함께 실행해. Unity 경계와 상태 캡처만 대역이야.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/VirtualObjectWorldHarness/Run.ps1
```

셀·청크 경계 이동 시 핸들 유지, 정적 렌더 버전 유지, 열차의 정적 지도 버전 유지,
비열차 차량의 지도 갱신, 저장 좌표 이전, 저장 키 충돌, 이동 후 제거를 확인해.
게임 화면과 실제 저장 파일의 왕복 복원은 이 하네스의 범위 밖이야.
