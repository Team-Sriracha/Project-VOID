# Lobby UI 에디터 설정 가이드 (2026-02-13 최신)

적용 기준 파일:
- `Assets/Scripts/UI/LobbyUI.cs`
- `Assets/Scripts/Core/MatchmakingManager.cs`

이 문서는 `자동 바인딩 제거` 상태를 기준으로 작성되었습니다. 모든 참조는 인스펙터에서 직접 연결해야 합니다.

## 1. 모드 구조

- 메인 모드 3개: `일반`, `랭크`, `커스텀`
- 일반 선택 시 타입 스위치: `4인`, `8인`
- 랭크 선택 시 타입 스위치: 숨김(8인 고정)
- 커스텀 선택 시 타입 스위치: `방 만들기`, `방 참가`
- 연습장은 별도 모드가 아니라 `커스텀 > 방 만들기 > 연습장 체크`로 동작

## 2. LobbyUI 필수 참조

아래 항목 중 하나라도 비어 있으면 `LobbyUI`가 비활성화됩니다.

- `_modeSwitch`
- `_typeSwitch`
- `_typeSwitchRoot`
- `_matchStartButton`
- `_roomSettingPanel`
- `_roomCodePanel`
- `_practiceToggle`
- `_roomCodeInputField`
- `_mapGenerationSettings`

권장 참조:
- `_matchmakingManager` (비어 있어도 런타임 탐색 시도는 하지만, 수동 연결 권장)

## 3. 스위치 인덱스 규칙

`_modeSwitch`:
- `0`: 일반
- `1`: 랭크
- `2`: 커스텀

`_typeSwitch`:
- 일반일 때 `0=4인`, `1=8인`
- 커스텀일 때 `0=방 만들기`, `1=방 참가`

## 4. 커스텀(방 만들기) 설정 규칙

인원:
- 범위 `2~8`
- 좌우 화살표 버튼으로 변경

게임 시간:
- 고정값 `5분`, `10분`, `15분`
- 좌우 화살표 버튼으로 변경

맵:
- `MapGenerationSettings.TemplateSets`에서 `GameMode=Custom`인 템플릿만 대상
- 현재 인원수가 `MinPlayers~MaxPlayers` 범위에 들어가는 템플릿만 표시

페이즈 시간:
- 로비 UI에서 직접 설정하지 않음
- `GameModeConfig` 템플릿에서 관리

## 5. 연습장 체크 동작

- 연습장 체크 ON 시 커스텀 설정 입력(인원/시간/맵)을 비활성화
- 매칭 시작 시 온라인 세션 접속 없이 Yak + Host 오프라인 연습장 경로로 진입

## 6. 커스텀 생성/참가 네트워크 규칙

- 방 만들기: 인원/시간/맵 설정이 `CustomRoomSettings`로 서버 세션 규칙에 반영
- 방 참가: Unity Lobby Join Code(6자리)로 참가
- 참가자는 방장이 설정한 세션 규칙을 그대로 사용

## 7. UI 표시 규칙

- 일반: 타입 스위치 표시
- 랭크: 타입 스위치 숨김
- 커스텀 + 방 만들기: `RoomSettingPanel` 표시
- 커스텀 + 방 참가: `RoomCodePanel` 표시
- 방 코드 입력값은 영문 대문자 기준으로 정규화

## 8. 최종 확인 항목

- 일반 4인/8인 전환이 정상 동작
- 랭크 선택 시 타입 스위치가 숨김
- 커스텀 방 만들기/방 참가 전환 시 패널이 정상 변경
- 커스텀 인원 범위가 2~8로 고정
- 커스텀 시간이 5/10/15분으로만 변경
- 연습장 체크 ON 시 설정 입력이 잠금
- 방 코드는 6자리일 때만 참가 시작
