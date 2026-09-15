# Block MonoBehaviour → ECS 엔티티 전환

작성일: 2026-09-15

## 결과

좌표마다 생성하던 `Block` MonoBehaviour를 제거했어. 런타임 `Block`은 이제
`BlockDataStore`의 generation-checked `BlockHandle`이 소유하는 일반 C# 엔티티야.
청크 셀은 GameObject, Transform, MonoBehaviour를 소유하지 않아.

프리팹의 직렬화 설정은 `BlockTemplate` 컴포넌트로 분리했어. 이 컴포넌트는
런타임 좌표마다 복제되지 않고, 새 엔티티를 만들 때 설정값만 복사해.

## 구조 변경

- `proxyHost.AddComponent<Block>()`를 `new Block()`과 `BlockDataStore.BindEntity()`로 교체했어.
- 64개 `Block Runtime Proxies` 호스트와 생성·파괴 경로를 제거했어.
- `BlockDataStore`의 `HasRuntimeProxy`, `RuntimeProxies`, `RuntimeProxyCache` 명칭과
  상태를 `HasEntity`, `Entities`, `EntityCache`로 통일했어.
- 블록 활성 상태와 레이어는 Block GameObject가 아니라 TerrainGenerator의 명시적
  런타임 경계를 통해 조회해.
- 프리팹의 기존 Block 스크립트 참조를 `BlockTemplate`로 교체했어.
- 사용되지 않던 `BlockPool` 직렬화 shim을 제거했어.
- 프로파일러는 `BlockComponents=0`, `BlockHostGameObjects=0`,
  `BlockEntities=<실제 엔티티 수>`를 출력해.

## SaveSlot1 예상 영향

SaveSlot1에서 초기 Block 엔티티 조건을 만족하는 좌표 합집합은 약 22,653개야.
이전에는 이 수에 가까운 `Block` MonoBehaviour가 생성됐지만, 전환 후에는 관리
엔티티만 생성돼. 따라서 장면의 MonoBehaviour 수는 약 2.2만 개 감소하는 것이
정상이고, Block 때문에 생성되는 GameObject는 0개가 정상값이야.

PortableObject 3,773개와 일반 설치물 View는 이번 범위에 포함하지 않았어.
따라서 전체 GameObject 수가 0이 되거나 모든 설치물 MonoBehaviour가 없어지는
변경은 아니야.

## 검증

- Assembly-CSharp 보충 참조 빌드: 경고 113 / 오류 0
- 컴파일된 `Block` 기반 형식: `System.Object`
- 컴파일된 `BlockTemplate` 기반 형식: `UnityEngine.MonoBehaviour`
- SimulationCoreHarness: 249개 통과
- BeltJobsHarness: 3,087개 통과
- WorldPersistenceHarness: 52개 통과
- ConveyorLineCacheHarness: 5,842개 통과, warm allocation 0 B
- VirtualObjectWorldHarness: 20개 통과
- AnimalAIOptimizationHarness: 68개 통과
- WorldVisualUpdateHarness: 27개 통과
- CheckBoundaries: Block 엔티티/authoring template 경계 통과

Unity 에디터·게임은 실행하지 않았어. 실제 SaveSlot1 로드 후 장면 카운터와
설치/회수/컨베이어/농지 상호작용은 Unity 런타임에서 확인해야 해.

`ConveyorTransportHarness`의 저장소 단독 검사는 11,041,204개 assertion을 통과했지만,
후속 추출형 world probe는 기존 소스 추출 계약이 현재 생산 코드와 맞지 않아 컴파일에
실패했어. 이번 Block 기반 형식 변경에서 발생한 Assembly-CSharp 컴파일 오류는 없어.
