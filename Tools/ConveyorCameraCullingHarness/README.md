# 월드·컨베이어 카메라 컬링 검증

GameManager Inspector와 EditorTool의 `Disable Camera Culling` 토글을 함께 사용한다.

- `false` / 체크 해제: 컬링 **켬**, 기본값.
- `true` / 체크: 컬링 **끔**.
- EditorTool은 런타임 상태를 조회해 체크 상태를 동기화한다. 프로토콜은 `debug disableCameraCulling true|false`이다.

모든 게임 카메라의 월드 오브젝트에 적용한다. 일반 Renderer(건물·동물·플레이어·기차·파티클 등)는 Unity 카메라 컬링을 사용하고, 지형 청크·자원·벨트·아이템·로봇팔 배치와 벨트 디버그 표시·전봇대 설치 범위 표시도 같은 토글을 따른다. 개별 오브젝트를 매 프레임 검색하거나 GameObject를 비활성화하지 않는다.

컬링을 켜면 화면 밖 벨트의 Transform 조회, 아이템 렌더 캐시 재생성, 그림자를 만들지 않는 배치의 GPU 업로드, 지형 청크의 그리기 제출을 줄인다. 아이템 이동과 생산·운송·AI는 계속 진행하며, 화면으로 돌아오면 최신 상태로 렌더 캐시를 복원한다. 시각 처리 순서는 카메라 이동 → 지형·디버그 표시(900) → 배치 렌더러(1000)이다. 지형/컨베이어 시뮬레이션 Update의 실행 순서는 유지한다.

동물은 별도의 AI 활성 상태를 따른다. 거리 제한이나 AI 일시정지로 행동 실행이 비활성화되면 Animator도 비활성화하고, 다시 활성화되면 보관한 애니메이터 상태를 이어간다. 정상 AI의 Idle·먹기·휴식, 탑승·올가미·수레의 직접 제어, 사망 연출은 유지한다. 카메라 컬링 토글과는 독립적이다. 최초 원거리 생성 시에는 0초 평가로 초기 자세만 준비한다.

컬링을 끄면 `WorldCameraCulling`이 게임 카메라의 렌더 콜백 동안 가시성 범위를 전체 int 좌표 월드를 포함하는 ±100억 월드 단위로 확장하고 occlusion culling을 해제한다. 이는 CPU의 가시성 판정을 우회하는 비교 모드다. 실제 투영·화면 클리핑·레이어 필터·LOD·비활성 오브젝트·청크 로딩 정책은 유지한다. 렌더 종료 시 기존 자동/사용자 지정 컬링 행렬과 occlusion 설정을 복구한다. 중첩 카메라, 렌더 중단, 컴포넌트 해제도 복구한다. SceneView·Preview·Reflection 카메라는 변경하지 않는다.

연결 지점은 [Unity Camera.cullingMatrix](https://docs.unity3d.com/ru/2019.4/ScriptReference/Camera-cullingMatrix.html)와 [URP beginCameraRendering](https://docs.unity3d.com/kr/Packages/com.unity.render-pipelines.universal%4014.0/manual/using-begincamerarendering.html)이다. 프로젝트의 URP 17.4 소스에서 begin 콜백이 `TryGetCullingParameters` 이전에 실행되는 것도 확인했다. 현재 GPU Resident Drawer의 frustum 검사 역시 해당 카메라의 culling planes를 사용한다.

## 엔진을 실행하지 않는 검증

저장소 루트에서 실행한다.

```powershell
& ./Tools/ConveyorCameraCullingHarness/Run.ps1
```

.NET 9 SDK와 Unity의 관리형 CoreModule DLL이 필요하다. 설치 위치가 다르면 `-UnityManagedDirectory`로 DLL이 있는 폴더를 지정한다. 임시 프로젝트는 시스템 임시 폴더에 생성한다. 먼저 `Assets/Scripts`의 모든 C# 파일에 메타 파일이 있고 GUID가 32자리이며 중복되지 않는지 검사한다. Unity가 잘못된 GUID 때문에 소스를 무시하는 문제는 C# 컴파일만으로 발견할 수 없기 때문이다.

실제 컬링 상태 클래스, 월드 카메라 제어 클래스, 보류 캐시 코드를 연결하고, 실제 벨트 등록·해제·행렬 갱신 메서드를 추출해 검사한다. 카메라 이동·줌·레이어 변경, 토글 반전, 반복 변경의 병합, 화면 복귀, 언로드, 화면 밖 Transform 조회 생략, GPU 배치 유지/해제를 검증한다. 월드 검사에서는 기본 설정 보존, 자동/사용자 지정 행렬 복원, 전체 좌표 범위, 중첩/복수 카메라, 편집 카메라 제외, 콜백 해제, 중단 시 복구를 검사한다. 씬 객체와 렌더 API는 대역을 사용하므로 실제 게임 화면, GPU 출력, FPS를 검증하지는 않는다.

## 게임에서 비교할 지표

동물 애니메이션 검사는 실제 중지/복원·AI 상태 연결·사망 처리 메서드를 추출하고 Animator를 대역으로 사용한다. 최초 비활성 상태, 반복 휴면 호출, 대기/먹기/휴식 유지, 복귀 시 기상, 직접 제어, 사망, 다른 코드가 비활성화한 Animator의 소유권을 검사한다. Unity 내 실제 자세 보존과 애니메이션 전환은 게임 검증 대상이다.

같은 배치·카메라 위치에서 토글 전후 프로파일러를 비교한다.

- `DisableCameraCulling`: 실제 토글 값.
- `VirtualBeltTrackedTransformReads`, `VirtualBeltCulledTrackedBelts`: 조회한 Transform 수와 생략한 벨트 수.
- `StaticCacheRebuilds`, `DeferredOffscreenBlocks`: 아이템 캐시 갱신 수와 화면 밖 보류 블록 수.
- `RenderedChunkSurfaces`, `CulledChunkSurfaces`: 지형 청크의 제출/생략 수.
- `Virtual Belt Render`, `Conveyor Item Rebuild`, 전체 프레임 시간.

게임 검증 시에는 카메라 이동·줌 후 지형·자원·건물·동물·플레이어·기차와 일반/코너/2F/분배기 및 Seam Top이 나타나는지, 화면 밖에서 이동한 아이템이 현재 위치에 나타나는지도 확인한다. 컬링을 껐다 켠 뒤 카메라 추적·줌·자유 카메라가 유지되는지 함께 확인한다.

FreeCamera player-view inspection: 15 additional checks exercise the actual PlayerCamera matrix update, shared custom-culling selection, native renderer override/restoration, player translation, camera ownership, missing-camera fallback, and Disable Camera Culling precedence. Total: 72 checks. Screen projection stays on the free camera; its occlusion culling is disabled only during the native inspection render, then restored.
