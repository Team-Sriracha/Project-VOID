# LobbyUI 수동 바인딩 체크리스트 (에디터 전용)

적용 스크립트:
- `Assets/Scripts/UI/LobbyUI.cs`

목적:
- 자동 바인딩 없이 인스펙터 수동 설정만으로 로비 UI를 안정적으로 동작시키기 위한 체크리스트

## 1. 기본 참조 체크

- `ModeSwitch` 오브젝트의 `UISwitchButton`를 `_modeSwitch`에 연결
- `TypeSwitch` 오브젝트의 `UISwitchButton`를 `_typeSwitch`에 연결
- `TypeSwitch`의 루트 오브젝트를 `_typeSwitchRoot`에 연결
- `MatchStartButton`의 `Button`을 `_matchStartButton`에 연결
- `RoomSettingPanel` 오브젝트를 `_roomSettingPanel`에 연결
- `RoomCodePanel` 오브젝트를 `_roomCodePanel`에 연결
- 연습장 토글 `Toggle`을 `_practiceToggle`에 연결
- 방 코드 입력 `TMP_InputField`를 `_roomCodeInputField`에 연결
- 커스텀 맵 소스 `MapGenerationSettings`를 `_mapGenerationSettings`에 연결
- `MatchmakingManager`를 `_matchmakingManager`에 연결(권장)

## 2. 커스텀 인원 UI 체크

- `_customPlayerCountText` 연결
- `_customPlayerCountLeftButton` 연결
- `_customPlayerCountRightButton` 연결
- 버튼 클릭 시 인원 값이 `2~8` 범위를 벗어나지 않는지 확인

## 3. 커스텀 시간 UI 체크

- `_customGameTimeText` 연결
- `_customGameTimeLeftButton` 연결
- `_customGameTimeRightButton` 연결
- 버튼 클릭 시 값이 `5/10/15분`으로만 변경되는지 확인

## 4. 커스텀 맵 UI 체크

- `_customMapText` 연결
- `_customMapLeftButton` 연결
- `_customMapRightButton` 연결
- 현재 인원수 기준으로 `MapGenerationSettings.TemplateSets`의 `GameMode=Custom` 템플릿만 나오는지 확인

## 5. 추가 잠금 대상 체크 (`_customSettingInteractables`)

- 자동 수집이 제거되었으므로, 연습장 체크 시 잠글 추가 UI를 직접 배열에 등록
- 등록 후보: 드롭다운, 토글, 추가 입력 필드, 기타 Selectable UI
- 제외 권장: `_practiceToggle`, `_matchStartButton`, 인원/시간/맵 좌우 버튼(이미 코드에서 직접 제어)

## 6. 타입/패널 전환 체크

- 메인 모드 `일반`일 때 `TypeSwitch` 표시
- 메인 모드 `랭크`일 때 `TypeSwitch` 숨김
- 메인 모드 `커스텀` + `방 만들기`일 때 `RoomSettingPanel` 표시
- 메인 모드 `커스텀` + `방 참가`일 때 `RoomCodePanel` 표시

## 7. 연습장 체크 동작 체크

- `커스텀 > 방 만들기`에서 연습장 체크 ON 시 인원/시간/맵 설정 비활성화
- 연습장 체크 ON 상태에서 매칭 시작 시 오프라인(Yak+Host) 연습장 진입

## 8. 방 코드 체크

- `_roomCodeInputField`는 6자리 기준으로 참가
- 소문자/공백/하이픈 입력 시 정규화되어 대문자 6자리로 처리되는지 확인

## 9. 점검 완료 기준

- Play 진입 시 `LobbyUI`가 비활성화되지 않음
- 콘솔에 `LobbyUI 참조 누락` 오류가 없음
- 3개 메인 모드 및 서브 스위치 전환이 의도대로 동작
- 연습장/커스텀 생성/커스텀 참가 시작 흐름이 모두 정상 진입
