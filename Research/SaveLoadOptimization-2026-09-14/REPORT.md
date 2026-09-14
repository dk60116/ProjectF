# 저장 및 불러오기 최적화 연구

작성일은 2026-09-14야. 현재 소스를 정적으로 분석했고 Unity나 게임 빌드는 실행하지 않았어.

## 결론

효과가 가장 클 가능성이 높은 순서는 아래와 같아.

1. 저장 틱 정지 범위를 스냅샷 수집까지만 줄여야 해.
2. 설치물 저장소의 동일 배치 검색을 인덱스로 바꿔 O(N²) 경로를 제거해야 해.
3. 파일 읽기·GZip 해제·역직렬화를 백그라운드로 옮겨야 해.
4. 중복 변환과 전체 컬렉션 복사, DTO 기본 리스트 할당을 줄여야 해.
5. 플레이어 주변 청크를 먼저 복원하고 원거리 청크는 지연 복원해야 해.
6. 그 다음에만 압축 알고리즘이나 새 저장 포맷을 비교하는 편이 좋아.

현재 저장은 바이너리와 `GZipStream(CompressionLevel.Fastest)`을 사용하고, 스냅샷 뒤 파일 쓰기는 `Task.Run`으로 분리돼 있어. 청크 생성도 코루틴과 프레임 예산을 사용해. 따라서 처음부터 직렬화 체계를 갈아엎기보다 메인 스레드 정지와 제곱 탐색을 먼저 없애는 게 맞아.

## 실측 규모 참고

기존 프로파일 자료 `Research/FrameOptimization-2026-09-13/initial-perf.json`에는 아래 규모가 기록돼 있어.

| 항목 | 수량 |
|---|---:|
| 로드 청크 | 1,310 |
| 로드 블록 | 34,656 |
| 로드 MapObject | 19,910 |
| 로드 설치물 | 2,462 |
| 로드 컨베이어 | 1,701 |
| Belt Job 레인 | 3,519 |
| 저장 컨베이어 블록/레인 | 1,434 / 2,877 |

이 정도 규모에서는 설치물당 전체 설치물 검색이나 컨베이어 엔트리당 선형 검색이 쉽게 눈에 띄는 비용이 돼.

## 확인된 병목

### 1. 저장 완료까지 시뮬레이션 틱이 계속 멈춰 있어

`SaveManager.SaveSlotRoutine`은 저장 시작 시 `MapObjectTickManager.BeginSaveTickPause()`를 호출하고, 백그라운드 쓰기 작업이 끝난 뒤 `FinishSaveOperation()`에서 정지를 풀어. 스냅샷은 이미 라이브 Unity 객체에서 분리됐다고 주석에 적혀 있으므로, 일관성에 필요한 정지 범위는 `CaptureSaveData` 전후면 충분할 가능성이 높아.

개선 방향은 아래와 같아.

- 스냅샷 수집 직전에 틱을 멈춰.
- `CaptureSaveData`가 성공하거나 실패한 즉시 `EndSaveTickPause()`를 호출해.
- 압축과 파일 쓰기는 게임 시뮬레이션이 재개된 상태에서 진행해.
- `IsSaving`은 중복 저장과 로드를 막는 용도로만 유지해.

이 변경은 저장 총시간보다 플레이 중 체감 정지를 크게 줄일 가능성이 높아.

### 2. 설치물 저장과 로드에 O(N²) 검색이 있어

`BlockStateStore.StoreInstallationState`는 매 상태 저장마다 `RemoveSavedInstallationStatesForSamePlacement`를 호출하고, 이 메서드는 `savedInstallationStates` 전체를 순회해. 로드도 `ApplyInstallationSaveStates`에서 엔트리마다 같은 저장 함수를 호출하므로 설치물 수가 늘수록 제곱으로 커져.

저장 경로는 더 중복돼 있어. `FlushLoadedRuntimeStateToStore`와 `SaveActiveRuntimeInstallations`은 같은 설치물에 `SaveInstallation`과 `RegisterLiveInstallation`을 연속 호출해. `RegisterLiveInstallation` 자체가 다시 상태를 만들고 `StoreInstallationState`를 호출하므로 상태 캡처·깊은 복사·전체 중복 검색이 두 번 발생해. 이어서 `RemoveLiveInstallationRecordsForSamePlacement`도 라이브 설치물 전체를 순회해.

2,462개 설치물 기준으로 단순 제곱은 약 606만 쌍이야. 저장 중 저장/등록 이중 호출과 saved/live 검색을 모두 합치면 실제 비교 횟수는 이보다 몇 배 커질 수 있어.

개선 방향은 아래와 같아.

- `placementSequence -> storageKey` 인덱스를 saved/live 각각 유지해.
- 런타임 오브젝트 참조가 필요한 경우 `InstallationObject -> storageKey` 인덱스도 유지해.
- 저장 키 이동이나 제거 때 인덱스를 함께 갱신해.
- 같은 설치물을 저장한 뒤 다시 등록하는 이중 호출을 하나의 `CaptureAndRegisterLiveInstallation` 경로로 합쳐.
- 배치 좌표와 점유 좌표가 바뀌지 않았다면 동적 상태만 갱신하고 좌표 매핑 전체 해제/재등록을 피해야 해.
- 로드 시 입력 목록을 한 번 정규화한 뒤 인덱스와 저장 딕셔너리를 선형 시간에 구축해.

이 항목은 저장과 불러오기 모두에 영향을 주고, 설치물 증가에 따른 확장성도 바로 개선해.

### 3. 런타임 로드의 파일 읽기와 역직렬화가 메인 스레드에서 실행돼

`SaveManager.ReloadSceneForSlot`은 씬 로드 화면을 시작하기 전에 `SaveGameBinarySerializer.ReadFromFile`을 동기 호출해. 큰 파일이면 버튼 입력 직후 화면이 멈출 수 있어.

개선 방향은 아래와 같아.

- 로드 코루틴을 만들고 로딩 표시를 먼저 보여줘.
- 파일 열기, GZip 해제, 바이너리 파싱은 `Task.Run`에서 처리해.
- Unity 오브젝트와 `ItemDefinition`을 접근하는 ID remap과 월드 적용은 메인 스레드에서 처리해.
- 로드 작업 상태를 저장 작업과 같은 방식으로 노출해 중복 요청을 막아.

### 4. 역직렬화 전에 압축 해제 데이터를 두 번 보관해

`ReadFromFile`은 GZip 전체를 `MemoryStream`으로 복사한 뒤 `ToArray()`를 호출하고, 그 `byte[]`에서 다시 `SaveGameData` 객체 그래프를 만들어. `ToArray()` 시점에는 메모리 스트림 내부 버퍼와 새 배열이 동시에 존재하고, 파싱 중에는 배열과 DTO가 함께 존재해.

개선 방향은 아래와 같아.

- 현재 버전은 `BinaryReader(GZipStream(FileStream))`에서 바로 읽어.
- v18 호환 재시도가 필요할 때만 파일을 다시 열어 두 번째 파서를 실행해.
- 파일 길이와 섹션 개수 상한을 헤더 단계에서 검증해.

이렇게 하면 압축 해제 크기에 비례하는 대형 `byte[]`와 복사를 제거할 수 있어.

### 5. 설치물 DTO 역직렬화에 빈 리스트 할당이 중복돼

`InstallationSaveState`는 생성 시 리스트 6개를 즉시 만들고, 현재 버전 역직렬화는 이 필드들을 읽은 새 리스트로 다시 덮어써. 설치물 2,462개라면 설치물 상태에서만 약 14,772개의 빈 리스트가 불필요하게 만들어질 수 있어. `InputOutputModule.PersistentState`도 리스트 6개를 기본 생성한 뒤 역직렬화에서 다시 덮어써.

개선 방향은 아래와 같아.

- 역직렬화 전용 생성자나 팩터리를 두고 기본 컬렉션 생성을 생략해.
- 빈 컬렉션은 공유 빈 읽기 전용 값이나 `null`로 표현하고 실제 수정 시에만 만들어.
- `ReadList`가 정확한 용량으로 만든 리스트를 그대로 소유하게 해.
- `Clone()`의 `source ?? new List<T>()`처럼 null일 때 중간 빈 리스트를 만드는 표현도 제거해.

### 6. 저장 스냅샷에서 전체 컬렉션 복사가 반복돼

`TerrainGenerator.FlushLoadedRuntimeStateToStore`와 `CaptureLoadedConveyorItemSaveStates`는 `loadedBlocks` 전체를 각각 새 `List<KeyValuePair<...>>`로 복사해. `BlockStateStore.CaptureSaveState`도 자원, 바닥 아이템, 컨베이어, 설치물 딕셔너리를 각각 새 리스트로 복사한 뒤 다시 DTO 리스트를 만들어.

틱 정지 중 컬렉션이 변하지 않는다는 계약을 명시할 수 있다면 직접 순회할 수 있어. 계약이 불충분하면 재사용 scratch 리스트를 소유하고 `Clear` 후 채우는 방식이 안전해. 최종 DTO는 백그라운드 쓰기 동안 독립적으로 살아야 하므로 필요한 깊은 복사까지 없애면 안 돼.

추가로 결과 리스트에 원본 딕셔너리 `Count`를 사용해 용량을 미리 잡으면 확장 복사를 줄일 수 있어.

### 7. 컨베이어 스냅샷 조립에 선형 검색과 중복 변환이 있어

`CaptureLoadedConveyorItemSaveStates`는 각 로드 컨베이어마다 `FindConveyorItemEntryIndex`로 `mapSaveData.conveyorItems`를 처음부터 검색해. 최악에는 O(B²)가 돼. `BackfillFromFloorObjects`도 같은 선형 검색을 사용해.

또한 `BackfillFromFloorObjects`는 저장 캡처 중 `BlockStateStore.CaptureSaveState` 내부와 `TerrainGenerator.CaptureMapSaveState`에서 다시 호출돼. 로드에서도 `TerrainGenerator.LoadFromSaveState`와 `BlockStateStore.ApplySaveState`에서 중복 호출돼.

개선 방향은 아래와 같아.

- 한 번 만든 `coordinate -> entry/index` 딕셔너리로 병합해.
- backfill 책임을 호환성 입력 경계 한 곳으로 모아.
- 현재 버전 저장 데이터에는 레거시 floor sentinel backfill을 수행하지 않아.
- 컨베이어 run 압축도 이미 구축한 좌표 인덱스를 재사용해.

### 8. 비어 있는 Belt Job 레인을 모두 별도 스냅샷으로 저장해

`CaptureBeltSimulationSnapshot`은 아이템이 들어 있는 레인은 `map.conveyorItems`에 있다고 보고 건너뛰지만, 나머지 빈 레인은 모두 `BeltSavedLane` 객체로 만들어. 기존 실측에는 총 3,519개 레인과 저장 아이템 레인 2,877개가 있어. 빈 레인도 합류 커서, gate, origin 같은 상태 때문에 필요할 수 있지만 완전 기본 상태까지 저장할 필요가 있는지는 구분할 수 있어.

개선 방향은 아래와 같아.

- 기본 빈 상태와 다른 레인만 기록해.
- 그룹 단위 합류 커서처럼 레인별로 반복되는 상태는 그룹 섹션으로 분리해.
- 생략된 레인은 토폴로지 재구축 기본값으로 복원해.

이 변경은 반드시 BeltJobs 하네스의 저장 후 연속 계산과 결정론 검사를 통과해야 해.

### 9. 설치물 저장 포맷이 모든 타입의 필드를 매번 써

`WriteInstallationState`는 일반 벨트에도 철도, 박스, 필터, 유체, 기차, 벌목기, 전신주 관련 기본 필드를 모두 써. GZip이 반복 기본값을 잘 압축해도 쓰기/읽기 호출과 DTO 필드·빈 리스트 비용은 남아.

새 버전에서는 아래 구조가 더 적합해.

- 공통 헤더: ID, anchor, 회전, placement sequence, 점유 좌표
- 기능 비트마스크: IO, 저장고, 필터, 유체, 철도, 차량, 분배기, 전신주 등
- 비트가 있는 컴포넌트 payload만 기록
- 섹션 길이를 기록해 이후 버전이 모르는 payload를 건너뛸 수 있게 구성

현재 단계에서 기존 세이브 호환성이 깨져도 되므로, 긴 레거시 분기를 유지하는 비용보다 새 포맷과 변환 도구를 명확히 분리하는 편이 좋아.

### 10. 모든 저장 활성 청크가 월드 준비 조건에 묶여 있어

저장은 모든 `loadedChunks` 좌표를 정렬해 기록하고, 로드는 그 좌표를 모두 큐에 넣어. `TryFinalizePendingWorldLoad`는 청크 스트리밍이 완전히 끝날 때까지 완료되지 않아. 저장된 좌표 정렬은 y/x 순서라 플레이어 주변 우선순위도 아니야.

기존 실측처럼 활성 청크가 1,310개면 로딩 화면 시간이 길어질 수 있어. 각 청크를 한 프레임에 격리하는 현재 정책은 프레임 스파이크를 줄이지만 전체 대기시간은 줄이지 못해.

개선 방향은 아래와 같아.

- 플레이어 현재 청크와 load radius 내부를 거리순으로 먼저 큐잉해.
- 이 핵심 영역의 설치물·컨베이어·철도 연결이 끝나면 플레이 가능 상태로 전환해.
- 원거리 저장 청크는 백그라운드 스트리밍하거나, 상태 저장소만 유지하고 접근 시 생성해.
- 탐험 여부가 필요하면 `active chunks`와 별도 `explored chunks` 데이터를 둬.

철도나 전력망처럼 원거리 토폴로지가 즉시 필요하면 데이터 전용 네트워크 상태를 먼저 복원하고 GameObject/메시는 지연 생성하는 식으로 책임을 나눠야 해.

### 11. 아이템 ID remap 조회를 캐시할 수 있어

현재 stable name 조회는 저장 카탈로그와 설치물마다 현재 정의 목록을 선형 순회하고 문자열 정규화와 설치물 prefab 검사를 반복해. 현재 정의에서 아래 맵을 로드당 한 번 만들면 돼.

- 정규화 이름 -> ItemDefinition
- 정규화 설치물 이름 -> ItemDefinition
- 이전 ID -> 현재 ID

이 최적화는 위의 O(N²) 설치물 경로보다 우선순위가 낮지만 구현 위험은 작아.

## 권장 구현 순서

### 0단계: 계측 하네스부터 추가해

아래 구간에 시간, 할당량, 엔트리 수를 기록해.

- Save: tick pause, runtime flush, state-store capture, conveyor merge/run 압축, player capture, binary write, GZip, file replace
- Load: file read, GZip, binary parse, item remap, state-store apply, 핵심 청크 ready, 전체 청크 ready, conveyor/철도 최종화
- 카운터: 설치물 중복 비교 수, live/saved 전체 순회 수, 생성 DTO/리스트 수, 원시/압축 파일 바이트

합성 데이터는 설치물·컨베이어·바닥 스택을 100/1,000/10,000 단위로 늘려 선형성을 확인해. 실제 저장 파일도 작은 월드, 현재 2,462 설치물 월드, 대형 스트레스 월드로 나눠 A/B/A 비교해.

### 1단계: 낮은 위험의 즉시 개선을 적용해

- 스냅샷 직후 틱 정지를 풀어.
- 로드 파일 파싱을 백그라운드로 옮겨.
- GZip 스트림에서 직접 읽어.
- backfill 중복 호출을 제거해.
- 컨베이어 좌표 인덱스를 사용해.
- DTO 리스트 용량 예약과 역직렬화 전용 생성을 적용해.

### 2단계: 설치물 저장소를 선형화해

- placement sequence와 런타임 객체 인덱스를 추가해.
- Save + Register 이중 캡처를 합쳐.
- 정적 좌표 매핑과 동적 상태 갱신을 분리해.
- 로드 입력을 정규화한 뒤 bulk build해.

이 단계가 실제 총시간 개선의 핵심일 가능성이 높아.

### 3단계: 플레이 가능 로드를 분리해

- 플레이어 주변 핵심 청크 우선순위를 도입해.
- 핵심 ready와 전체 background ready를 분리해.
- 원거리 설치물은 데이터 전용 상태로 유지해.

### 4단계: 새 포맷과 증분 저장을 검토해

- 설치물 기능 비트마스크와 선택 payload를 도입해.
- 바닥 아이템 RLE를 DTO에서 풀지 않고 파일까지 유지해.
- 섹션별 독립 압축과 목차를 두어 필요한 청크만 읽게 해.
- 그 다음 dirty chunk journal 또는 base snapshot + delta를 검토해.

압축기는 같은 데이터로 GZip Fastest, 무압축, LZ4 계열을 비교한 뒤 결정해야 해. CPU 시간, 파일 크기, peak memory, 플랫폼 지원을 함께 봐야 해.

## 검증 조건

최적화는 아래 동작을 보존해야 해.

- 저장 시점의 simulation tick과 설치물 next ID가 동일하게 복원돼야 해.
- 아이템 ID remap 후 자원, 바닥, 박스, IO, 필터, 컨베이어, 플레이어 인벤토리 수량이 보존돼야 해.
- 설치물 placement sequence와 점유/상호작용 좌표 인덱스가 중복 없이 복원돼야 해.
- 철도 차량 위치, 역 이름/색상, 자동 운전 목표와 연료/화물 필터가 보존돼야 해.
- 컨베이어 아이템 순서, lane, 남은 이동, 합류 cursor, 분배기 교대 상태가 보존돼야 해.
- 저장 직후 게임 틱을 재개해도 백그라운드 writer가 라이브 상태를 참조하지 않아야 해.
- 파일 교체 도중 종료돼도 이전 정상 세이브나 새 정상 세이브 중 하나는 남아야 해.

기존 `SplitterSaveHarness`, `SaveItemIdRemapHarness`, `FreightCarPersistenceHarness`, `ResourceDepletionSaveHarness`, `BeltJobsHarness`, `ConveyorTransportHarness`, `RobotArmIoHarness`를 회귀 세트로 묶는 게 좋아. 여기에 전체 `SaveGameData` 왕복과 설치물 bulk-index 동등성 하네스를 추가해야 해.

## 안전성과 파일 교체

현재 writer는 임시 파일을 쓴 뒤 기존 파일을 삭제하고 임시 파일을 이동해. 삭제와 이동 사이에 실패하면 기존 정상 세이브가 사라질 수 있어.

성능 작업과 함께 아래를 적용하는 게 좋아.

- 같은 디렉터리의 임시 파일에 완전히 쓰고 flush/close해.
- 기존 파일이 있으면 원자적 replace를 사용하고, 플랫폼별 실패 시 백업 파일을 유지해.
- 헤더에 payload 길이와 checksum을 넣어 부분 파일을 거부해.
- 성공 뒤 오래된 `.tmp`와 `.bak`을 정리해.

이건 속도 향상보다 저장 신뢰성 개선이지만, 비동기 저장 범위를 넓힐 때 같이 처리해야 해.

## 최종 판단

첫 구현 묶음은 `틱 정지 범위 축소 + 설치물 인덱스 + 비동기 스트리밍 로드 + 중복 backfill 제거 + 계측`이 적절해. 새 압축 포맷이나 증분 저장은 이 묶음의 A/B 결과를 본 뒤 진행해야 해. 현재 구조에서는 압축기 교체보다 메인 스레드 정지와 설치물 제곱 검색을 없애는 쪽이 훨씬 직접적인 개선이야.
