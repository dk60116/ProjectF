# CraftingTree 소수 수량 하네스

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/CraftingTreeQuantityHarness/Run.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/CraftingTreeQuantityHarness/Compile.ps1
```

`Run.ps1`은 실제 런타임 전체와 편집기의 수량 필드·바이너리 저장, ID 리매퍼의 읽기·쓰기, 자동 채우기의 바이너리 로더·출력 생성자를 가져와 검사해. Unity 연결은 스텁으로 대체해.

- v1–v5 정수 데이터 읽기와 v6 Float32 수량 저장
- 유체 0.25 출력·0.5 입력의 편집, JSON, 저장·재로드, ID 변경, 자동 채우기
- 일반 아이템의 정수 입력과 인벤토리 수량 변환
- 실제 원본 바이너리의 전체 레코드 읽기
- 손 제작 레시피 조회의 작업대 전용·원재료 제외, 정렬, 재로드, 조회 중 GC 할당 검증

`Compile.ps1`은 기존 Unity DLL 참조로 런타임과 에디터 소스를 임시 폴더에 컴파일해. Unity를 실행하거나 프로젝트 바이너리를 교체하지 않아. 기존 프로젝트 파일과 Library DLL이 필요해.

실제 제작기의 소수 입력 배치와 출력 압력·배출량은 `ProductionMachineFluidInputHarness/ReceiverRun.ps1`, `ProductionMachineFluidOutputHarness/Run.ps1`에서도 검사해. Unity 화면 동작은 별도 검증이 필요해.
