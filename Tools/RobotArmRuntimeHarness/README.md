# 로봇팔 런타임 최적화 검증

`./Tools/RobotArmRuntimeHarness/Run.ps1`

Unity를 실행하지 않고 실제 RobotArm의 전력 수요, 깨우기, 틱 진입과 시간 계산,
MapObjectTickManager의 버킷 스케줄링 메서드를 추출해 .NET에서 실행합니다.

- 모든 상태/아이템 보유/포트 유효성 조합의 전력 수요 확인. 수요 판정 중 아이템 탐색은 예외로 검출합니다.
- 좌표 알림과 기반 모듈 알림 2,000회가 다음 틱 전까지 중복 등록/타이머 리셋을 만들지 않는지 확인합니다.
- 깨우기 요청이 이미 있어도 실행 등록이 유실되면 복구하는지, 비활성화/재사용 후 다시 깨울 수 있는지 확인합니다.
- 34개 팔을 30/60/113/144/240 FPS에서 5초간 스케줄링하여 실행 빈도, 공정성과 누적 경과 시간을 확인합니다.

시계, 전력 공급, 씬 조회, 실제 운반 상태 실행 및 틱 등록 저장소는 대역입니다.
이 검사는 Animator나 실제 운반 처리량, Unity/Burst 성능을 측정하지 않습니다.
입출력/세이브는 RobotArmIoHarness, 틱 없는 렌더 프레임의 손 아이템 표시와
배치 재구축은 RobotArmVisualHarness로 함께 확인합니다.

현재 변경은 기존 스케줄러를 이용한 평균 약 60Hz 제한입니다.
네트워크용 고정 틱/입력 순서 동기화는 별도 작업입니다.

MapObject Profiler에는 Robot Arm Sleep Check, Power, State Tick, Pickup/Drop Query,
Pickup/Drop Transfer, Wake Applied, Held Item Visuals, Render Build, Render Submit 및
Electric Network Runtime이 표시됩니다. Query는 State Tick/Transfer 안에, 전력망 평가는
첫 소비자의 Power 안에 포함될 수 있으므로 행들을 모두 합산하면 중복 집계됩니다.
