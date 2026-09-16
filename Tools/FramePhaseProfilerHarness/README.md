# 프레임 단계 계측 검증

`FramePhaseProfiler` 생산 소스를 가짜 PlayerLoop와 결정적 시계로 검사해. Unity나 게임은 실행하지 않아.

```powershell
dotnet run --project Tools/FramePhaseProfilerHarness/FramePhaseProfilerHarness.csproj -c Release
```

## 프로파일 사용

기존 `mapObjectTickProfiling`을 켜면 스냅샷에 `FramePhases`가 추가돼. Unity 기본 마커가 없는 빌드에서도 PlayerLoop 최상위 단계와 바로 아래 단계의 벽시계 시간을 기록해.

상세 계측이 켜져 있으면 프로젝트의 MonoBehaviour `Update()`와 `LateUpdate()` 진입점도 각각 `Update Caller`, `LateUpdate Caller` 행으로 집계돼. 같은 타입의 여러 인스턴스는 타입별 한 행으로 합산되고, `Samples`는 전체 호출 횟수, `MsPerRenderFrame`은 해당 타입이 프레임당 사용한 총 시간이야. 이 행은 내부 세부 타이머를 포함하는 상위 범위라 자식 행과 합산하지 않아.

- 값은 최근 최대 128개 **완료된 프레임**의 평균 ms야. 상세 열에 median/p95/max가 있어.
- 부모 단계에는 자식 시간이 포함돼. 부모·자식을 합산하면 안 돼.
- FixedUpdate가 여러 번 실행되면 프레임 내 시간을 합산해. 실행되지 않은 프레임은 0으로 포함해.
- PlayerLoop 밖의 대기/에디터 시간은 포함하지 않아. GPU 연산 시간이 아니라 메인 스레드 경과 시간이야.
- 자체 계측 오버헤드도 포함해. 이름별 최대 시간이 0.05ms 미만인 하위 단계는 스냅샷에서 생략해.
- 켜고 끌 때 현재 PlayerLoop의 다른 콜백을 보존하며, 비활성화/월드 관리자 파괴 시 자체 경계만 제거해.

기존 디버그 명령 경로에서 `debug mapObjectTickDetailedProfiling false`로 상세 객체 타이머를 끌 수 있어. 프레임 계측과 네이티브 Recorder는 남겨 둬. `true`로 원복해. GameManager Inspector의 같은 설정으로도 변경할 수 있어.

동일 슬롯·카메라·시뮬레이션 속도에서 상세 ON/OFF 각각 128프레임 이상 지난 뒤 캡처해. FramePhases는 모드가 바뀌면 표본을 비워. 네이티브 Recorder와 커스텀 객체 스냅샷은 수집 창이 다르므로 서로 빼서 정확한 잔여 시간으로 해석하지 마.

## 검증 범위

중복 설치 방지, 기존 콜백 유지, 완료 프레임만 수집, 순환 버퍼, 백분위, 상세 OFF 수집 유지, 경계 콜백 할당 0, 제거와 재설치를 검사해. 실제 Unity의 네이티브 루프 실행·렌더링·성능 개선량은 플레이 테스트가 필요해.
