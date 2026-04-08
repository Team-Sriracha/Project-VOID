# 커스텀 2~8 에디터 적용 순서 (실제 프로젝트 경로 기준)

적용 대상 에셋:
- `Assets/Data/GameMode/4 Players.asset`
- `Assets/Data/GameMode/8 Players.asset`
- `Assets/Data/GameMode/Ranked 8 Players.asset`
- `Assets/Data/GameMode/Custom 2 Players.asset`
- `Assets/Data/GameMode/Custom 3 Players.asset`
- `Assets/Data/GameMode/Custom 4 Players.asset`
- `Assets/Data/GameMode/Custom 5 Players.asset`
- `Assets/Data/GameMode/Custom 6 Players.asset`
- `Assets/Data/GameMode/Custom 7 Players.asset`
- `Assets/Data/GameMode/Custom 8 Players.asset`
- `Assets/Data/GameMode/Custom Mode.asset`
- `Assets/Data/GameMode/Pratice Range.asset`
- `Assets/Data/Map/MapGenerationSettings.asset`
- `Assets/Prefab/Manager/GameStateManager.prefab`

## 1. 현재 기본 에셋 확인

`GameStateManager.prefab`의 `_gameModeConfigs`에는 현재 12개가 연결되어 있습니다.

- `4 Players`
- `8 Players`
- `Ranked 8 Players`
- `Custom 2 Players`
- `Custom 3 Players`
- `Custom 4 Players`
- `Custom 5 Players`
- `Custom 6 Players`
- `Custom 7 Players`
- `Custom 8 Players`
- `Custom Mode`
- `Pratice Range`

이번 반영으로 인원 범위는 아래처럼 고정됩니다.

- `4 Players`: `4~4`
- `8 Players`: `8~8`
- `Custom Mode`: `2~8`
- `Pratice Range`: `1~1`

## 2. 커스텀 2~8 세분화(적용 완료)

기본 7개 에셋이 이미 생성/연결되어 있습니다.

- `Custom 2 Players` ~ `Custom 8 Players`
- 모두 `GameMode = Custom`
- 각각 `MinPlayers = MaxPlayers = N`

선택 우선순위:
- `GameStateManager`는 배열 순서대로 탐색하므로, `Custom 2~8`이 `Custom Mode`보다 앞에 있어 인원별 설정이 우선 적용됩니다.

## 3. 맵 템플릿(커스텀 2~8) 연결

`Assets/Data/Map/MapGenerationSettings.asset`의 `_templateSets`에서 `GameMode=Custom` 항목을 인원별로 분리합니다.

권장 구성:
- `Custom, Min=2, Max=2, Templates=[...]`
- `Custom, Min=3, Max=3, Templates=[...]`
- ...
- `Custom, Min=8, Max=8, Templates=[...]`

중요:
- 템플릿 배열이 비어 있으면 해당 인원에서 fallback이 발생할 수 있습니다.

현재 반영:
- `GameMode=Custom` 기본 세트는 `Min=2`, `Max=8`로 보정됨
- `GameMode=Ranked` 세트가 추가되어 8인 템플릿을 명시적으로 사용

## 4. 랭크 모드 처리

현재는 랭크 전용 에셋이 이미 연결된 상태입니다.

- `Assets/Data/GameMode/Ranked 8 Players.asset`
- `GameMode = Ranked`
- `MinPlayers = 8`
- `MaxPlayers = 8`

필요 시 이 에셋에서 랭크 전용 페이즈/시간/드랍률을 독립 튜닝하면 됩니다.

## 5. 적용 후 확인 포인트

- 로비 커스텀 인원 화살표가 `2~8`에서만 동작
- 커스텀 생성 시 세션 `TargetPlayers`가 `2~8` 값으로 기록
- 동일 인원에서 `GameStateManager.CurrentGameModeConfig`가 의도한 커스텀 에셋으로 선택
- `MapGenerationSettings`가 해당 인원 템플릿만 반환
