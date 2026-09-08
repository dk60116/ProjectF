# 분배벨트 저장 왕복 검사

Unity 실행 없이 현재 게임의 `Assembly-CSharp.dll`을 참조하여
실제 `SaveGameBinarySerializer`의 설치물 쓰기/읽기 함수를 실행한다.

```powershell
dotnet run --project Tools/SplitterSaveHarness/SplitterSaveHarness.csproj
```

저장소 루트에서 실행한다. DLL은 현재 소스로 먼저 다시 컴파일해야 한다.
기본 위치는 `FactorioProject/Temp/bin/Debug`이며 `CompiledAssemblyDirectory`로 바꿀 수 있다.
필터 모드 3종 × 다음 입력 2종 × 다음 출력 2종 × Wheel 상태 4종의 버전 56 저장 왕복과,
상자 보관 범위·벌목 성장도 범위, 버전 50/51/52/55 설치물 읽기 호환성을 합쳐 총 240개를 검사한다.
기존 버전 55는 최소값을 유지하고 최대값을 전체 한도로 복원한다.
기존 버전 51 저장은 Wheel 상태가 없으므로 최초 상태(양쪽 정지)로 복원한다.
전체 월드 로드/Unity 오브젝트 생성은 실행하지 않는다.
