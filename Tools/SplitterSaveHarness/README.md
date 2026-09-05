# 분배벨트 저장 왕복 검사

Unity 실행 없이 Roslyn으로 빌드한 현재 게임의 `Assembly-CSharp-P.dll`을 참조하여
실제 `SaveGameBinarySerializer`의 설치물 쓰기/읽기 함수를 실행한다.

```powershell
dotnet run --project Tools/SplitterSaveHarness/SplitterSaveHarness.csproj "-p:CompiledAssemblyDirectory=$env:TEMP/ProjectF-BeltTopUv-Compile" '-p:UnityManagedDirectory=C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine'
```

DLL은 현재 소스로 먼저 다시 컴파일해야 한다. 디렉터리 인자는 해당 DLL 위치로 바꿀 수 있다.
필터 모드 3종 × 다음 입력 2종 × 다음 출력 2종 × Wheel 상태 4종의 버전 52 저장 왕복과,
버전 50/51 설치물 읽기 호환성을 합쳐 총 144개를 검사한다.
기존 버전 51 저장은 Wheel 상태가 없으므로 최초 상태(양쪽 정지)로 복원한다.
전체 월드 로드/Unity 오브젝트 생성은 실행하지 않는다.
