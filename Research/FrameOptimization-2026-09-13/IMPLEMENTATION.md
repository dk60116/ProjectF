# 프레임 최적화 적용

IO 중첩 영역의 전체 시설 순회를 제거했고, 반복 상태 조회에서 씬 전수조사를 분리했어. 남아 있는 프레임 비용을 찾을 수 있도록 계측도 보강했어.

## 변경

- `InputOutputModule`의 출력 아이템·입력 아이템·연료 종류 조회는 해당 좌표의 `registeredRuntimeAreaCoordinates`만 확인해. 점유 격자 밖 IO 영역도 포함하는 기존 인덱스를 사용해. 설치 구성·복원·활성화·비활성화·풀 반환 때의 등록 경로를 확인했어.
- 중첩 허용 집합은 Unity `HashSetPool`로 빌리고 반환해. 각각 별도 임대라 중첩 호출에서도 집합이 섞이지 않아. 필터·레시피의 계산 결과는 매번 현재 값을 읽으므로 변경 후 무효화가 빠지는 캐시를 추가하지 않았어. 조회용 델리게이트도 재사용해.
- `status`는 FPS·UPS·벨트 아이템 등 가벼운 값과 마지막 전수조사 결과를 반환해. `perf`도 매번 블록과 컴포넌트 계층을 조사하지 않아.
- EditorTool과 MapObjectProfiler의 `Refresh Counts`가 `counts` 명령으로 설치물·씬 GO/MB·월드 컴포넌트 전수조사를 수행해. 최초 조사 전 값은 미측정으로 표시하고 이후에는 조사 경과 시간을 표시해. 청크 로딩 중에는 조사를 거절해.
- `renderFrames`와 `simulationTicks`를 따로 집계해. 기존 `beltLoopProfileFrames` 필드는 벨트가 표본을 기록한 프레임 수로 유지해. 표와 그래프·텍스트의 `ms/frame`은 실제 `renderFrames`로 나눠.
- CPU 메인·렌더 스레드·스크립트·물리·애니메이션·UI 카운터 후보를 보강했어. 사용할 수 없는 엔진 마커는 `n/a`로 표시해. 계측을 끄면 엔진 레코더도 해제해.
- 시뮬레이션 프레임, Terrain Update/Visuals/Surfaces, 자원 렌더링, 도구 요청과 전수조사를 구간별로 계측해. 상위 구간은 하위 구간을 포함하므로 합산하면 안 돼. 엔진 레코더의 이동 평균 창도 MapObject 표본 창과 다를 수 있어.
- Pause 상태에서는 기존 완료 스냅샷을 유지해.
- 벨트 본체, 정적 설치물, 자원 성장 표시, 지면 아이템, 영역 마커, 플레이어, 카메라, UI의 렌더 프레임 비용을 별도 행으로 계측해. 다음 스냅샷에서 기존 `CPU Main`에 숨었던 비용을 바로 구분할 수 있어.
- 나무 성장 흔적과 성장 게이지는 16m 공간 버킷을 먼저 프러스텀 컬링해. 기존처럼 모든 나무를 두 번 개별 검사하지 않고, 화면과 겹치는 버킷의 후보만 같은 렌더 조건으로 처리해.
- 화면 밖 설치물의 관리형 애니메이션 상태는 4개 프레임 위상으로 나눠 재검사해. 보이는 설치물과 컬링이 꺼진 상태는 매 프레임 갱신하고, 화면 밖 대상만 최대 4프레임 안에 다시 확인해. `InstallationVisuals` 카운터에서 실제 검사 수와 지연 수를 볼 수 있어.
- 벨트·정적 설치물의 배치 수, 행렬 수, 예상 드로우콜 수와 자원 성장 버킷·화면 후보 수를 런타임 카운터로 추가해. 총 설치 수와 실제 렌더 제출량의 차이를 확인할 수 있어.

프레임 타이밍 카운터는 필요할 때 레코더를 연결하는 방식을 사용했어. 릴리스 빌드 전체에 Frame Timing Stats를 항상 켜지는 않았어. 근거는 [Unity ProfilerRecorder 프레임 타이밍 문서](https://docs.unity3d.com/6000.0/Documentation/Manual/frame-timing-manager-record-timing-data.html)와 [릴리스 빌드 설정 문서](https://docs.unity3d.com/6000.4/Documentation/Manual/frame-timing-manager-enable.html)에 있어.

## 검증

- `Assembly-CSharp.csproj`: 컴파일 오류 0개. Unity 플레이어/IL2CPP 빌드를 실행한 검증은 아니야.
- MapObjectProfiler와 EditorTool: Release publish 성공.
- `RuntimeIoQueryHarness`: 무관한 모듈 10,000개 미조회, 입력·연료·박스 필터, 비활성·이동·등록·해제·중첩 호출 검사 통과. 워밍업 후 10,000회 조회의 관리 힙 할당 0바이트. 씬·레시피 경계는 대체한 검사야.
- `RobotArmIoHarness`: 기존 IO·저장 검사 211개 통과.
- `MapObjectProfilerHarness`: 새 필드를 제외한 기존 JSON 스냅샷 127개 일치. 30/100 화면 프레임과 60틱 분리, 일시정지·중복 조회·측정 끔 검사 통과. 반복 status에서 전수조사 0회, 수동 조사와 월드 변경 캐시 처리 검사 통과.
- `WorldVisualUpdateHarness`: 27개 검사 통과. 보이는 설치물의 즉시 갱신, 화면 밖 갱신 생략, 최대 4프레임 내 복귀와 상태 복원을 포함해.
- `git diff --check`: 공백 오류 없음.

실행 중인 게임은 이전 빌드이므로 이번 변경의 실제 FPS 개선폭을 아직 측정하지 않았어. 실제 설치·로드·생산 동작도 새 게임 빌드에서 확인해야 해. 다음 Unity 게임 빌드의 기존 `CraftingTreeBuildSync`가 수정한 두 도구를 빌드 폴더에 함께 배포해.
