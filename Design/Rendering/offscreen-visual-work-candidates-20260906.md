# 화면 밖 시각 처리 생략 후보 — 2026-09-06

현재 C# 코드, 설치물 프리팹, 설치물 애니메이션 클립을 정적으로 대조한 목록이다. 최초 취합 이후 아래 공통 시각 관리자를 적용했다. 엔진 실행과 FPS 측정은 하지 않았다. 아래 우선순위는 구현 난이도와 반복 처리 범위 기준이며 실제 비용 순위는 아니다.

## 공통 관리자 적용 상태

- `WorldVisualUpdateManager.LateUpdate`에서 카메라 판정과 등록된 설치물의 시각 갱신을 처리한다. 실행 순서는 850이며 카메라 이동 이후, TerrainWorldRenderer와 배치 렌더러 이전이다.
- 분배기·스프링클러·원유 채굴기·파이프의 개별 시각 Update/LateUpdate를 제거했다. 발전기 Wheel, 차량 Wheel, 기관차 급수관 보간도 이 루프로 처리한다.
- 생산 모듈·벌목기·차량·분배기·파이프가 등록 대상이다. 생산 모듈의 작업 Animator 및 명시적인 파티클 요청을 가시성 정책에 연결했다. 벌목기는 논리 각도와 정렬 판정을 유지한다.
- 화면 밖에서는 Animator의 상태 유지 옵션을 보존하며 비활성화하고, 파티클은 정지·제거한다. 재진입하면 최신 작동 상태로 재생하며 과거 파티클을 몰아서 재생하지 않는다. 다른 코드가 이미 꺼둔 Animator는 임의로 켜지 않는다.
- `Disable Camera Culling == true` 또는 게임 카메라 부재 시 표시 처리를 복원한다. 등록/표시/컬링 대상 수는 관리자의 RegisteredCount/VisibleCount/CulledCount로 확인할 수 있다.
- EditorTool의 `FreeCamera: Player Culling`을 켜면 자유 시점에서 플레이어 기준 컬링을 관찰한다. 자유 시점 진입 전의 시야·줌을 유지하고 플레이어 이동만 반영하며, 기본값은 꺼짐이다. 네이티브 렌더러와 수동 배치/시각 관리자가 같은 행렬을 사용한다. `Disable Camera Culling`이 우선하며, FreeCamera를 끄면 이 관찰 설정은 적용되지 않는다.
- 로봇팔·활성 동물·아이템 Tween·문·상자·체력바·양동이 표시의 추가 중앙화는 아직 적용하지 않았다. 기관차의 기존 Update/LateUpdate 중 자율주행·연료/물 소비·급수 상태 처리는 유지한다.
- 검증: `Tools/WorldVisualUpdateHarness/Run.ps1`은 실제 관리자/상태 클래스의 24개 관리 코드 검사를 수행한다. 카메라 컬링 하네스는 FreeCamera 관찰 모드 15개를 포함해 72개 검사다. 네이티브 Animator/ParticleSystem 비용과 실제 FPS는 엔진에서 별도 측정해야 한다.

아래 표의 코드 위치와 적용 후보 설명은 최초 취합 기준이며, 이동한 진입점은 위 적용 상태를 기준으로 확인한다.

카메라 렌더링 컬링과 스크립트·Animator·ParticleSystem 실행 중단은 별개다. 기존 컬링이 켜져 있어도 시각 갱신 함수는 호출될 수 있다. 이미 재생 중인 파티클은 Play 호출만 생략해서 멈추지 않는다. 네이티브 컬링 설정에 따라 엔진 내부에서 일부 계산이 생략될 수 있으므로, 아래 목록 전체가 현재 동일한 비용을 소비한다는 뜻은 아니다.

## 1. 우선 적용 후보

시각 부분의 진입점이 분리되어 있거나 생산 상태와 별도로 제어할 수 있는 대상이다. 오브젝트 전체 또는 생산 컴포넌트를 비활성화하는 방식은 사용하지 않는다.

| 대상 | 생략할 처리 | 계속 실행할 처리 / 적용 조건 | 코드 근거 |
|---|---|---|---|
| 분배 벨트 | 좌우 Wheel Transform 회전 | 분배 순서·필터·운반·회전 전환 지연은 유지. 재진입 시 최신 채널 상태 반영 | [Spliterbelt.Update](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Spliterbelt.cs:241>) |
| 생산기 MK1·MK2 | 작업 Animator 평가, speed·bWork 갱신 | 제작 시간·재료 소모·출력 대기는 유지 | [InputOutputModule.SetWorkAnimatorState](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InputOutputModule.cs:6044>) |
| 채굴기·전기 채굴기 | 작업 Animator | 자원 채굴·에너지 소비·출력 처리 유지 | [MiningMachine.ShouldPlayWorkAnimation](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/MiningMachine.cs:495>) |
| 파종기 | 작업 Animator | 파종 타이머·씨앗 소비·실제 심기 유지 | [SeedPlanter.ManagedUpdateTick](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/SeedPlanter.cs:66>) |
| 원유 채굴기·전기 원유 채굴기 | Animator 재생과 speed 설정 | 원유 추출·저장·에너지 계산 유지. 아래 프리팹 주의사항 참고 | [OilDrillingMachine.ApplyAnimatorPlayback](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/OilDrillingMachine.cs:375>) |
| 용광로 | 제작 파티클 시뮬레이션과 재생 상태 갱신 | 제련·연료·출력 처리 유지 | [UpdateCraftParticleEffectVisual](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/InputOutputModule.cs:6005>) |
| 증기 발전기 | Wheel 회전, 발전 파티클 | 발전량·증기/유체 소비 유지 | [UpdateGenerationVisuals](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/SteamGenerator.cs:550>) |
| 스프링클러 | 노즐 회전, 물줄기 파티클 | 물 소비·살수 주기·실제 관수 유지 | [Update](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Sprinkler.cs:136>), [SetOperating](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Sprinkler.cs:748>) |
| 모닥불 | 불 파티클 시뮬레이션 | 점화 상태·연료 소비 등 게임 상태 유지. 현재 Play/Stop 호출은 점화 변경 시점에 발생 | [OnItemLightToggleStateChanged](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Camp fire.cs:55>) |
| 증기 기관차 | 주행 연기 파티클, 급수관 시각 보간 | 열차 이동·연료/물·급수 상태 유지. 화면 밖 이동 기준 위치도 갱신 | [UpdateWaterPipeVisual](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Vehicle/SteamTrain.cs:3849>), [SetMovementParticleActive](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Vehicle/SteamTrain.cs:4021>) |
| 차량 공통: 열차·화차·레일 핸드카·손수레 | 이동 거리에 따른 바퀴 Transform 회전 | 차체 이동·충돌·경로·연결·적재 상태 유지. wheels가 등록된 차량에 적용 | [RotateWheelsByDistance](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Vehicle/Vehicle.cs:163>) |
| 벌목기 | 작업 Animator, 힌지 Transform 반영 | currentHingeAngle 갱신과 정렬 판정은 반드시 유지. 이미 논리 각도와 ApplyHingeRotation이 나뉘어 있음 | [UpdateHingeRotation](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/LoggingMachine.cs:934>) |

### 프리팹 대조 결과

- 생산기 MK1·MK2, 채굴기, 전기 채굴기, 파종기, 원유 채굴기, 전기 원유 채굴기, 벌목기에서 Animator Controller 참조를 확인했다. 해당 프리팹의 Animator 직렬화 값은 m_CullingMode: 0이다.
- 설치물 작업 애니메이션의 텍스트 .anim 파일에서 m_Events: []를 확인했다. 해당 작업은 코드의 생산 타이머와 에너지로 진행된다. 이는 프로젝트 전체의 모든 애니메이션에 이벤트가 없다는 의미는 아니다.
- 용광로 Furnace.prefab에는 particleEffect 참조와 playParticleEffectWhileCrafting: 1이 있다. 나머지 생산기 모두에 제작 파티클이 있다고 간주하면 안 된다.
- 모닥불은 별도 fireEffect, 증기 발전기와 증기 기관차는 particleEffect를 사용한다. 스프링클러 물줄기는 코드에서 생성한다.
- 원유 채굴기 두 프리팹의 pumpjackBeam/Crank/Rod 참조는 모두 fileID: 0이다. 현재 자산에서는 Animator가 우선 대상이다. [수동 펌프 LateUpdate](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/OilDrillingMachine.cs:136>)는 참조가 있는 구성에서만 추가 후보가 된다.
- 모닥불·책상에도 전기 채굴기 Override Controller 참조가 보인다. 이 참조만으로 실제 작업 애니메이션 비용을 확정하지 않는다. 클립 바인딩이 해당 모델에 유효한지 확인한 뒤 불필요한 Animator 제거 또는 정지를 판단한다.

## 2. 로직 분리 또는 별도 정책이 필요한 대상

| 대상 | 적용 가능한 시각 처리 | 바로 전체 중단하면 안 되는 이유 |
|---|---|---|
| 로봇팔 | 본체 회전, Pick/Drop Animator, 들고 있는 아이템의 시각 갱신 | [RotateBodyToward](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/RobotArm.cs:2752>)가 실제 Transform 각도로 완료를 판정하고 운반 상태를 전환한다. 논리 각도/시간을 분리해야 한다. |
| 화면 밖에서도 AI가 활동 중인 동물 | 스킨/Animator 시각 평가 | [IsReadyForAIMovement](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/Animal/Animal.cs:1549>)가 기상 애니메이션 상태에 의존한다. 통째로 멈추면 기상·이동 전환이 지연될 수 있다. 이동·사망·탑승·포획 상태는 별도 검토한다. |
| 아이템 이동 연출, 적재·연료 투입 연출 | PortableObject 이동 보간 및 모델 갱신 | [MoveTo](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/PortableObject.cs:562>)의 완료 콜백과 오브젝트 회수까지 멈추면 안 된다. 연출 생략 시에도 필요한 완료 처리를 정확히 한 번 실행해야 한다. |
| 울타리 문 | 문짝 Tween | [ApplyDoorState](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Building/FenceDoor.cs:82>)가 문 개폐와 Collider를 함께 관리한다. 힌지 하위 Collider 구성에 따라 물리 영향이 있으므로 프리팹 확인 후 논리 개폐와 시각 보간 분리 |

벌목기의 논리 각도는 이미 별도 변수에 있으므로 로봇팔보다 적용 부담이 작다. 다만 벌목기 UpdateHingeRotation 전체를 생략하는 것은 금지한다.

## 3. 추가 후보 — 반복 애니메이션 외의 시각 갱신

| 대상 | 후보 처리 | 우선도 / 조건 |
|---|---|---|
| 파이프 | 0.2초 주기 유체 표시 조회·색상/표시 갱신 | [Pipe.Update](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Pipe.cs:97>), [RefreshFluidDisplay](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Pipe.cs:733>). 표시용 경로만 생략하고 유체 네트워크 자체는 유지. 다시 보이면 최신 내용 강제 갱신 |
| 동물 체력바 | 위치 추적·카메라 방향 맞춤·UI Refresh | [AnimalWorldHealthBar.LateUpdate](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/Animal/AnimalWorldHealthBar.cs:179>). 체력과 표시 유효 상태 유지, 재진입 시 즉시 동기화 |
| 상자 | 뚜껑 Tween, 내용 아이콘/수량 표시 | [ApplyHingeRotation](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Box/BoxObject.cs:496>). 단발/이벤트 기반이므로 상시 기계 애니메이션보다 낮은 우선도. 인벤토리 상태 유지 |
| 설치된 물/기름 양동이 | 유체 수면 Transform/재질 갱신 | [RefreshInstalledFluidVisual](<C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/Bucket.cs:460>). 독립적인 매 프레임 물결 애니메이션이 아니라 충전량 갱신 시 보간 값을 적용한다. 필요할 때 최신 표시만 반영 |

AreaMarker 아이콘 회전은 설정 변경 기반이므로 지속 회전 애니메이션 후보에서 제외한다. 정적 벽·바닥·레일·전봇대·자원 모델도 별도 반복 시각 코드가 확인되지 않은 한 Animator 중단 대상을 추가하지 않는다. 일반 벨트 표면의 셰이더 흐름은 C# 작업 애니메이션과 구분한다.

## 4. 이미 적용된 부분과 적용 시 공통 조건

- 벨트/아이템 배치 렌더링과 화면 밖 일부 렌더 데이터 준비 생략은 기존 작업 범위다. 생산·운반 시뮬레이션은 계속한다.
- 거리 제한·AI 일시정지로 비활성인 동물의 Animator 중단은 이미 적용되어 있다. 화면 밖 활성 동물 전체의 Animator 중단과는 다르다.
- Disable Camera Culling == false가 컬링 켬이다. 새 시각 처리 생략도 같은 정책을 따르고, true로 바꾸면 컬링 때문에 정지한 시각 상태를 해제해야 한다.
- 같은 오브젝트의 Renderer/Animator/ParticleSystem 참조와 Bounds를 캐시한다. 프레임마다 GetComponentsInChildren, LINQ, 신규 컬렉션 할당을 추가하지 않는다.
- 화면 안팎 전환 시 Animator·파티클을 실제로 제어한다. 생산 코드가 매 틱 Play/SetBool/speed를 다시 쓰는 경로까지 함께 처리해야 한다.
- 재진입 시 과거 효과를 몰아서 재생하지 않는다. 현재 생산/운반 상태로 복원하며, 화면 밖에서 생산이 종료되었다면 효과를 재시작하지 않는다.
- 본체뿐 아니라 넓게 퍼지는 연기·물줄기, 화면에 들어오는 그림자 등 시각 범위를 고려한다. Unity 에디터 Scene 뷰의 가시성을 게임 카메라 판정으로 대신 사용하지 않는다.
- 이동·생산 컴포넌트 전체 비활성화 대신 시각 처리만 생략한다. 기존 AI 비활성화와 카메라 비가시성이 동시에 걸리는 Animator는 각각의 정지 사유가 해제되었는지 구분해야 한다.

권장 적용 순서: 생산 설비 Animator 및 파티클 공통 경로 → 분배기/스프링클러/발전기/차량의 독립 시각 처리 → 로봇팔 논리·시각 분리 → 파이프·체력바 등 추가 표시 갱신.

검증 시나리오: 화면 밖에서도 생산량·운반량 유지, 화면 밖에서 작업 종료/연료 소진 후 재진입, 이동 오브젝트의 카메라 경계 통과, 컬링 토글 복원, 동물 AI 중단과 카메라 중단 중첩, 풀 반환·재설치 시 상태 초기화. 실제 성능 개선 폭은 동일 저장 상태에서 카메라 위치만 바꾼 프로파일 비교로 확인해야 한다.
