# SaveLoadProfileHarness

실제 `.pfsave`를 Unity 실행 없이 읽어 파일 처리 시간과 저장 데이터 규모를 출력하는 진단 하네스임.

```powershell
dotnet run --project Tools/SaveLoadProfileHarness/SaveLoadProfileHarness.csproj -- <save-file> [chunk-size load-radius]
```

- 파일을 변경하지 않음
- 프로덕션 `Assembly-CSharp.dll`과 `SaveGameBinarySerializer`를 직접 사용함
- 읽기·파싱 및 메모리 재압축 시간, 주요 목록 개수, 플레이어 주변 청크 후보 개수를 출력함
