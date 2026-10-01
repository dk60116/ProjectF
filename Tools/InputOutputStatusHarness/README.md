# IOModule 상태·전력 하네스

`powershell -NoProfile -ExecutionPolicy Bypass -File Tools/InputOutputStatusHarness/Run.ps1`

실제 IOModule·제작기의 상태와 타깃 판정, 전력 수요, InfoPanel 전력 및 표시등 메서드를 추출해 실행해.

- 아이템·유체 입력 대기와 배출 정체: 노랑, 설정된 UseAmount 전체 수요
- 전력 부족: 빨강, 공급 0, 전력 복구를 위한 수요 유지
- 타깃·레시피·출력 영역 없음: 빨강, 전력 수요와 공급 0
- 배출 진행: 초록, 이전 배치가 끝나기 전에는 잠긴 타깃 유지
- 네트워크 평가 중 수요 판정에서 전력 공급을 재귀 조회하지 않음

Unity 실행이나 UI 화면 검증 없이 동작하는 별도 콘솔 하네스야.
