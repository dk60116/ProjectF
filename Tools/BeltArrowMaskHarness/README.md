# Belt arrow material mask

```powershell
& ./Tools/BeltArrowMaskHarness/Run.ps1
```

기존 벨트 상판 텍스처에서 두 화살표만 분리한 선형 마스크와 직선·경사·분배기 공통 상판, 코너, 양쪽 연결부 재질 연결을 확인해. 셰이더에서는 마스크가 상판과 같은 이동·길이·코너 흐름 UV를 사용하는지도 소스 계약으로 확인해. 실제 Unity 셰이더 컴파일과 GPU 출력은 검사하지 않아.

`M_BeltTop`, `M_BeltTop_Corner`, `M_BeltTop_Seam_Input`, `M_BeltTop_Seam_Output`의 **Arrow Color**로 화살표 색을 정해. 기본값은 흰색 화살표이고 알파는 색 적용 강도야. 알파 0은 원본 회색을 표시해. 상판의 원래 명암과 불투명도는 유지해. 색은 조명과 설치 미리보기 색보다 먼저 적용하므로 화살표에도 조명과 그림자가 반영돼. 재질 인스턴스를 프레임마다 만들거나 벨트별 색 상태를 추가하지 않아.

원본 상판 PNG는 그대로 사용해. 마스크는 데이터 생성 도구 `Tools/Generate-BeltArrowMask.ps1`로 다시 만들 수 있어. 128픽셀 원본의 두 화살표 영역과 밝기에서 피복률을 추출하고 금속 테두리와 바닥은 제외해. 원본 아틀라스 배치를 바꾸면 생성 도구의 영역도 함께 갱신해야 해.
