# Benchmark terrain harness

Seed 0은 Map Size를 무시하고 빈 흙 지형을 청크 단위로 생성해. 자연 생성되는 물·광물·원유·나무·갈대·동물은 없어. 설치물과 플레이어가 만든 상태는 기존 저장·복원 경로를 사용해.

실제 런타임 어셈블리의 좌표/청크 경계, 추적 좌표 제한, 자원·물·동물 생성, 표면 워커 입력, 먼 플레이어·공장의 로드 청크 계획, 지형 바이너리 저장·복원을 검사해. 일반 시드의 유한 경계도 함께 검사해. Unity 밖에서 실행하기 위해 메모리 복사본의 네이티브 ProfilerMarker 초기화만 생략해. 지형 함수 본문과 원본 DLL은 변경하지 않아. Unity 실행, 실제 메시 렌더링, 플레이어 이동과 전체 파일 저장은 검증하지 않아.

```powershell
& ./Tools/CraftingTreeQuantityHarness/Compile.ps1
& ./Tools/BenchmarkTerrainHarness/Run.ps1 -CompiledAssemblyDirectory '<compile output directory>/bin/Debug/netstandard2.1'
```
