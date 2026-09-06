# 로봇팔 실물 아이템 렌더 회귀 하네스

`powershell -ExecutionPolicy Bypass -File Tools/RobotArmVisualHarness/Run.ps1`

Unity를 실행하지 않고 .NET에서 실제 로봇팔 refresh, PortableObject 전달 메서드,
PortableItemRenderer LateUpdate/변경 감지/재구축 및 VirtualRenderBatchKey를 추출해 실행한다.
기준은 `LegacyPortableItemRenderer.cs`에 고정한 변경 전 RebuildPortableObjectBatches이며 요청마다 전역 재구축한다.
매 프레임 모든 배치의 키·행렬·mesh bounds·순서를 비교한다.

- 정지 120프레임: 그리기 유지, 배치 재구축 0회
- 로봇팔 tick 후 애니메이터/부모 이동, 아주 작은 행렬 변화
- 다른 아이템이 별도 dirty 없이 부모/재질 변경: 기존 전역 refresh 의미 유지
- 아이템/mesh/material/layer/shadow/tint/debug 색 및 mesh bounds 변경
- 같은 material 내부의 수면 디버그색 변경과 이동 프레임의 렌더 데이터 단일 읽기
- 부모 비활성화/복귀, 화면 밖 이동/시야 복귀, 등록 해제/같은 풀 객체 재사용
- 명시적 dirty, 파괴 객체 정리, 반복 이동 추적, 여러 재질의 행렬 단일 읽기
- 입력 필터 delegate 재사용, 필터 변경/풀 재사용/다른 로봇팔의 상태 분리

Unity의 행렬·벡터·bounds 값 형식을 사용한다. Transform/Mesh/Material,
컬링 및 배치 제출은 테스트 대역이므로 실제 GPU 이미지나 엔진 Animator 순서를 검증하지는 않는다.
