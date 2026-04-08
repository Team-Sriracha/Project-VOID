# 커스텀 2~8 데이터 표준 가이드

적용 대상:
- `GameModeConfig` 에셋
- `MapGenerationSettings` 에셋

목표:
- 커스텀 모드 인원 범위를 `2~8`로 고정
- 인원별 룰/맵 템플릿을 데이터로 분리해 코드 수정 없이 확장

## 1. 전제 규칙

- 커스텀 모드 목표 인원: `2~8`
- 총 게임 시간(로비 선택): `5/10/15분`
- 페이즈 시간은 로비에서 입력하지 않고 `GameModeConfig`에서 관리

## 2. GameModeConfig 표준

권장 에셋 구성:
- `Custom_2P`
- `Custom_3P`
- `Custom_4P`
- `Custom_5P`
- `Custom_6P`
- `Custom_7P`
- `Custom_8P`

필수 설정 규칙:
- `GameMode = Custom`
- `MinPlayers = N`
- `MaxPlayers = N`
- 페이즈 관련 값(페이즈 시간, 단계 수, 기타 룰) 명시

의미:
- 인원별 밸런스/페이즈를 독립 튜닝 가능
- 런타임은 `GameMode=Custom + TargetPlayers`로 적합한 에셋 선택

## 3. MapGenerationSettings 표준

`TemplateSets` 권장 구성:
- `GameMode = Custom`
- `MinPlayers = N`
- `MaxPlayers = N`
- `Templates = [N인용 템플릿 목록]`

권장 분리:
- 2인 전용 템플릿
- 3인 전용 템플릿
- ...
- 8인 전용 템플릿

동작 규칙:
- 로비 UI에서 현재 인원과 `Min/Max`가 일치하는 템플릿만 표시
- 템플릿 이름은 중복 없이 관리

## 4. 네이밍 컨벤션

`GameModeConfig`:
- `GMC_Custom_2P`
- `GMC_Custom_3P`
- ...
- `GMC_Custom_8P`

`MapLayoutTemplate`:
- `MAP_Custom_2P_Arena01`
- `MAP_Custom_4P_Field02`
- `MAP_Custom_8P_Island01`

## 5. 검증 체크리스트

- 2인 선택 시 2인용 `GameModeConfig`가 선택되는지
- 8인 선택 시 8인용 `GameModeConfig`가 선택되는지
- 인원 변경 시 맵 템플릿 목록이 해당 인원 범위로 즉시 갱신되는지
- 커스텀 생성 세션에서 `TargetPlayers`가 `2~8`로 저장되는지
- 서버 모니터에서 세션 인원수가 모드 규칙에 맞게 표시되는지

## 6. 운영 원칙

- `Resources` 폴더 미사용
- 모든 데이터 에셋은 `Assets/Data` 하위에서 관리
- 모드/인원 정책 변경 시 코드가 아니라 데이터 우선 수정
