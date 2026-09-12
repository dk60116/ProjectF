# RobotArm 표시 구조 검사

`./Tools/RobotArmVisualHarness/Run.ps1`

공용 호스트 생성, 엔티티의 씬 컴포넌트 배제, 프리팹을 변경하지 않는 행렬 렌더링, 컬링 순서와 손 아이템 보간 경로를 검사합니다.
원본 일반/긴 팔의 16개 애니메이션 트랙을 생성 코드와 비교합니다.

이 검사는 소스와 자산의 일관성 검사이며 실제 Unity 화면을 렌더하지 않습니다. 이전 개별 PortableObject/Transform 기반 검사는 제거했습니다.
전체 동작 검사는 [RobotArmEcsHarness](../RobotArmEcsHarness/README.md)를 참고하세요.
