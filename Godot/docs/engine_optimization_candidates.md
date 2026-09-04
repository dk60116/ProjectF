# Engine optimization candidates

엔진 수정 여부를 판단하기 위한 기록 문서다. 각 항목은 프로파일러 증거, 재현 harness, GDExtension 수준의 대안이
모두 기록되기 전까지 `미확인`으로 유지한다.

| Candidate | 현재 상태 | Module/fork 진입 조건 |
|---|---|---|
| RenderingServer call overhead | 미확인 | batch 수를 고정한 경계 호출 비용이 frame budget의 유의미한 비율일 때 |
| MultiMesh buffer upload | baseline 확보 | dirty batch upload만으로도 지속 stall이 재현될 때 |
| SceneTree overhead | 현재 대상 아님 | factory entity가 node가 아닌데도 SceneTree가 병목일 때 |
| Resource loading | 미확인 | background decode/upload 분리 후에도 streaming hitch가 남을 때 |
| Shader pipeline | 미확인 | warm cache에서도 pipeline compile hitch가 재현될 때 |
| Instance submission | baseline 확보 | 동일 draw count에서 server submission이 병목으로 확인될 때 |
| Memory allocator | 미확인 | reserve/pool 적용 후 allocation flame graph가 hot path에 남을 때 |
| Thread scheduler | 미확인 | dependency graph와 worker-local queue 구현 후 scheduler 비용이 클 때 |
| Serialization | 미확인 | binary chunk save/load profile에서 codec 외 경계 비용이 클 때 |
| Physics integration | 회피 설계 | grid occupancy 외 소수 dynamic body에서도 server 비용이 병목일 때 |
| Navigation | 미구현 | custom chunk navigation baseline보다 엔진 연동 비용이 지배적일 때 |
| GDExtension boundary | 미확인 | coarse-grained bridge에서도 measurable bottleneck일 때 |

현재 결론은 Level 1인 pure C++ library + GDExtension을 유지하는 것이다. Module 또는 fork로 이동할 근거는 아직 없다.
