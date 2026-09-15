# SaveLoadOptimizationHarness

Unity 실행 없이 프로덕션 저장 스케줄러와 초기 청크 계획을 검증해.

```powershell
dotnet run --project Tools/SaveLoadOptimizationHarness -- <slot_01.pfsave> <slot_02.pfsave>
```

- 4ms 예산, 느린 렌더 프레임, 캡처 취소·실패·단일 작업 초과 시간을 검증해.
- 화면 밖 설치물과 입출력 좌표, 작물, 바닥 아이템, 동물의 초기 청크 포함을 검증해.
- 슬롯 인수는 선택 사항이며 읽기 전용이야. 계획 전후 전체 직렬화 바이트가 같은지도 확인해.
- 스케줄러와 청크 계획은 현재 소스를 직접 컴파일해. 저장 DTO와 직렬화기는 `FactorioProject/Temp/bin/Debug/Assembly-CSharp.dll`을 사용해.
- 실제 씬의 저장 완료 시간과 FPS는 별도로 측정해야 해. 런타임 로그의 `Snapshot wallMs/activeMs/maxSliceMs/frames`, `Compress/write wallMs`, `Load chunks`를 비교하면 돼.
- 슬롯 로드 로그의 슬롯 분리, 전체·읽기·씬/월드 시간, 교체 취소 기록도 임시 폴더에서 검증해.
- 슬롯 저장 로그의 전체·스냅샷·최대 프레임·파일 쓰기 시간과 실패 기록도 검증해.
- 탐험 좌표와 자원 상태는 모두 저장소에 유지돼. 화면 밖 설비와 저장된 활동에 필요 없는 지형은 다시 방문할 때 생성하며, 해당 구역의 자연 자원 성장·미접촉 야생동물은 상주 시점부터 갱신돼.
