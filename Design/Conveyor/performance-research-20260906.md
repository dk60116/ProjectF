**벨트 60FPS 최적화 조사 — 2026-09-06**

벨트 2,283개, 로드된 벨트 아이템 2,436개인 장면에서 가장 먼저 줄여야 할 비용은 실행 큐의 반복 처리, 정지 부품의 Transform 추적, 아이템 렌더 캐시의 전체 검사다. 현재의 메쉬 일괄 렌더링은 이미 작동하고 있다. 작은 검색 최적화만으로 60FPS를 확보하기에는 부족하다.

근거는 [사용자 제공 스냅샷](C:/Git/ProjectF/Design/Conveyor/performance-snapshot-20260906-011222.txt), 로컬 소스, 엔진 없이 실행한 관리형 재현이다. 기준 소스 커밋은 `25d0bda`이며 이전 작업의 ConveyorBelt / VirtualConveyorBeltRenderer 미커밋 변경도 함께 확인했다. 스냅샷에는 빌드 식별자가 없어 실행 파일에 이 변경이 포함됐는지는 확정하지 않았다. 이번 연구에서는 게임 코드를 추가 수정하거나 엔진을 실행하지 않았다.

**수치의 해석**

보고된 FPS는 42.151, 프레임 시간은 23.724ms다. 60FPS 예산 16.667ms에 맞추려면 이 프레임 시간 기준 약 7.057ms, 29.75%를 줄여야 한다. Main Thread는 최근 5개 표본 평균 21.015ms다. CPU 메인 스레드가 우선 조사 대상이지만 GPU 병목이 없다고 확정할 수는 없다. FrameTiming 표본이 없고 GPU Recorder는 0을 반환한다.

스냅샷 집계 시간은 1,016.083ms, 벨트 프로파일 프레임은 44개다. 아래 값은 `TotalMs / 44`이며, 호출당 AvgUs와 구분했다. FPS와 Recorder의 집계 창은 이 44프레임과 정확히 일치하지 않는다.

| 구간 | ms / 프로파일 프레임 | 해석 |
|---|---:|---|
| Active Belt Tick | 7.949 | 시뮬레이션 갱신의 주요 비용 |
| └ Process Wake Queue | 7.855 | 위 행에 포함되므로 더하지 않음 |
| └ Wake Line / Block / Corner Group | 3.208 / 3.006 / 1.573 | Wake Queue 내부 비용 |
| Virtual Belt Render | 3.099 | Transform 동기화와 배치 렌더 호출을 합친 값 |
| Conveyor Item Rebuild | 1.590 | 매 프레임 전체 메쉬를 새로 만든다는 뜻은 아님 |
| └ Reconcile Static Cache | 1.374 | Rebuild에 포함. 실제 실행된 13회는 평균 4.652ms, 최대 6.035ms |
| Conveyor Item Render | 0.418 | 아이템 렌더 제출 비용 |
| Robot arm | 0.813 | 19개 로봇팔의 합계를 프레임으로 환산 |
| Belt Data Motion | 0.073 | 현재 이동 보간 계산은 주요 병목이 아님 |

벨트 갱신·벨트 렌더·아이템 Rebuild·아이템 Render의 중복 없는 합은 약 13.056ms다. 여기에 다른 게임 시스템과 엔진 비용이 들어간다. 전체 스냅샷 행을 단순 합산하면 중첩 계측 때문에 과대 계산된다.

**1. 정지한 기어까지 추적하고 있다 — 가장 먼저 분리해서 측정할 수정**

스냅샷에는 가상 벨트 Transform 추적 항목이 9,181개인데 실제 행렬 갱신은 0개다. 일반 벨트 프리팹에는 Gears 아래 부품 4개가 있으며, [IsTrackedVirtualRenderer](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/ConveyorBelt.cs:2015)는 실제 애니메이션 여부와 관계없이 Gears / Gear 이름으로 추적을 켠다. [SyncTrackedTransformMatrices](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/VirtualConveyorBeltRenderer.cs:557)는 이 항목들의 localToWorldMatrix를 매 프레임 읽고 비교한다. 추적 항목 수는 고유 Transform 수와 반드시 같지는 않다.

따라서 앞서 적용한 ‘추적 항목이 있는 벨트만 순회’는 이 장면에서 효과가 제한된다. 일반 벨트 대부분이 이미 기어 때문에 그 목록에 들어가기 때문이다. 정지해도 행렬 조회 비용은 남는다. 가상 벨트 등록 2,269개 중 숨겨진 소스 오브젝트는 총 2,009개뿐이고, 활성 벨트 Transform도 26,615개 남아 있다.

개선은 일반 기어를 기본적으로 정적 배치에 넣고, 실제로 회전시키는 코드가 있는 부품만 명시적으로 추적하는 것이다. 설치·회전·메쉬 교체 때 정적 행렬을 다시 등록하고, BeltTop 흐름은 기존 셰이더를 유지한다. 분배기 Wheel은 현재 네이티브 렌더 경로이므로 정적 일반 기어와 분리해서 보존할 수 있다. 추후 기어 애니메이션을 추가할 때도 이름 판별 대신 애니메이션 소유자가 추적을 요청하도록 한다.

확인할 지표는 `TrackedTransformEntries`, 실제 행렬 조회 횟수, 갱신 횟수다. Virtual Belt Render에 Sync와 RenderBatches의 개별 계측을 추가해야 3.099ms 중 얼마가 절약되는지 알 수 있다. 3.099ms 전체가 사라진다고 계산하면 안 된다. 네 방향, 코너 End, T자 Seam/Top, 1F+2F, 분배기 Wheel, 숨김 해제와 풀 재사용을 함께 검증해야 한다.

**2. 실행 큐가 실제 이동량보다 훨씬 많이 돈다 — 가장 큰 개선 대상**

일반 이동 API 호출은 프레임당 334.136회, 성공은 0.5회로 호출 성공 비율은 약 0.15%다. 다만 이 호출에는 빈 슬롯·아직 준비되지 않은 슬롯·스로틀로 조기 반환한 경우도 포함되므로 실패 99.85%를 전부 막힘으로 해석하면 안 된다. 실제 이동 계획 호출은 23.523회, 계획 적용은 1.682회다. 성공 한 번이 여러 아이템을 옮길 수 있어 이것만으로 아이템 처리량을 계산할 수도 없다.

마지막 프레임의 블록/코너 Tick 시도 222회 중 120회는 같은 프레임의 중복 실행이라 건너뛰었다. Block Tick 79회는 전부 진행이 없어 재등록을 생략했고, Corner 쪽도 진행 없는 경우가 22회다. 이 값들은 마지막 한 프레임 수치이며 44프레임 평균과 구분해야 한다.

코드에서 확인한 구체적인 문제와 후보는 다음과 같다.

| 경로 | 확인한 동작 | 개선 방향 |
|---|---|---|
| [WakeConveyorNetwork](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:2088) → [지연 저장](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:439) → [Flush](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:625) | `queueWake=false` 정보가 지연 저장에서 사라지고 Flush가 항상 QueueConveyorWake를 호출한다. 관리형 재현으로 확인했다. | 지연 요청에도 큐 등록 의도를 보존한다. 같은 블록에 true/false 요청이 섞이면 true를 OR 병합한다. |
| [ProcessQueuedConveyorBlockWake](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:2656) | Direct Wake는 현재 블록과 앞뒤 블록의 대기/스로틀 상태를 해제한다. | 실제 점유 변화에 의한 깨우기와 단순 재확인 요청을 구분한다. 재확인이 이웃의 막힘 상태까지 지우지 않게 한다. |
| [직선 경로의 Direct Fallback](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:3313) | `ShouldTickActiveConveyor=false`여도 아이템이 있으면 Direct Wake로 보낸다. Direct Wake는 직선 재시도 상태를 지운다. | 아이템 존재 대신 실제 이동 가능 시각·대기 원인·런타임 장애 원인으로 fallback 범위를 제한한다. |
| [Corner Group 처리](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:2719) | 이미 이번 프레임 처리한 블록이면 다음 QueueConveyorWake를 요청한다. 같은 그룹도 처리 중 다시 등록될 수 있다. | 같은 프레임의 새 점유 변경을 보존하면서 동일 요청은 다음 프레임의 한 건으로 합친다. |

현재 코드에 지연 큐 의도 누락이 있다는 사실은 확정됐지만, 그것이 7.855ms 중 차지하는 비중은 아직 모른다. QueueReason별 요청·중복 병합·실행·이동 성공 수를 계측해야 한다. 큐 길이를 512에서 더 낮추는 방식은 작업을 뒤로 미루어 벨트 속도와 아이템 간격을 악화시킬 수 있으므로 우선 해법으로 삼지 않는다. 마지막 프레임 처리 수는 324건으로 이미 512건 한도보다 적다.

Safety Scan은 코드상 기본 0.25초 간격이고 스냅샷의 29건은 마지막 프레임 값이다. 매 프레임 29개를 강제로 깨운다고 해석하면 안 된다. Safety Scan 자체 비용도 0.078ms/프레임으로 작다. 실제 빈칸 이벤트가 누락되던 과거 버그가 있으므로 Safety Scan을 끄기 전에 모든 회수·투입·분배·언로드 이벤트의 깨우기 경로부터 검증해야 한다.

목표 구조는 막힌 슬롯은 목적지 빈칸 알림을 기다리고, 이동 중인 슬롯은 완료 시각에만 준비 상태를 확인하는 것이다. 이미 있는 blocked waiter, retry, ready-delay 경로를 활용하고 동일 의미의 새 대기 상태를 추가하지 않는다. NetworkSleeping=0만으로 네트워크 전체 수면 기능을 원인으로 단정할 수도 없다. 일부가 계속 움직이는 네트워크는 정상적으로 깨어 있어야 한다.

**3. 아이템 한 칸의 변화가 전체 캐시 검사를 유발한다 — 순간 끊김과 평균 비용**

[RefreshActiveVirtualConveyorRenderBlocksIfNeeded](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/PortableItemRenderer.cs:703)는 아이템이 있는 블록 집합의 버전이 달라지면 전체 목록을 복사하고 lookup을 다시 만든다. 이어 [ReconcileActiveVirtualConveyorRenderBlockCaches](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/PortableItemRenderer.cs:771)가 캐시 전체와 활성 블록 전체를 순회한다.

스냅샷의 활성 아이템 렌더 블록은 1,246개다. 이 전체 검사는 약 1초에 13회 발생했고 호출당 평균 4.652ms, 최대 6.035ms였다. 평균 프레임 비용으로는 1.374ms이며 전체 Rebuild 1.590ms의 약 86%다. Dirty Cache 갱신은 0.013ms에 불과하다. 개별 아이템 보간이나 동적 아이템 렌더링보다 집합 변경에 따른 전체 검사부터 줄여야 한다.

기존 Track/Untrack이 이미 changed handle을 dirty 집합에 넣으므로 이를 추가·제거·변경분으로 소비하는 경로를 우선 검토한다. 초기 로드, 월드 전환, 렌더 에셋 교체 때만 전체 reconciliation을 하고 평소에는 변경된 handle만 반영한다. 같은 프레임에 추가 후 제거된 경우는 최종 상태로 합친다. BlockHandle 세대와 청크 언로드, 아이템 개수 0↔1, 정적↔CPU 동적 전환을 함께 처리해야 한다. ‘정적 캐시’에도 GPU에서 움직이는 아이템이 있으므로 이름만 보고 이동 중 아이템을 제거해서는 안 된다.

**4. 코너 큐의 리스트를 버렸다가 다시 만든다 — GC 감소 작업**

[TryQueueConveyorCornerGroupWake](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:797)는 그룹 목록이 없으면 새 List를 만든다. 그룹 처리 시 딕셔너리에서 목록을 제거하고 Clear만 한다. 다음 요청은 새 List와 내부 배열을 만든다. 목록을 재사용하지 않는 동작을 관리형 재현에서도 확인했다.

집계상 그룹 처리 호출은 프레임당 약 110회다. 이 숫자가 정확한 할당 횟수라는 뜻은 아니지만 고빈도 할당 경로가 존재한다. 그룹별 reusable pending/processing 목록을 두거나 작은 풀로 반환하는 방식이 적합하다. 처리 중 같은 그룹에 새 요청이 들어올 수 있으므로 하나의 목록을 무조건 Clear해서는 안 된다. 이 작업은 요청 의미 정리 뒤에 적용하면 상태 관리가 단순해진다.

GC 카운트는 집계 구간 중 증가했지만 수집 시간의 최근 Recorder 표본은 0ms다. 현재 자료로 GC를 주 병목이라 단정하거나 세 generation 증가를 세 번의 독립 수집으로 계산하지 않는다.

**5. 후순위 항목과 계측의 빈틈**

네이티브 벨트 렌더러 23,425개 중 활성·활성화된 것은 64개이고, 벨트 가상 배치는 117개다. 아이템 쪽 배치 추정 DrawCalls는 275개다. `EstimatedDrawCallCount`는 화면 밖 배치도 포함할 수 있으므로 실제 GPU 제출 수와 같지 않다. 아이템 렌더 제출 자체는 0.418ms라 배치 수만 보고 셰이더/메쉬를 먼저 전면 교체할 근거는 약하다.

BRG 배치는 0개다. [BRG 호환 검사](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Rendering/VirtualRenderBatchRendererGroupBackend.cs:87)는 명시적 호환 태그와 DOTS 키워드를 요구하고 GPU conveyor motion 등 일부 경로를 제외한다. 프로젝트 Assets 셰이더 검색에서는 해당 명시적 호환 선언을 찾지 못했다. 플랫폼 지원 여부나 외부 셰이더도 있어 0의 유일 원인이라고 확정할 수는 없다. BRG 전환은 호환 셰이더/Player 변형/UV·모션 레이아웃을 검증할 별도 작업이며 스위치 하나로 켤 대상이 아니다.

벨트 활성 Transform 26,615개와 Block 컴포넌트 11,485개는 이후 구조 경량화 후보다. 다만 정적인 활성 Transform 수 자체가 현재 프레임 비용이라는 증거는 없다. 로직과 View의 생명주기를 분리한 뒤 정적 하위 구조를 숨기거나 축소하는 순서가 맞다. 분배기의 충돌·포커스·Wheel을 위해 필요한 루트를 무작정 꺼서는 안 된다.

프로파일러 자체도 비용이 있다. [스냅샷 생성](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Manager/GameManager.cs:1956)은 [CountRuntimeComponents](C:/Git/ProjectF/FactorioProject/Assets/Scripts/Map/TerrainGenerator.Conveyors.cs:4746)를 통해 전체 MapObject 하위 Transform/Renderer/Collider를 조회한다. 목록 버퍼는 재사용하지만 전체 순회 비용은 남는다. 외부 도구 기본 폴링은 1초다. 이 스냅샷 생성 시간은 현재 상위 표에 별도 계측되지 않는다. 정적 구조 카운트는 저빈도 수집하거나 캐시하고, 경량 프레임 계측과 상세 스냅샷을 분리할 필요가 있다. 평균 FPS 저하 전부를 프로파일러 탓으로 돌릴 자료도 없다.

**실행 순서와 완료 기준**

| 순서 | 작업 | 완료 판정 |
|---|---|---|
| 0 | 빌드 식별자·30~60초 프레임 분포, Sync/Render 분리, wake 원인과 no-progress 이유 계측 | 동일 세이브/카메라에서 계측 ON/OFF 비교. CPU/GPU 누락 수치는 별도로 표시 |
| 1 | 일반 정적 Gear의 상시 Transform 추적 제거 | 정적 장면 조회 수가 실제 애니메이션 수에 비례. End/Seam/Top와 Wheel 정상 |
| 2 | 지연 queueWake 의도 보존, 중복 wake 병합, 불필요한 Direct Fallback 제한 | 정체 상태의 반복 실행 감소. 새 빈칸은 다음 가능한 시뮬레이션 처리에서 즉시 반영 |
| 3 | 아이템 렌더 집합/캐시를 변경분만 갱신 | 한 칸의 변경이 전체 캐시 재검사를 유발하지 않음. 언로드·정적/동적 전환 누락 없음 |
| 4 | 코너 pending 목록 재사용, 프로파일러 상세 카운트 저빈도화 | 안정 구간에서 관련 큐의 불필요한 GC 할당 제거 |
| 5 | 남은 CPU/GPU 결과에 따라 BRG·정적 메쉬 합치기·View 경량화 | 실제 개선 폭을 측정한 작업만 확대 |

초기 성능 예산 목표는 Active Belt Tick ≤3.5ms, Virtual Belt Render ≤1.0ms, Item Rebuild ≤0.3ms다. 현재값 대비 이 세 항목에서 약 7.84ms를 줄여야 하는 목표이며, 달성 예측이나 측정된 개선량이 아니다. 최종 목표는 로드가 안정된 동일 장면 60초에서 평균 14~15ms 여유와 p99 ≤16.667ms다. 저장·대규모 청크 로딩은 별도 시나리오로 계측한다.

회귀 검증은 전체 정체 → 중간 아이템 회수, 빈 목적지 생성, 로봇팔 투입·회수, 직선/코너/T자/1F+2F, 분배기 양쪽 교대와 필터, 연속 이동 중 간격 유지, 청크 언로드·재로드를 포함한다. 전체 정체 장면과 정상 최대 유량 장면을 각각 검사해 처리량을 낮춰 얻은 FPS 개선을 배제한다.

**이번에 실행한 재현**

[Probe-WakeQueue.ps1](C:/Git/ProjectF/Tools/ConveyorResearchHarness/Probe-WakeQueue.ps1)은 실제 소스의 다섯 메서드를 그대로 추출해 관리형 월드 대역과 컴파일한다. Unity 실행이나 조작은 하지 않는다.

```powershell
& C:/Git/ProjectF/Tools/ConveyorResearchHarness/Probe-WakeQueue.ps1
```

| 경우 | 예상 Queue 호출 | 실제 |
|---|---:|---:|
| 즉시 처리, queueWake=false | 0 | 0 |
| 즉시 처리, queueWake=true | 1 | 1 |
| 지연 처리, queueWake=false | 0 | 1 |
| 지연 처리, queueWake=true | 1 | 1 |

현재 코드에서는 의도 보존 검사 한 건이 실패하므로 종료 코드 1이 정상적인 버그 재현 결과다. 코너 목록은 처리 후 다음 등록에서 재사용되지 않았다. 이 재현은 큐 의도 누락과 목록 수명만 확인하며, 실제 게임에서 발생하는 빈도·FPS 영향·시뮬레이션 정확도까지 증명하지 않는다. 최적화 구현과 동일 장면의 실측은 후속 작업이다.
