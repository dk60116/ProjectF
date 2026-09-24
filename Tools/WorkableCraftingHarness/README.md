# Workable 제작 UI 하네스

`WorkableObject` 상호작용 제작 UI가 사용하는 제작 트리 연결을 화면 조작 없이 검증한다.

## 검증 범위

- 원본 바이너리 `crafting_tree.bytes` 버전이 런타임 형식 5인지 확인한다.
- Workbench와 Anvil을 요구하는 제작 레시피가 각각 존재하는지 확인한다.
- 서로 다른 Workable 작업 범위가 겹칠 때 두 작업대 레시피의 중복 없는 합집합이 만들어지는지 확인한다.
- 직접 재료 중 다시 제작할 수 있는 하위 아이템을 가진 레시피가 존재하는지 확인한다.
- UI 런타임은 같은 필요 작업대 ID를 `CraftingTreeRuntime.TryGetCraftableItemIdsForMapObject`로 역조회한다.
- 직접 재료가 부족하면 `CraftingPlanBuilder`가 보유 중인 하위 재료를 기준으로 중간 제작품부터 순서대로 계획한다.
- 중간 제작품 중 다음 단계가 사용할 수량은 큐 안에서 예약하고, 남는 수량만 플레이어 인벤토리로 전달한다.
- 계획의 마지막 큐를 취소하면 완료된 중간 단계까지 반영한 현재 재료 장부를 반환한다.

## 실행

PowerShell에서 다음 명령을 실행한다.

```powershell
./Tools/WorkableCraftingHarness/Verify-WorkableCrafting.ps1
```

정상 결과에는 Workbench와 Anvil 레시피 수와 `Workable crafting binary validation passed.`가 표시된다.

Unity 에디터 연결 상태에서는 `VerifyCraftingPlan.cs`를 `unity command run_script`로 실행해 실제 런타임 제작 트리 기준으로 다음도 확인할 수 있다.

```powershell
unity command run_script --file "C:\Git\ProjectF\Tools\WorkableCraftingHarness\VerifyCraftingPlan.cs" --entry "VerifyCraftingPlan.Run" --project-path "C:\Git\ProjectF\FactorioProject"
```

- 하위 재료만 보유한 경우 5개 이하의 순차 제작 계획이 만들어지는지
- 모든 직접 재료를 보유한 경우 최종 아이템 한 단계만 등록되는지
- 원재료가 없는 경우 계획 생성이 거부되는지
- 중간 생산량 예약이 각 단계 생산량을 넘지 않는지

## 수동 확인 항목

1. Workbench 또는 Anvil에 접근하면 InteractionButton이 표시된다.
2. 버튼을 누르면 해당 작업대 레시피가 왼쪽에 한 줄당 5개인 아이콘 그리드로 표시된다.
3. 서로 다른 Workable 작업 범위의 겹친 위치에서는 모든 작업대의 레시피가 중복 없이 함께 표시된다.
4. 처음 열었을 때는 아이템 이름과 상세 정보가 표시되지 않는다.
5. 아이콘에 마우스를 올리면 아이콘 슬롯이 살짝 커지고 벗어나면 원래 크기로 돌아온다.
6. 아이콘에 마우스를 올린 상태로 목록을 스크롤해도 아이콘이 목록 셀과 함께 이동한다.
7. 재료·도면·큐 조건이 부족한 아이콘도 선택할 수 있고, 오른쪽 제작 버튼만 비활성화된다.
8. 아이콘을 누르면 오른쪽에 이름, 생산량, 제작 시간, 재료가 표시된다.
9. 오른쪽 아이템 슬롯을 눌러도 재료 영역이 접히거나 펼쳐지지 않는다.
10. 우측 하단 제작 버튼은 정사각형이며 기존 CraftingSlot의 망치 아이콘을 표시한다.
11. 직접 재료가 있으면 우측 하단 제작 버튼을 눌렀을 때 기존처럼 최종 아이템만 제작 큐에 추가된다.
12. 직접 재료가 없지만 제작 가능한 하위 재료가 충분하면 중간 아이템부터 최종 아이템 순서로 큐에 추가된다.
13. 중간 제작품의 남는 생산량은 플레이어에게 지급되고, 다음 단계가 사용할 수량은 큐 안에서 소비된다.
14. 계획의 마지막 큐를 취소하면 같은 계획의 남은 큐가 모두 제거되고 현재 재료 상태가 반환된다.
15. 저장 후 불러와도 재귀 제작 순서와 예약 수량이 유지된다.
16. 패널 프레임 바깥의 어두운 배경을 클릭하면 UI가 닫히고, 프레임 내부의 빈 공간을 클릭하면 유지된다.
17. 작업 범위를 벗어나거나 닫기 또는 Escape를 누르면 UI가 닫힌다.
