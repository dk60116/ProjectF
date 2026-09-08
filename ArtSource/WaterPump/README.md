# WaterPump — 원본 표면 유지, UV 정렬

원본의 색상, 질감, 얼룩, 통풍구 무늬를 그대로 유지한다. 표면을 다시 그리거나 색을 단순화하지 않는다.

기존 194개 UV 섬을 새로 펼치지 않고 회전·이동·균일 확대/축소로 정렬한다. 원본 UV로 읽은 원본 텍스처를 새 UV 위치에 Emission 방식으로 옮긴다. 라이팅, AO, 재질 패턴을 새로 굽지 않는다. 결과 PNG에는 원본의 조각들이 새 UV 위치에 배치되므로 파일 자체는 원본과 다르다. 재샘플링 오차를 줄이기 위해 원본 512×512보다 높은 2048×2048 해상도를 사용한다.

## 파일

- WaterPump_clean.blend: 원본 표면을 유지한 UV 정렬 결과. 최종 텍스처가 내부에 패킹되어 있다.
- Water pump.fbx, Water pump_TB.png: 프로젝트에 반영할 FBX/텍스처 사본.
- original/Water pump.fbx, original/Water pump_TB.png: 변경 전 원본. 재생성의 기준이다.
- before_front.png, before_back.png, after_front.png, after_back.png: 동일 조명과 시점의 표면 비교 렌더.
- uv_repack.json: 섬 개수, 섬 모양 보존 오차, 렌더 비교 수치.
- validation.json: FBX 및 UV 검증 결과.

프로젝트 적용 경로는 FactorioProject/Assets/MapObject/Fluid/Water pump이다. FBX의 UV, UVIndex 및 그에 대응하는 Tangents/Binormals만 변경한다. 원본의 메시·노멀·좌표·ID와 기존 GUID·재질·프리팹 참조는 유지한다. 원본에 있던 면적 0인 삼각형 2개도 유지한다.

## 재생성

설치된 Blender를 --background --python repack_uv.py로 실행한다. 이 폴더의 원본에서 새 장면을 만들고 UV 정렬, 원본 이미지 이전, 전후 렌더, 편집 파일 저장을 수행한다. 실행 중인 블렌더의 다른 작업을 수정하지 않으며 Unity 자산을 자동 덮어쓰지 않는다.

NumPy가 포함된 Python으로 validate_waterpump.py를 실행해 FBX의 허용 배열 외 내용 일치, UV 범위·겹침·퇴화 및 PNG 크기를 검사한다. 검토 후 이 폴더의 FBX/PNG를 프로젝트 적용 경로에 함께 복사한다.

Unity 실행 및 게임 내 검증은 하지 않았다. 앞선 표면 재제작 방식은 최종 결과에서 제외했다. 이전 스크립트와 산출물은 original/superseded_surface_redesign에 별도 보관한다.


## 받침대 직사각형 보정

최종본에는 UV 정렬 후 받침대 형상 보정도 적용되어 있다. 받침판의 8개 모서리를 일정한 폭·높이의 직육면체로 맞추고, 앞뒤 받침발의 폭을 통일했다. 펌프 상부의 정점·노멀, 면 구성, UV 좌표, 텍스처 파일은 이 단계에서 변경하지 않았다. 받침대의 변경된 면에 대응하는 노멀·탄젠트만 갱신했다.

- square_base.py: original/before_rectangular_base의 UV 정렬 완료본을 기준으로 보정하고 저장한다.
- rectangular_base.json: 변경 범위와 받침판 치수.
- rectangular_base_front.png, rectangular_base_back.png, rectangular_base_top.png: 최종 형상 확인 렌더.
- original/before_rectangular_base: 받침대 수정 이전의 FBX/Blender 및 검증 보고서.

현재 최종 결과를 재생성할 때는 square_base.py를 사용한다. repack_uv.py는 받침대 수정 이전의 UV 정렬 단계만 재생성한다. 검증기는 최종 형상에서 상부 메시·UV·토폴로지·텍스처 보존과 받침판의 직사각형 모서리를 추가 검사한다.
