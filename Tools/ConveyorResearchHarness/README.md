# 벨트 재처리·수면 판단 회귀 하네스

저장소 루트에서 `./Tools/ConveyorResearchHarness/Probe-WakeQueue.ps1`을 실행한다. .NET 9만 사용하며 게임 엔진과 실행 중인 빌드에는 연결하지 않는다.

이전에 지연 wake의 의미 손실을 조사하던 스크립트를 회귀 검사로 갱신했다. 별도의 유사 큐 하네스를 만들지 않고 기존 진입점을 유지한다.

실제 소스에서 다음 메서드를 추출한다.

- batch 시작/종료, network wake 저장/병합/flush
- direct enqueue/dispatch와 라인 fallback 대상 선택
- ShouldTickActiveConveyor와 슬롯 ready/sleep 판정
- 실제 이동 시도 본문과 CanMove 캐시 조회/저장/무효화
- 실패 후 수면 판단과 재각성 판단
- 빈 슬롯 predecessor 알림과 blocked waiter 깨우기

118개 단언이 다음을 검사한다.

- 네 개 false/true 요청의 모든 16개 순서, 중첩 batch, false-only의 큐 억제와 네트워크 상태 갱신, true 요청 병합
- flush 중 새 pending 요청 보존과 삭제된 블록 요청 무시
- 정체 블록 100회의 직접 요청에서 tick 0회, 이웃 수면 해제 0회
- 아이템 존재만으로 fallback에 등록하지 않음, 실제 빈칸 predecessor/waiter 알림 후 재개, 진행 중 모션과 라인 소유권 처리
- 이동 실패 후 수면/각성 판단에서 같은 결과 재사용
- 프레임 변경, 동일 프레임 상태 변경, throttle 모드 차이, 준비되지 않은 조기 반환에서 stale 결과를 재사용하지 않음

준비 후 10,000회 지연 요청 병합/flush의 관리형 할당은 0B이며, 상태 갱신 전용 요청의 enqueue는 0회다. 100회 실제 이동 시도 실패 뒤 수면/각성 판단의 추가 읽기 전용 계획 호출은 0회다. 이 숫자는 하네스의 통제된 조건에서 얻은 작업량이며 전체 게임 FPS 개선율이 아니다.

한계: 블록 핸들 저장소·토폴로지 조회·네트워크 구축·시각 갱신·모션 표현·전송 적용·재귀 계획기의 결정은 관리형 대역이다. 실제 재귀 계획기의 전체 이동 규칙이나 프레임 예산/공정성 검사를 대체하지 않는다. 실제 이동·연결은 ConveyorTransport, ConveyorStraightTransfer, ConveyorPlacement, RobotArmIo, Splitter 하네스로 함께 확인한다. Unity/IL2CPP·Tween/물리·실제 세이브 왕복과 FPS는 별도 확인이 필요하다.
