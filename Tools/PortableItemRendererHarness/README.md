# PortableItemRenderer 증분 배치 검증

실제 `PortableItemRenderer`의 등록·해제·dirty 병합·증분 갱신 메서드를 추출해 결정적인 배치 대역으로 검사해.

```powershell
& ./Tools/PortableItemRendererHarness/Run.ps1
```

초기 등록은 객체당 한 번만 배치를 만들고, 같은 객체의 반복 dirty 요청은 한 번으로 합쳐지는지 확인해. 행렬만 바뀌면 기존 엔트리를 갱신하고 셀/배치 키가 바뀔 때만 해당 객체를 재구성하며, 비표시·해제 객체가 캐시에서 제거되는지도 검사해. 워밍업 이후 반복 증분 갱신의 관리 힙 할당은 0이어야 해.

Unity 렌더링과 GPU 제출은 실행하지 않아. 실제 화면과 프레임 시간은 게임 빌드에서 새 `Portable Object Incremental Update`, `Portable Object Submit`, `PortableRegistered`, `PortableDirtyObjects` 지표로 검증해야 해.
