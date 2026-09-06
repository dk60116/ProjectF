# MapObjectProfiler 동등성 하네스

프로덕션 `MapObjectTickProfiler` 전체를 직접 추출해 실행합니다. Unity API는 작은 데이터 스텁으로, Stopwatch는 결정적 시계로 대체합니다. 게임 엔진이나 실행 중인 빌드에는 연결하지 않습니다.

```powershell
./Tools/MapObjectProfilerHarness/Run.ps1
```

검증 범위:

- 변경 전 코드에서 저장한 129개 스냅샷과 JSON 전체를 바이트 단위로 비교합니다.
- 활성 대상 추가·제거, 활성 상태만 있는 그룹, 샘플만 있는 그룹, 같은 종류의 여러 아이템, 그룹 풀 재사용을 확인합니다.
- 빈 컬렉션·null 대상, 이름 정규화·이스케이프, 행 제한, 정렬, 샘플 합계·평균·최댓값, 측정 창 초기화를 확인합니다.
- 프로파일러 켜기·끄기·Reset, 벨트 프레임 중복 집계 방지, 음수 입력 보정, 런타임 카운터를 확인합니다.
- 준비가 끝난 HashSet 활성 대상 순회를 1,000회 반복해 관리 힙 할당이 0바이트인지 확인합니다. 변경 전 동일 경로는 328,000바이트를 할당했습니다.
- 스텁의 `ManagedUpdateTick`는 호출되면 실패하므로, 통계 수집이 실제 오브젝트 동작을 실행하지 않는지도 확인합니다.

`ExpectedSnapshots.json`은 최적화 이전 소스에서 캡처한 기준값입니다. 기준값 재생성은 의도적으로 출력 계약을 바꿀 때에만 사용합니다.

```powershell
./Tools/MapObjectProfilerHarness/Run.ps1 -SourcePath '<비교 기준 MapObjectTickManager.cs>' -CaptureBaseline
```

이 하네스는 통계 출력과 반복 할당의 동등성을 검증합니다. 실제 빌드의 FPS·CPU 시간 개선량은 측정하지 않습니다.
