# Conveyor line entity-cache harness

```powershell
./Tools/ConveyorLineCacheHarness/Run.ps1
```

실제 `BlockDataStore.cs` 전체와 현재 `TerrainGenerator`의 `ConveyorLine`, `TryResolveLoadedRuntimeBlock`, `TryResolveConveyorLineBlock`을 추출해 .NET에서 실행해. 최적화 직전의 두 resolver는 `LegacyResolver.cs`에 고정되어 있어. Block은 일반 C# 엔티티 대역이고 엔진이나 실행 중인 게임에는 연결하지 않아.

같은 엔티티와 같은 변경 이력을 가진 두 실제 저장소를 사용해 기존 resolver와 캐시 resolver의 반환값·out 참조·엔티티 수·청크 수·등록 셀 수·셀 플래그가 일치하는지 비교해. 해제된 관리 엔티티가 null처럼 무효화되고 저장소에서 지연 정리되는 계약도 검사해.

검증 범위:

- 정상 line의 빈 캐시 lazy 초기화, 길이 확장·축소, 같은 슬롯 인덱스의 handle 교체
- 음수 좌표, 빈 line, null line, 잘못된 슬롯, 기본 handle
- 월드 활성화/비활성화
- 동일 엔티티 재바인딩, 다른 엔티티로 교체, 제거 후 기존 handle 사용과 재바인딩
- 해제된 관리 엔티티의 null 호환 비교와 저장소 지연 정리
- 청크 unload/reload와 generation 차이, 전체 Clear, ConfigureChunkSize 변경/동일 값
- 같은 handle을 발급한 다른 저장소에서 캐시 반입, 다른 저장소 변경의 격리
- 엔티티 없는 등록 셀, 미등록 청크, 누락 셀 100개 일괄 조회
- 생성·제거·교체·해제·비활성화·unload·Clear를 섞은 결정적 400단계 변경 이력

`entityCache` 필드는 프로덕션에서 항상 빈 배열로 초기화돼. 정상 생성 경로에 없는 배열 null 주입은 검증 대상에 포함하지 않아.

성능 검증은 기존 `TryGetValue(BlockHandle, out Block)`의 임시 하네스 복사본에만 조회 카운터를 주입해. 안정된 100개 슬롯을 100회 순회하면 기존 경로는 10,000회, 준비된 캐시 경로는 0회 호출해. 준비된 캐시 순회의 관리 힙 할당은 0바이트야. 실제 게임의 FPS 개선량을 뜻하지 않아.

기준 실행 결과: **5,842개 검사 통과**.
