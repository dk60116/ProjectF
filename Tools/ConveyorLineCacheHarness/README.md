# Conveyor line runtime-proxy cache harness

```powershell
./Tools/ConveyorLineCacheHarness/Run.ps1
```

실제 `BlockDataStore.cs` 전체와 현재 `TerrainGenerator`의 `ConveyorLine`, `TryResolveLoadedRuntimeBlock`, `TryResolveConveyorLineBlock`을 추출해 .NET에서 실행합니다. 최적화 직전의 두 resolver는 `LegacyResolver.cs`에 고정되어 있습니다. Unity의 파괴된 오브젝트 null 비교와 `activeInHierarchy`만 작은 대역으로 제공합니다. 엔진이나 실행 중인 게임에는 연결하지 않습니다.

같은 프록시와 같은 변경 이력을 가진 두 실제 저장소를 사용하여, 기존 resolver와 캐시 resolver의 반환값·out 참조·프록시 수·청크 수·등록 셀 수·셀 플래그가 일치하는지 비교합니다. 파괴된 객체를 처음 읽을 때에는 false 반환과 함께 파괴된 참조가 out에 남고 저장소가 정리되는 동작도 비교합니다.

검증 범위:

- 정상 line의 빈 캐시 lazy 초기화, 길이 확장·축소, 같은 슬롯 인덱스의 handle 교체
- 음수 좌표, 빈 line, null line, 잘못된 슬롯, 기본 handle
- 자기 오브젝트 및 부모·조상 비활성화/활성화
- 동일 프록시 재바인딩, 다른 프록시로 교체, 제거 후 기존 handle 사용과 재바인딩
- Unity fake-null 파괴와 실제 저장소의 지연 정리
- 청크 unload/reload와 generation 차이, 전체 Clear, ConfigureChunkSize 변경/동일 값
- 같은 handle을 발급한 다른 저장소에서 캐시 반입, 다른 저장소 변경의 격리
- 프록시 없는 등록 셀, 미등록 청크, 누락 셀 100개 일괄 조회
- 생성·제거·교체·파괴·비활성화·unload·Clear를 섞은 결정적 400단계 변경 이력

`runtimeBlocks` 필드는 프로덕션에서 항상 빈 배열로 초기화됩니다. 정상 생성 경로에 없는 배열 null 주입은 검증 대상에 포함하지 않습니다.

성능 검증은 기존 `TryGetValue(BlockHandle, out Block)`의 임시 하네스 복사본에만 조회 카운터를 주입합니다. 안정된 100개 슬롯을 100회 순회하면 기존 경로는 10,000회, 준비된 캐시 경로는 0회 호출합니다. 준비된 캐시 순회의 관리 힙 할당은 0바이트입니다. 실제 게임의 FPS 개선량을 뜻하지 않습니다.

기준 실행 결과: **5,854개 검사 통과**.
