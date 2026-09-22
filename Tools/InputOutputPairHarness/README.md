# InputOutputPair harness

실행: `./Tools/InputOutputPairHarness/Run.ps1`

Unity 에디터를 실행하지 않고 실제 `InputOutputModule`의 Pair 처리 메서드와
`OnValidate`, 부모 선택 검증 메서드를 추출해 .NET에서 실행해.

- 복수 Input/Output, 레거시 데이터 변환, 유체 소수량, 부모 레시피 상속
- 부모 프리팹이 아직 로드되지 않았을 때 참조 보존과 이후 상속 복원
- 로드 검증 중 다른 에셋의 `mapObject`를 조회하지 않는지 확인
- 저장된 순환 참조를 자동 삭제하지 않으면서 레시피 순회는 유한하게 유지
- 편집 시 새 순환 참조 거부와 명시적인 `None` 선택 허용

`OnValidate`의 기반 클래스와 RectGrid 처리는 스텁이야. Unity 네이티브 에셋
직렬화, 프리팹 저장, 에디터 종료/재시작까지 검증한 테스트는 아니야.
별도 확인할 때는 변경된 스크립트 컴파일이 끝난 뒤 Item Data에서 부모를 지정하고,
프리팹 파일의 `parentInputOutputModuleItem` GUID가 지정 직후와 재시작 후에도
같은지 확인해. 이미 파일에 `None`으로 저장된 참조를 이 수정이 추정 복구하지는 않아.

## 실행 중인 Unity의 실제 참조 진단

`CaptureLiveParentState.cs`는 Assets 밖에 둔 진단 스크립트야. Unity Pipeline의
`run_script`로 실행하며, 프로젝트 재컴파일/도메인 리로드 없이 현재 객체를 읽어.

```powershell
unity command run_script --caller plugin --skill unity-cli --project-path C:/Git/ProjectF/FactorioProject --file C:/Git/ProjectF/Tools/InputOutputPairHarness/CaptureLiveParentState.cs --entry CaptureLiveParentState.Main --format json
unity command run_script --caller plugin --skill unity-cli --project-path C:/Git/ProjectF/FactorioProject --file C:/Git/ProjectF/Tools/InputOutputPairHarness/CaptureLiveParentState.cs --entry CaptureLiveParentState.AuditAll --format json
```

- `Main`: Item Data 창의 **Update 전** SerializedObject 캐시와 실제 MK1~MK4 참조를 대조해.
- `AuditAll`: ItemDefinition이 가리키는 IO 모듈의 로드된 부모 GUID와 원본 프리팹의 로컬 부모 필드를 비교해.
  원본에 로컬 필드가 없는 Variant 등은 `UNVERIFIED`로 표시해.
- 둘 다 참조 재지정, Apply, 저장, 임포트를 하지 않아. 재시작 전후에 각각 실행해야 해.

`RectGridBlockDetailChecks.cs`는 임시 오브젝트만 만들어 RectGrid 블록의 아이템/유체
참조가 직렬화되고, 블록 이동·교환 뒤에도 블록과 함께 유지되는지 검사해.

```powershell
unity command run_script --caller plugin --skill unity-cli --project-path C:/Git/ProjectF/FactorioProject --file C:/Git/ProjectF/Tools/InputOutputPairHarness/RectGridBlockDetailChecks.cs --format json
```
