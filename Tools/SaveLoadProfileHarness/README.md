# SaveLoadProfileHarness

실제 `.pfsave`를 Unity 실행 없이 읽어 파일 처리 시간과 저장 데이터 규모를 출력하는 진단 하네스임.

```powershell
dotnet run --project Tools/SaveLoadProfileHarness/SaveLoadProfileHarness.csproj -- <save-file> [chunk-size load-radius]
dotnet run --project Tools/SaveLoadProfileHarness/SaveLoadProfileHarness.csproj -- --self-check
dotnet run --project Tools/SaveLoadProfileHarness/SaveLoadProfileHarness.csproj -- --refinery-self-check
dotnet run --project Tools/SaveLoadProfileHarness/SaveLoadProfileHarness.csproj -p:CompiledAssemblyDirectory=../../FactorioProject/Library/ScriptAssemblies -- --fluid-output <save-file>
dotnet run --project Tools/SaveLoadProfileHarness/SaveLoadProfileHarness.csproj -p:CompiledAssemblyDirectory=../../FactorioProject/Library/ScriptAssemblies -- --pipe-topology <save-file>
```

- 파일을 변경하지 않음
- 프로덕션 `Assembly-CSharp.dll`과 `SaveGameBinarySerializer`를 직접 사용함
- 읽기·파싱 및 메모리 재압축 시간, 주요 목록 개수, 플레이어 주변 청크 후보 개수를 출력함
- `--self-check`는 복제 지형영역의 저장/복원 포맷을 메모리에서 왕복 검증함
- `--refinery-self-check`는 정유기의 입력량·정유 진행·출력별 잔량·온도·스냅샷 복사와 버전 68 읽기 경계를 메모리에서 검증해
- `--fluid-output`은 제작기 진행 상태·출력 잔량·입력 좌표·입력 유체·필터 상태, 같은 행의 파이프 좌표·연결 마스크, 탱크의 실제 저장 유체량을 읽기 전용으로 출력함. 펌프의 회전·포트 좌표·같은 열의 파이프도 함께 출력해서 공급 방향을 추적함
- `--pipe-topology`는 파이프·원유 채굴기·정유기의 좌표, 파이프 종류·회전·연결 마스크·월드 회전, 출력 좌표를 읽기 전용으로 출력해. 저장된 모양과 복구 중 재계산된 모양을 구분하는 진단 경로야.
