# 벨트 GPU 모션/UV MaterialPropertyBlock 차등 하네스

`powershell -ExecutionPolicy Bypass -File Tools/ConveyorBatchPropertyHarness/Run.ps1`

실제 `VirtualRenderBatchCollection` 전체 코드를 추출하여 실행한다. 비교 대상은
`LegacyResolveBatchPropertyBlock.cs`에 고정한 변경 전 메서드다. 나머지 배치/소유권 코드는
같은 실제 코드로 실행하며, 모든 draw의 순서·mesh·material·행렬·bounds·UV·모션 배열·색·텍스처를 비교한다.

- 1/1023/1024/2046/2047개, 여러 draw 범위의 독립 캐시
- 1023↔1024개 경계 40회 왕복: 워밍업 후 MPB 재생성 0회, 현재 draw 수+예비 1개로 보관 한도 제한
- DataVersion 변경, 시작/끝 모션 데이터 변경, UV 변경, swap-remove 및 옮겨진 owner 인덱스
- material 내부 수면색 변경, 디버그 색/텍스처, 값 없는 일반 배치
- 카메라/레이어 컬링 후 복귀, 화면 밖 dirty, suspend/resume
- ClearActiveMatrices와 같은 배치 재사용, 범위 축소, Clear, mesh 교체, 마지막 owner 제거
- 같은 GPU 데이터의 120프레임 렌더: SetVectorArray/실제 range-copy helper 호출 수 비교
- 워밍업 후 캐시 hit의 .NET GC 할당 검사

Unity 엔진은 실행하지 않는다. Unity 값 형식과 실제 배치 코드를 쓰되 MaterialPropertyBlock,
Graphics 및 카메라 컬링은 대역이다. `_ConveyorMotionTime` 변경을 draw마다 기록하며
런타임의 전역 시간 갱신 코드에는 손대지 않는다. GPU/드라이버 내부 업로드 비용이나 실제 FPS는 측정하지 않는다.
