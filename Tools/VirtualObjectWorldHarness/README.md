# VirtualObjectWorldHarness

실제 `VirtualObjectWorld`와 `MapObjectHandle` 소스를 Unity 씬 없이 컴파일해 실행해.

검사 범위는 관리형 서비스 수명, 데이터 전용 설치물 등록, View 결합·해제 중 ID 유지,
좌표 색인, 논리 엔티티 교체와 월드 교체 후 stale handle 차단이야.

```powershell
dotnet run --project Tools/VirtualObjectWorldHarness/VirtualObjectWorldHarness.csproj -c Release
```
