# Slot save/load timing logs

슬롯 로드가 월드 체크포인트 복원까지 끝나면 `slot_01_load_times.log` 형식으로 누적 기록해.

각 행은 UTC 완료 시각, 슬롯, 상태, 시작 경로, 전체 시간, 파일 읽기 시간, 씬·월드 복원 시간, 상세 상태를 탭으로 구분해.

슬롯 저장은 `slot_01_save_times.log` 형식으로 누적해. 전체 시간, 스냅샷 벽시계·CPU 시간, 최대 프레임 점유, 처리 프레임·체크포인트 수, 압축·파일 쓰기 시간을 기록해.

`detail`의 `stages=` 값은 `단계명:누적 활성 ms/최대 단일 작업 ms/호출 수` 형식이야. 로드는 레코드 적용, 청크 생성, 벨트·파이프·아이템·가시성 최종화를 구분하고 저장은 맵 상태 수집과 벨트 스냅샷을 구분해.

로드의 `chunk-*` 단계는 청크 준비, 엔티티·설치물 복원, 지형 Job 완료, 메시 적용을 구분해. 저장의 `map-flush-blocks/resources/installations`는 dirty 블록, 자원, 설치물 동기화 비용을 각각 보여줘.

에디터나 저장소 내부 PC 빌드는 이 폴더를 사용해. 저장소 밖 빌드는 `Application.persistentDataPath/Tools/Log`를 사용해.
