# ProjectF Godot 포팅 계획

## 확인된 기준 환경

- Godot 4.7.2 stable, Forward+, D3D12, Jolt Physics
- godot-cpp 4.7 API, revision `05057de73de4b99f114d36c40d84ca46926c0e25`
- Windows 11 x64, Visual Studio 2022 MSVC 19.38, SCons
- 현재 godot-cpp 기본값에 맞춘 C++17. C++20 전환은 모듈과 GDExtension을 함께 검증한 뒤 결정한다.
- 기존 Godot 장면은 `main.tscn`, `player.tscn`이며 기존 native 코드는 플레이어와 스포너다.

## 포팅 원칙

Unity 프로젝트는 기능 비교 원본으로 보존하고 Godot 구현이 시나리오별 동등성을 확보할 때까지 제거하지 않는다.
Unity의 MonoBehaviour 구조를 파일 단위로 옮기지 않고, 다음 데이터 흐름으로 재구성한다.

```text
FactorySimulation (순수 C++)
  -> dirty/visible chunk 추출
  -> RenderSnapshot
  -> FactoryRenderBridge
  -> chunk MultiMesh RID
  -> GPU
```

플레이어처럼 실제 물리가 필요한 소수 객체만 Godot 노드를 사용한다. 기계, 벨트 아이템, 설치물은 Node3D,
MeshInstance3D, PhysicsBody3D와 1:1 대응시키지 않는다.

## 공간 및 소유권 결정

- 공장 배치가 X/Z grid 중심이므로 simulation chunk는 32×32 regular grid를 사용한다.
- 높이는 octree 대신 `GridTransform.layer`로 보존한다. 다층 공장의 실제 밀도가 확인된 뒤 vertical subchunk를 검토한다.
- 렌더 컬링 단위는 현재 16×16 subchunk다. 가시 배치 수와 draw call 간 절충을 측정하기 위한 baseline이다.
- simulation이 상태의 유일한 소유자다. renderer는 immutable `RenderSnapshot`만 소비한다.
- 외부 식별자는 8-byte generational `EntityId`이며 pointer를 노출하지 않는다.

## 단계별 이행

### M0 — 완료: 실행 가능한 기반선

- 순수 C++ `FactorySimulation`, `EntityManager`, AoS `MachineStorage`, `ChunkManager`
- negative coordinate를 포함한 X/Z chunk 변환
- active/sleeping machine 목록과 30 UPS fixed tick, catch-up cap
- visible 데이터 전달용 `RenderSnapshot`
- GDExtension simulation bridge와 start/stop/step/statistics API
- per-object node 없이 RenderingServer + chunk MultiMesh placeholder 렌더링
- 카메라 frustum 기준 CPU chunk visibility
- 10K/100K/1M CPU benchmark와 10K/50K/100K render harness
- C++ 단위 테스트 및 Godot bridge smoke test

### M1 — 다음: Unity 기능 동등성의 첫 vertical slice

1. `ItemDefinition`에 대응하는 immutable native definition table을 만든다.
2. 설치물 배치/철거를 `EntityId + GridTransform + footprint`로 구현한다.
3. chunk-local occupancy grid로 배치 충돌과 picking을 처리한다.
4. 저장 포맷은 generational runtime handle과 분리된 persistent ID를 사용한다.
5. 한 종류의 machine과 한 줄의 conveyor를 생성·저장·불러오기까지 연결한다.

완료 조건은 같은 seed와 입력에서 재현 가능한 simulation checksum, 설치/철거 테스트, save/load round-trip이다.

### M2 — conveyor와 대량 item

- item-per-node 구조를 금지하고 `BeltLine` 압축 모델과 item-per-record baseline을 실측 비교한다.
- segment dirty queue만 갱신하며 보이는 item만 snapshot에 추출한다.
- 직선/코너/경사/입출력 연결을 테스트 데이터로 고정한다.

### M3 — streaming, LOD, scheduler

- 저장된 chunk와 resident chunk를 분리한다.
- render LOD와 simulation LOD를 서로 독립시킨다.
- distant factory는 sleeping/event-driven update 후보로 전환한다.
- single-thread baseline을 유지한 뒤 visibility preparation과 chunk I/O부터 worker-local buffer + merge로 병렬화한다.

### M4 — 통합 수준 판단

GDExtension 경계, MultiMesh upload, SceneTree, allocator가 프로파일러에서 실제 병목으로 확인될 때만 Godot C++
Module을 검토한다. Custom Godot fork는 Module에서도 해결되지 않는 엔진 내부 병목에 한정한다.

## 성능 게이트

- 매 변경에서 simulation tick median/p95, active 비율, reserved bytes, capacity growth를 기록한다.
- 렌더는 visible instance, batch/draw call, snapshot build, upload bytes, frame wall time을 기록한다.
- 최적화 순서는 update 대상 감소, 데이터 압축/지역성, draw call/visibility, allocation 제거, 병렬화 순이다.
- AoS/SoA, render chunk 16/32/64, active list/전체 순회는 동일한 harness에서 비교한다.
- 저사양 목표 수치는 실제 목표 장비 측정 후 고정하며 PC 결과로 Switch급 성능을 추정하지 않는다.

## 즉시 다음 권장 작업

현재 baseline에서는 100% active일 때 active index 간접 접근이 전체 연속 순회보다 느리고, 10% 이하에서 큰 이득을
보였다. 따라서 SoA 전환보다 먼저 machine activity 정책과 sleeping 전환 조건을 구현하는 편이 효과가 크다.
렌더 측에서는 100K가 400개 draw call로 분할되므로 render chunk 16/32/64 비교가 다음 우선 실험이다.
