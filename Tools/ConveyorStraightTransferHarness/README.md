# Straight transfer validation equivalence

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/ConveyorStraightTransferHarness/Run.ps1
```

Unity를 실행하지 않고 .NET 9와 Unity 관리형 벡터 수학만 사용한다. 실행마다 현재 `Block.cs`의 실제 cached/noncached 전송 메서드, 아이템 읽기·준비 여부 판정·이동 시간 초기화 함수를 추출한다. 변경 전 네 메서드는 `LegacyBlock.cs`에 고정했다.

기존의 모든 게임 호출 형태를 비교한다.

- 직선 라인의 `CanMove && TryMove` → 단일 `TryMove`
- 구조 확인 후 경로를 계산하는 noncached `TryMoveStraightConveyorDataLaneTo`

기존 cached TryMove 자체는 실패 호출도 프로파일러 시도로 집계했다. 현재 게임 호출부는 사전 Can 검사로 실패 후보를 제외하므로, 통합 뒤에도 실패는 0회, 전송은 1회로 유지한다. 사전 검사 없이 public cached TryMove만 직접 호출하는 외부 코드의 실패 진단 횟수는 달라진다. 이 호출 형태는 현재 프로젝트에 없다.

잘못된 슬롯·비활성 레인·음수 아이템·빈칸·실물 아이템·hold·같은 프레임 이동·준비 경계, 동일 블록/이웃/없는 대상, 네 방향·혼합 속도·0속도·경로 epsilon·경유점과 noncached 구조 fallback을 확인한다. 변경 전후 슬롯 수량·ID, pickup gate, occupancy version, move frame, 이동 위치·시작 시각·길이·기간을 비트 단위로 비교하며 vacancy callback과 Clear/Set/Dirty/MarkMoved의 순서도 비교한다. 성공 경로의 readiness·lane validity 호출 감소를 계수한다.

Clear/Set 내부 저장소와 알림 수신자는 관리형 대역이다. 실제 전송 본문의 호출 순서는 사용하지만 실제 청크 저장소·시설물·GPU·2F/분배기의 전체 fallback이나 화면 동작을 실행하지는 않는다. 기존 경로·분배기·로봇팔 하네스와 엔진 내 검증을 대체하지 않는다.
