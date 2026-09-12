# RobotArm ECS 전환
설치된 일반/긴 로봇팔은 `RobotArmWorld` GameObject와 MonoBehaviour 하나를 공유합니다.
`RobotArmInstance`는 세대 검증 ID이며 가변 시뮬레이션 값은 슬롯 배열에 저장합니다.
상태 계획과 아이템 변경 적용은 고정 틱에서 설치 ID 순으로 처리합니다. Jobs/Burst를 도입한 변경은 아닙니다.

프리팹의 Transform 계층은 종류별로 한 번 읽습니다. 이후 회전·애니메이션·손 아이템은 데이터와 렌더 행렬로 계산합니다.
일반/긴 팔의 16개 Euler 애니메이션 트랙은 원본 .anim에서 추출한 값/접선입니다.
물리 충돌은 공용 호스트에 붙은 SphereCollider로 유지합니다. 개별 Animator, PortableObject, Transform 계층은 설치된 팔에 생성하지 않습니다.
기존 배치/편집 도구의 미리보기는 임시 프리팹을 사용하며 풀에 보관하지 않습니다.

좌표 변경은 정확한 입출력·본체 좌표에만 알립니다. 알림에서는 수용 가능 여부를 조회하지 않으며
다음 틱에 중복을 합쳐 검사합니다. 경합에서 진 팔은 아이템을 유지하고 Drop 애니메이션을 시작하지 않습니다.
움직이는 화물칸은 같은 셀에서 정지하는 전환을 놓치지 않도록 재시도를 유지합니다.

## 실행
- `./Tools/RobotArmEcsHarness/Run.ps1`: 생산 코드의 알림·계획·적용·수면·수요·스케줄러와 세대 슬롯을 추출해 416개 검사.
- `./Tools/RobotArmIoHarness/Run.ps1`: 입출력 좌표 회전, 벨트/바닥 구분, 열차 조건, 저장 DTO 등 206개 검사.
- `./Tools/RobotArmBeltPickupHarness/Run.ps1`: 벨트 픽업 우선순위 6개 검사.
- `./Tools/RobotArmVisualHarness/Run.ps1`: 호스트 생성 경로와 렌더링 구조, 16개 원본 애니메이션 트랙 일치 검사.
- `./Tools/RobotArmAnimationBake/Run.ps1 -Check`: 원본과 생성 코드 비교. 애니메이션 변경 후에는 -Check 없이 재생성.

하네스는 Unity를 실행하지 않습니다. 엔진·전력망 공급·실제 인벤토리 경계는 대역입니다.
렌더 픽셀, 실제 충돌, 실행 성능 및 서로 다른 CPU에서의 완전한 결정론을 증명하지 않습니다.

## 게임에서 확인할 항목
1. SaveSlot1 로드 후 RobotArmWorld 자식 계층이 없고 등록 수가 기존 팔 수와 같은지 확인.
2. 일반/긴 팔을 네 방향으로 설치해 박스↔벨트↔화물칸 운반, 전력 부족/복구, 필터 변경 확인.
3. 마우스 클릭 선택·근접 선택·손 아이템 회수·입출력 마커 표시 확인.
4. 꽉 찬 벨트 앞에서 고개를 반복해서 움직이지 않는지, 빈칸과 화물칸 정차에 재개하는지 확인.
5. 손에 아이템을 든 상태 및 회전 중 저장/로드, 이동·취소·회수, MapObj Item Clear 확인.
6. 모든 팔을 화면 밖으로 옮겼을 때 RobotArmWorld Visible/Matrices가 0인지 확인.

기존 저장 DTO를 유지합니다. `turnTimer`는 회전 전체 시간이 아니라 남은 회전 시간으로 저장해 복원 진행도를 보존합니다.
