# 게임 사운드 시스템 설정 가이드

적용 기준 파일:
- `Assets/Scripts/Audio/AudioManager.cs`
- `Assets/Scripts/Audio/AudioCue.cs`
- `Assets/Scripts/Audio/SceneAudioController.cs`
- `Assets/Scripts/Audio/AudioSettingsStore.cs`

연결 대상 주요 스크립트:
- `Assets/Scripts/Data/ItemData.cs`
- `Assets/Scripts/Data/WeaponData.cs`
- `Assets/Scripts/Data/GunData.cs`
- `Assets/Scripts/Data/MeleeWeaponData.cs`
- `Assets/Scripts/Data/MeleeSkillData.cs`
- `Assets/Scripts/Data/MobData.cs`
- `Assets/Scripts/Player/PlayerCombat.cs`
- `Assets/Scripts/Player/PlayerController.cs`
- `Assets/Scripts/Weapons/NetworkedWeapon.cs`
- `Assets/Scripts/Weapons/Projectile.cs`
- `Assets/Scripts/Items/NetworkedItem.cs`
- `Assets/Scripts/Mobs/MobCombat.cs`
- `Assets/Scripts/Mobs/MobAnimationController.cs`
- `Assets/Scripts/Core/MatchmakingManager.cs`
- `Assets/Scripts/UI/LoadingUIManager.cs`
- `Assets/Scripts/UI/UIManager.cs`
- `Assets/Scripts/UI/LoginUI.cs`
- `Assets/Scripts/UI/LobbyUI.cs`
- `Assets/Scripts/UI/MatchmakingUI.cs`
- `Assets/Scripts/UI/DebugUIManager.cs`

이 문서는 현재 구현된 오디오 시스템을 Unity 에디터에서 실제로 연결하는 방법을 설명합니다.

## 1. 현재 구조 요약

현재 오디오 구조는 아래 기준으로 동작합니다.

- `AudioManager`
  - 씬에 수동 배치합니다.
  - `DontDestroyOnLoad`로 유지됩니다.
  - 중복 인스턴스는 Awake에서 제거됩니다.
- `SceneAudioController`
  - 각 씬이 자신의 BGM만 지정합니다.
  - 씬 진입 시 `AudioManager`에 재생 요청을 보냅니다.
- `AudioCue`
  - 실제 오디오 클립과 재생 파라미터를 묶는 에셋입니다.
- 기능별 로컬 소유
  - 무기 소리는 무기 데이터가 소유
  - 몹 소리는 몹 데이터가 소유
  - UI 소리는 각 UI 컴포넌트가 소유
  - 아이템 픽업음은 `ItemData`가 소유

즉, 이 시스템은 `거대한 전역 설정 에셋` 없이 아래 방식으로 설정합니다.

- 씬 BGM: 씬 오브젝트
- 전투/아이템/몹 사운드: 데이터 에셋
- UI 사운드: UI 컴포넌트 인스펙터

## 2. 먼저 알아둘 규칙

### 2-1. `AudioManager`는 수동 배치합니다

- `AudioManager`는 이제 코드에서 자동 생성되지 않습니다.
- 플레이 가능한 클라이언트 씬에 직접 배치해야 합니다.
- 권장 배치 씬:
  - `Login`
  - `Lobby`
  - `Matchmaking`
  - `GamePlay`
- `ServerScene`에는 배치하지 않습니다.

### 2-2. 씬 BGM은 `SceneAudioController`가 담당합니다

- 각 씬에 `SceneAudioController`를 하나만 둡니다.
- 이 컴포넌트는 `_bgmCue`만 지정하면 됩니다.

### 2-3. `SceneAudioController`가 없는 씬에서는 이전 BGM이 유지됩니다

- 현재 구현상 `_bgmCue == null`이면 아무 동작도 하지 않습니다.
- 따라서 씬 전환 시 BGM을 확실히 바꾸고 싶다면, 주요 씬마다 `SceneAudioController`를 넣어야 합니다.

### 2-4. 현재 코드는 `AudioMixer`를 필수로 사용하지 않습니다

- 문서나 계획에는 믹서 확장 방향이 있지만, 현재 런타임 코드는 `AudioMixer`를 직접 참조하지 않습니다.
- 지금 단계에서는 `AudioCue`와 필드 연결만으로 동작합니다.
- 믹서는 추후 볼륨 노출 파라미터를 붙일 때 추가해도 됩니다.

### 2-5. 사운드가 안 나도 Null 예외는 최대한 피하도록 작성되어 있습니다

- 대부분의 재생 코드는 `AudioManager.Instance?.Play...` 형태입니다.
- 하지만 사운드가 안 나는 상태로 방치되면 QA가 어려워지므로, 주요 필드는 반드시 채우는 편이 좋습니다.

## 3. 권장 폴더 구조

오디오 파일과 `AudioCue` 에셋은 아래처럼 정리하는 것을 권장합니다.

```text
Assets/Content/Sound/
  Bgm/
  UI/
  Player/
  Weapon/
    Gun/
    Melee/
    Skill/
  Mob/
  Item/
  Match/

Assets/Data/Audio/
  Bgm/
  UI/
  Player/
  Weapon/
  Mob/
  Item/
  Match/
```

권장 원칙:
- 실제 오디오 파일은 `Assets/Content/Sound/`
- `AudioCue` ScriptableObject는 `Assets/Data/Audio/`

## 4. 오디오 파일 준비

### 4-1. 파일 임포트

1. 사용할 오디오 파일을 `Assets/Content/Sound/` 하위로 복사합니다.
2. Unity가 리임포트할 때까지 기다립니다.
3. 각 파일의 Import Settings를 확인합니다.

권장 기본값:
- BGM:
  - `Load Type`: Streaming 또는 Compressed in Memory
  - `Compression Format`: Vorbis 권장
  - `Preload Audio Data`: 켜도 무방
- 짧은 UI/SFX:
  - `Load Type`: Decompress on Load 또는 Compressed in Memory
  - `Preload Audio Data`: 켬

### 4-2. 파일 네이밍 규칙 권장

- `bgm_login_loop`
- `bgm_lobby_loop`
- `ui_click_primary`
- `ui_confirm`
- `gun_pistol_fire_01`
- `gun_pistol_reload_start`
- `player_dash`
- `mob_alien_alert`

## 5. `AudioCue` 에셋 만들기

### 5-1. 생성 방법

Unity 메뉴:
- `Assets > Create > Project VOID > Audio > Audio Cue`

### 5-2. `AudioCue` 필드 설명

- `_clips`
  - 재생 가능한 오디오 클립 목록
  - 1개면 고정 재생
  - 2개 이상이면 랜덤 선택
- `_channel`
  - `Bgm`
  - `Ui`
  - `Sfx`
- `_volume`
  - 이 큐 자체의 기본 볼륨
- `_pitchRange`
  - 랜덤 피치 범위
  - 예: `(0.98, 1.02)`면 미세하게 흔들림
- `_spatialBlend`
  - `0`: 2D
  - `1`: 3D
- `_loop`
  - 루프 재생 여부
- `_minDistance`
  - 3D 사운드 최소 감쇠 거리
- `_maxDistance`
  - 3D 사운드 최대 거리

### 5-3. 추천 프리셋

#### BGM용

- `_channel = Bgm`
- `_volume = 1`
- `_pitchRange = (1, 1)`
- `_spatialBlend = 0`
- `_loop = true`
- `_minDistance`, `_maxDistance`는 의미 없음

#### UI용

- `_channel = Ui`
- `_volume = 1`
- `_pitchRange = (1, 1)` 또는 `(0.98, 1.02)`
- `_spatialBlend = 0`
- `_loop = false`

#### 일반 월드 SFX용

- `_channel = Sfx`
- `_volume = 1`
- `_pitchRange = (0.97, 1.03)` 정도 권장
- `_spatialBlend = 1`
- `_loop = false`
- `_minDistance = 1`
- `_maxDistance = 12 ~ 24`

#### 근접 타격/발사음용

- `_channel = Sfx`
- `_spatialBlend = 1`
- `_loop = false`
- `_minDistance = 1`
- `_maxDistance = 14 ~ 20`

## 6. 씬 BGM 설정

### 6-1. 원칙

- 각 씬에 `SceneAudioController` 하나를 둡니다.
- `_bgmCue`에 BGM용 `AudioCue`를 연결합니다.
- `_fadeDuration`으로 크로스페이드 시간을 조절합니다.

### 6-2. 씬별 권장 배치

#### Login 씬

현재 확인된 UI 오브젝트:
- `LoginUI`

설정 방법:
1. `Login` 씬을 엽니다.
2. 빈 오브젝트를 만들고 이름을 `SceneAudioController`로 지정합니다.
3. `SceneAudioController` 컴포넌트를 추가합니다.
4. `_bgmCue`에 로그인 BGM용 `AudioCue`를 연결합니다.
5. `_fadeDuration`은 `0.3 ~ 0.7` 사이로 설정합니다.

#### Lobby 씬

현재 확인된 오브젝트:
- `MatchmakingManager`
- `LobbyUI`

설정 방법:
1. `Lobby` 씬에 같은 방식으로 `SceneAudioController`를 추가합니다.
2. `_bgmCue`에 로비 BGM용 `AudioCue`를 연결합니다.

#### Matchmaking 씬

현재 확인된 오브젝트:
- `MatchmakingUI`

설정 방법:
1. `Matchmaking` 씬에 `SceneAudioController`를 추가합니다.
2. `_bgmCue`에 매칭/로딩 BGM용 `AudioCue`를 연결합니다.

#### GamePlay 씬

현재 확인된 오브젝트:
- `UIManager`
- `MobileUIManager`

설정 방법:
1. `GamePlay` 씬에 `SceneAudioController`를 추가합니다.
2. `_bgmCue`에 전투 BGM용 `AudioCue`를 연결합니다.

### 6-3. 중요한 주의사항

- `SceneAudioController`를 빼먹은 씬에서는 이전 씬 BGM이 그대로 유지됩니다.
- "이 씬은 무음이어야 한다"는 요구가 있으면 현재 구조에서는 별도 스크립트에서 `AudioManager.Instance.StopBgm()` 호출을 추가해야 합니다.

## 7. 데이터 에셋 설정

이 섹션은 "소리를 기능 옆에 붙이는" 핵심 설정입니다.

### 7-1. 아이템 공통 픽업음

대상 스크립트:
- `ItemData`

설정 필드:
- `Pickup Audio Cue`

적용 대상 예시:
- `Assets/Data/Armors/Armor.asset`
- `Assets/Data/Item/AmmoPack_Medium.asset`
- `Assets/Data/Item/HealthPack_Small.asset`
- 모든 무기 에셋도 `ItemData`를 상속하므로 공통 픽업음을 지정할 수 있습니다.

권장 방식:
- 방어구 픽업음 1종
- 소비 아이템 픽업음 1종
- 무기 픽업음 1종

### 7-2. 무기 공통 장착음

대상 스크립트:
- `WeaponData`

설정 필드:
- `Equip Audio Cue`

적용 대상 예시:
- `Assets/Data/Weapons/Gun/Pistol.asset`
- `Assets/Data/Weapons/Gun/Rifle.asset`
- `Assets/Data/Weapons/Gun/Shotgun.asset`
- `Assets/Data/Weapons/Gun/Sniper.asset`
- `Assets/Data/Weapons/Melee/Hammer.asset`
- `Assets/Data/Weapons/Melee/Hand.asset`

### 7-3. 총기 사운드

대상 스크립트:
- `GunData`

설정 필드:
- `Fire Audio Cue`
- `Reload Start Audio Cue`
- `Reload Complete Audio Cue`

권장 규칙:
- 권총/소총/샷건/스나이퍼는 발사음을 각각 분리
- 재장전 시작/완료음은 총기별로 따로 두거나 공용화 가능

### 7-4. 근접 무기 사운드

대상 스크립트:
- `MeleeWeaponData`

설정 필드:
- `Swing Audio Cue`
- `Hit Audio Cue`

적용 대상 예시:
- `Assets/Data/Weapons/Melee/Hammer.asset`
- `Assets/Data/Weapons/Melee/Hand.asset`

### 7-5. 근접 스킬 사운드

대상 스크립트:
- `MeleeSkillData`

설정 필드:
- `Cast Audio Cue`
- `Hit Audio Cue`

적용 대상 예시:
- `Assets/Data/Weapons/Melee/MeleeSkills/HammerSkill.asset`

### 7-6. 몹 사운드

대상 스크립트:
- `MobData`

설정 필드:
- `Alert Audio Cue`
- `Attack Audio Cue`
- `Hit Audio Cue`
- `Death Audio Cue`

적용 대상 예시:
- `Assets/Data/Mob/Alien.asset`
- `Assets/Data/Mob/VentMite.asset`

## 8. 프리팹 설정

### 8-1. Player 프리팹

대상 프리팹:
- `Assets/Prefab/Player/Player.prefab`

설정해야 할 컴포넌트:

#### `PlayerCombat`

필드:
- `_hitAudioCue`
- `_healAudioCue`
- `_deathAudioCue`
- `_victoryAudioCue`

권장:
- 피격음은 짧고 확실한 소리
- 회복음은 2D처럼 들릴 필요는 없지만 현재는 부착 재생이므로 3D SFX 큐 권장
- 승리음은 과하게 길지 않게 구성

#### `PlayerController`

필드:
- `_dashAudioCue`

권장:
- 짧은 이동/부스트 계열 사운드

### 8-2. Projectile 프리팹

대상 후보:
- `Assets/Prefab/Weapon/Gun/Projectile/SmallEnergyBullet/Projectile.prefab`
- 총알 프리팹이 여러 개라면 실제 사용하는 모든 발사체 프리팹에 적용

설정할 컴포넌트:
- `Projectile`

필드:
- `_hitAudioCue`

주의:
- 총기 발사음은 `GunData`에 넣고
- 탄환 충돌음은 `Projectile`에 넣습니다.

### 8-3. Mob 프리팹

대상 후보:
- `Assets/Prefab/Mob/MobAilen.prefab`

설정 원칙:
- `MobCombat`, `MobAnimationController`는 직접 오디오 필드가 거의 없습니다.
- 실제 소리는 `MobData`에서 가져옵니다.
- 따라서 Mob 프리팹 쪽에서는 `MobCombat`이 올바른 `MobData`를 참조하고 있는지만 확인하면 됩니다.

## 9. 씬 오브젝트 설정

### 9-1. Login 씬

대상 오브젝트:
- `LoginUI`

설정할 필드:
- `_uiClickAudioCue`

이 필드는 아래 버튼들에 사용됩니다.
- Google 로그인
- Apple 로그인
- Guest 로그인
- 닉네임 확인

### 9-2. Lobby 씬

대상 오브젝트:
- `LobbyUI`
- `MatchmakingManager`

#### `LobbyUI`

설정할 필드:
- `_uiClickAudioCue`
- `_selectionAudioCue`

용도:
- 모드 선택
- 서브 스위치 선택
- 커스텀 인원/시간/맵 좌우 버튼
- 시작 버튼

#### `MatchmakingManager`

설정할 필드:
- `_matchCompleteAudioCue`
- `_sceneTransitionAudioCue`
- `_gameStartedAudioCue`
- `_cancelAudioCue`

용도:
- 매칭 완료
- 씬 전환
- 게임 시작
- 매칭 취소

### 9-3. Matchmaking 씬

대상 오브젝트:
- `MatchmakingUI`

설정할 필드:
- `_uiClickAudioCue`

용도:
- 취소 버튼

### 9-4. GamePlay 씬

대상 오브젝트:
- `UIManager`
- `MobileUIManager`
- `DebugUIManager`가 있다면 해당 오브젝트

#### `UIManager`

설정할 필드:
- `_uiClickAudioCue`
- `_pauseOpenAudioCue`
- `_pauseCloseAudioCue`
- `_victoryAudioCue`
- `_defeatAudioCue`
- `_tieAudioCue`
- `_cardSelectAudioCue`

용도:
- 카드 선택
- 일시정지 열기/닫기
- 종료 확인/취소
- 결과 UI

#### `MobileUIManager`

주의:
- `MobileUIManager`는 `UIManager` 상속 구조입니다.
- GamePlay 씬에 `UIManager`와 `MobileUIManager`가 모두 존재한다면, 실제로 어떤 인스턴스가 사용되는지 확인해야 합니다.

권장:
- 두 오브젝트 모두 동일한 오디오 큐를 채워 둡니다.

#### `DebugUIManager`

설정할 필드:
- `_toggleAudioCue`

용도:
- 디버그 패널 토글

### 9-5. LoadingUIManager

대상:
- `LoadingUIManager`가 실제로 배치된 씬/프리팹

설정할 필드:
- `_countdownStartAudioCue`

용도:
- 카운트다운 패널 표시 시작 시 재생

주의:
- 현재 저장소 탐색만으로는 배치 위치가 즉시 보이지 않을 수 있습니다.
- 실제 씬에서 `LoadingUIManager` 인스턴스를 찾아 필드를 채워야 합니다.
- 없다면 Lobby 진입 시 생성되는 구조가 아니라면 별도 배치가 필요합니다.

## 10. 실제 작업 순서 추천

아래 순서대로 하면 가장 덜 헷갈립니다.

### 10-1. 1차: 공용 `AudioCue` 만들기

먼저 아래 큐부터 만듭니다.

- BGM
  - `BGM_Login`
  - `BGM_Lobby`
  - `BGM_Matchmaking`
  - `BGM_Gameplay`
- UI
  - `UI_Click`
  - `UI_Select`
  - `UI_Cancel`
  - `UI_CountdownStart`
  - `UI_Victory`
  - `UI_Defeat`
  - `UI_Tie`
- 플레이어
  - `Player_Hit`
  - `Player_Heal`
  - `Player_Death`
  - `Player_Dash`
  - `Player_Victory`
- 총기
  - `Gun_Pistol_Fire`
  - `Gun_Reload_Start`
  - `Gun_Reload_Complete`
- 근접
  - `Melee_Swing`
  - `Melee_Hit`
  - `Skill_Cast`
  - `Skill_Hit`
- 몹
  - `Mob_Alert`
  - `Mob_Attack`
  - `Mob_Hit`
  - `Mob_Death`
- 아이템
  - `Item_Pickup`
  - `Consumable_Pickup`

### 10-2. 2차: 씬 BGM 연결

씬마다 `SceneAudioController`를 추가합니다.

### 10-3. 3차: 데이터 에셋 연결

무기/몹/아이템 에셋부터 채웁니다.

### 10-4. 4차: 프리팹 연결

`Player.prefab`, `Projectile.prefab`부터 채웁니다.

### 10-5. 5차: UI 씬 연결

`LoginUI`, `LobbyUI`, `MatchmakingUI`, `UIManager`, `DebugUIManager`, `LoadingUIManager` 순으로 채웁니다.

## 11. 빠른 점검 체크리스트

### 11-1. 씬 진입

- `Login` 진입 시 로그인 BGM 재생
- `Lobby` 진입 시 로비 BGM 전환
- `Matchmaking` 진입 시 매칭 BGM 전환
- `GamePlay` 진입 시 전투 BGM 전환

### 11-2. UI

- 로그인 버튼 클릭음
- 로비 모드 전환음
- 로비 시작 버튼음
- 매칭 취소 버튼음
- 카드 선택음
- 일시정지 열기/닫기음
- 결과음

### 11-3. 전투

- 총 발사음
- 재장전 시작/완료음
- 근접 휘두름음/타격음
- 스킬 발동음/히트음
- 탄환 충돌음
- 피격음/회복음/사망음
- 대시음
- 몹 경계음/공격음/피격음/사망음
- 아이템 픽업음

## 12. 자주 틀리는 포인트

### 12-1. `AudioManager`를 아예 안 넣는 실수

- 현재는 수동 배치 구조입니다.
- 클라이언트 씬에 `AudioManager`가 없으면 사운드가 재생되지 않습니다.
- 특히 씬 단독 실행 테스트를 자주 한다면 `Login`, `Lobby`, `Matchmaking`, `GamePlay` 모두에 넣는 편이 안전합니다.

### 12-2. `SceneAudioController`를 안 넣고 BGM이 안 바뀐다고 생각하는 경우

- 현재 구조에서는 안 바뀌는 것이 정상입니다.
- 컨트롤러가 없는 씬은 이전 BGM이 유지됩니다.

### 12-3. `AudioCue` 채널을 잘못 두는 경우

- BGM인데 `Sfx`
- UI 버튼인데 `Sfx`
- 월드 사운드인데 `Ui`

이렇게 설정하면 볼륨 제어와 공간감이 어색해집니다.

### 12-4. 3D 사운드인데 `Spatial Blend = 0`으로 두는 경우

- 총기, 대시, 몹, 아이템 픽업처럼 월드에 있어야 하는 소리가 화면 중앙에서만 들립니다.

### 12-5. MobileUIManager 필드를 안 채우는 경우

- GamePlay 씬에서 `UIManager`만 채우고 `MobileUIManager`는 비워 두면 플랫폼에 따라 빠지는 소리가 생길 수 있습니다.

### 12-6. 데이터 에셋은 채웠는데 프리팹 필드를 안 채우는 경우

- 예:
  - `PlayerCombat`
  - `PlayerController`
  - `Projectile`
  - `MatchmakingManager`
  - `LoadingUIManager`

이쪽은 데이터가 아니라 컴포넌트 직결 필드이므로 별도로 채워야 합니다.

## 13. 현재 구현 기준에서 아직 없는 것

아래는 현재 가이드 범위 밖입니다.

- 발소리 시스템
- 환경 앰비언스 시스템
- 믹서 기반 세부 볼륨 노출 UI
- 씬별 무음 전환 전용 컴포넌트
- 공용 UI 사운드 자동 바인더

따라서 현재는 "수동 연결 중심"으로 세팅하는 것이 맞습니다.

## 14. 최종 권장 방식

이 프로젝트에서 가장 안전한 설정 방식은 아래입니다.

1. `AudioManager`는 플레이 가능한 클라이언트 씬에 직접 배치합니다.
2. 각 씬에는 `SceneAudioController`를 넣습니다.
3. 무기/몹/아이템은 데이터 에셋에 오디오 큐를 연결합니다.
4. 플레이어/투사체/UI/매치 흐름은 각 컴포넌트 인스펙터에서 직접 연결합니다.
5. 공용 오디오를 만들더라도 "전역 설정 하나"에 몰아넣지 않습니다.

이 원칙으로 가면 구조가 가장 덜 꼬이고, 기능 추가 시 수정 위치도 명확합니다.
