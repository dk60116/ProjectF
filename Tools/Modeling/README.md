# 2F 컨베이어 모델 생성

`Generate-ConveyorBelt2FModel.py`는 2F 컨베이어의 3구간 본체와 연속 상판을 생성한다.
Unity가 직접 읽는 native mesh asset, 편집용 OBJ 원본, 정리된 프리팹 계층을 한 번에 갱신한다.

```powershell
python Tools/Modeling/Generate-ConveyorBelt2FModel.py --preview tmp/ConveyorBelt2F_ModelPreview.png
```

생성 중 퇴화 삼각형, 16비트 인덱스 한계와 빈 메시를 검사한다. `--no-prefab`을 지정하면
메시와 OBJ만 다시 만들 수 있다.
