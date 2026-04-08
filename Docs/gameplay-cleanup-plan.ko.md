# 게임플레이 코드 정리·최적화 통합 계획

작성일: 2026-04-05  
성격: **계획 문서 전용**  
범위: 게임플레이 관련 코드의 비효율 로직, 최적화 후보, 과한 주석 정리 기준 수립  
비범위: 이번 문서에서는 **구현하지 않음**

## 1. 목적

이 문서는 팀 분석으로 나온 3개의 계획 초안을 하나로 통합한 최종 기준 문서다.
목표는 다음 3가지를 우선순위와 검증 기준까지 포함해 정리하는 것이다.

1. 프레임/틱 루프에서 반복되는 비효율 로직 식별
2. 할당, 탐색, 물리 쿼리, 이펙트 생성 비용이 큰 구간 식별
3. 유지보수 가치가 낮은 주석을 정리하는 기준 정의

## 2. 범위와 제외 항목

### 포함 범위
- 플레이어 이동, 입력, 전투
- 몹 AI, 몹 전투, 몹 스폰
- 무기, 투사체, 아이템 드랍/장착
- 맵 생성, 맵 축소, FOV, 렌더 보조 로직
- 전장 UI, 모바일 조작 UI, 픽업 UI
- 경기 상태 집계 및 게임플레이 상태 갱신
- 카드 드로우/수정자 적용 관련 로직

### 우선순위에서 제외
- `Assets/Scripts/Core/Auth/**`
- 로그인 UI, 계정 연동, 인증 예외 처리
- 인증/전적 적재 전용 블록

### 1차 우선순위 후순위 영역
- `Assets/Scripts/Core/NetworkManager.cs`
- `Assets/Scripts/Core/MatchmakingManager.cs`
- `Assets/Scripts/UI/LobbyUI.cs`
- `Assets/Scripts/Editor/**`

위 영역은 코드 규모는 크지만, 현재 요청의 핵심인 **실시간 게임플레이 루프 최적화**보다 세션/로비/에디터 책임이 더 강하므로 후순위로 둔다.

## 3. 조사 근거 요약

### 주석 밀도 상위 파일
- `Assets/Scripts/Items/NetworkedItem.cs` — 주석 107줄 / 778줄
- `Assets/Scripts/Weapons/NetworkedWeapon.cs` — 주석 170줄 / 1249줄
- `Assets/Scripts/Player/PlayerController.cs` — 주석 106줄 / 799줄
- `Assets/Scripts/Map/MapGenerator.cs` — 주석 175줄 / 1426줄
- `Assets/Scripts/Map/OxygenDepletionManager.cs` — 주석 191줄 / 1618줄

### 반복 비용이 확인된 대표 패턴
- 프레임 루프 polling: `PlayerController`, `NetworkedWeapon`, `NetworkedItem`, `MobAI`, `GameStateManager`, `UIManager`, `MobileUIManager`, `MobOverheadUI`
- 물리/탐색 쿼리 반복: `NetworkedWeapon`, `MobAI`, `MobSpawnManager`, `ItemPickupDetector`
- 전체 순회/정렬/복사: `MapGenerator`, `OxygenDepletionManager`, `NetworkMapManager`
- UI 재생성 churn: `UIManager`, 픽업 UI, 카드 UI, 오버헤드 UI
- 컴포넌트 재조회: `PlayerController`, `NetworkedWeapon`, `MobAI`, `NetworkedItem`, `LootBox`

## 4. 우선순위별 실행 계획

### P0 — 체감 성능에 직접 닿는 실시간 루프 정리

| 영역 | 대표 파일 | 핵심 문제 | 정리 방향 | 위험도 | 선행 검증 |
| --- | --- | --- | --- | --- | --- |
| 플레이어 이동/입력 | `Assets/Scripts/Player/PlayerController.cs` | `GetComponent` 반복, 입력/이동/외삽/오디오 책임 밀집, 설명성 주석 과다 | 컴포넌트 캐시, owner/remote 경로 분리, 상태 변화 기반 갱신 | 중간 | 로컬/원격 이동 회귀, 대시/스킬 이동 재현, GC Alloc 측정 |
| 무기/투사체/근접 판정 | `Assets/Scripts/Weapons/NetworkedWeapon.cs` | 공격 경로에서 `GetComponent` 재조회, `Physics.OverlapSphere` 반복, VFX `Instantiate/Destroy`, 주석 과다 | 판정/연출 분리, hit buffer 재사용 검토, VFX 풀링 후보 분리 | 높음 | 총기/근접/스킬 회귀, 서버/클라 판정 비교, 발사당 GC/Physics 비용 측정 |
| 드랍 아이템/장착 상태 | `Assets/Scripts/Items/NetworkedItem.cs`, `Assets/Scripts/Items/LootBox.cs` | 상태 변화가 없어도 반복 처리, 장착점/무기 참조 재조회, 로그 과다 | 상태 전환 기반 갱신, attach point 캐시, 운영 로그/디버그 로그 구분 | 높음 | 줍기/드랍/교체/소모 회귀, 대량 드랍 부하 측정 |
| 몹 탐지/추적 | `Assets/Scripts/Mobs/MobAI.cs` | 감지 루프에서 거리 계산/컴포넌트 조회 반복, 상태 전이와 탐지 책임 혼합 | 탐지 후보 캐시 검토, sqrMagnitude 우선 검토, 상태 전이 분리 | 높음 | 몹 10/30/50 부하 측정, 타깃 전환/복귀 회귀 |

#### P0 세부 착수 순서
1. `PlayerController`
2. `NetworkedWeapon`
3. `NetworkedItem` / `LootBox`
4. `MobAI`

> 이유: 전투, 입력, 이동은 프레임 타임과 체감 반응성에 가장 직접적이다.

---

### P1 — 전역 polling 및 시스템성 루프 정리

| 영역 | 대표 파일 | 핵심 문제 | 정리 방향 | 위험도 | 선행 검증 |
| --- | --- | --- | --- | --- | --- |
| 전장 UI 전체 갱신 | `Assets/Scripts/UI/UIManager.cs` | `Update()`에서 전장 UI 전체를 매 프레임 갱신, 픽업/카드 UI 재생성 중심 | 이벤트 기반 UI, dirty flag, 엔트리 재사용 | 중간 | HUD/카드/픽업 패널 회귀, UI rebuild/GC 측정 |
| 모바일 로컬 플레이어 탐색 | `Assets/Scripts/UI/Mobile/MobileUIManager.cs` | 플레이어 미초기화 시 전역 탐색 반복 | spawn 이벤트 기반 연결, 재시도 간격 제한 | 중간 | 모바일 씬 진입 직후 CPU spike 측정 |
| 픽업 감지 | `Assets/Scripts/Player/ItemPickupDetector.cs` | `Physics.SyncTransforms()`, `OverlapSphere()`, `List.Contains` 기반 비교 반복 | 감지 쿨다운 재검토, 조건부 동기화, ID 집합 사용 | 중간 | 아이템 밀집 지역 감지 부하 측정 |
| FOV 계산/갱신 | `Assets/Scripts/FOV/FOVController.cs` | 조준 변화가 없어도 FOV 계산/메시 갱신 반복 | 변화량 기반 갱신, 계산과 반영 단계 분리 | 높음 | 정지/회전 상태 비용 비교, PC/모바일 렌더 비교 |
| 맵 축소/경고/데미지 | `Assets/Scripts/Map/OxygenDepletionManager.cs` | phase 진행, 경고 처리, 데미지 적용, 경고등 갱신이 한 클래스에 과집중 | 플레이어 목록 캐시, light 캐시, phase/damage/visual 분리 | 높음 | phase 전후 spike, 경고등/데미지 동기화 검증 |
| 몹 스폰/오버헤드 UI | `Assets/Scripts/Mobs/MobSpawnManager.cs`, `Assets/Scripts/UI/MobOverheadUI.cs` | `GameObject.Find`, 캔버스 탐색, UI 생성/파괴 반복 | 초기화 시점 고정, 부모 참조 캐시, UI 재사용 검토 | 중간 | 대량 스폰/디스폰, 씬 전환 후 UI 복구 회귀 |

#### P1 세부 착수 순서
1. `UIManager`
2. `OxygenDepletionManager`
3. `FOVController`
4. `ItemPickupDetector`
5. `MobileUIManager`
6. `MobSpawnManager` / `MobOverheadUI`

> 이유: 이 영역은 “한 번 느리다”보다 “계속 돌기 때문에 누적된다”는 특성이 강하다.

---

### P2 — 생성, 집계, 동기화 경로 단순화

| 영역 | 대표 파일 | 핵심 문제 | 정리 방향 | 위험도 | 선행 검증 |
| --- | --- | --- | --- | --- | --- |
| 맵 생성/직렬화 | `Assets/Scripts/Map/MapGenerator.cs`, `Assets/Scripts/Map/ChunkInstance.cs` | 정렬/리스트/해시셋 반복, 대표 청크/연결성 검증 경로 복잡, 주석 과다 | 정렬 결과 재사용, 대표 청크 캐시, 생성 규칙과 인스턴스화 분리 | 높음 | 동일 seed 비교, 연결성/문 규칙 회귀 |
| 맵 동기화/스폰 보조 | `Assets/Scripts/Network/NetworkMapManager.cs` | SyncList 복사, 스폰 위치 계산, NavMesh 샘플링 반복 | 동기화와 스폰 보조 경로 분리, 스폰 포인트 계산 캐시 검토 | 중간 | late join spike, player spawn 반복 측정 |
| 경기 상태 갱신 | `Assets/Scripts/Core/GameStateManager.cs` | 시간 집계, 종료 판정, 세션 상태 갱신, 플레이어 탐색이 한 클래스에 밀집 | 상태 집계/외부 업데이트/결과 생성 분리 | 중간 | phase 변경, 종료 직전, 플레이어 이탈 시 self time 비교 |
| 카드 드로우/수정자 | `Assets/Scripts/Player/PlayerCardSystem.cs` | `Select(...).ToArray()`, `Where(...).ToList()`, modifier 생성 반복 | 후보 계산 할당 축소, metadata와 runtime 생성 분리 | 중간 | 카드 등장 규칙/희귀도/중복 방지 회귀 |

#### P2 세부 착수 순서
1. `MapGenerator` / `ChunkInstance`
2. `NetworkMapManager`
3. `GameStateManager`
4. `PlayerCardSystem`

> 이유: 실행 빈도는 P0/P1보다 낮지만, 한 번의 스파이크가 크고 클래스 책임이 비대하다.

---

### P3 — 주석 정리 전용 패스

#### 유지할 주석
- 네트워크 권한, 동기화 순서, owner/server 분기 같은 **실수 비용이 큰 제약**
- Fish-Net, NavMesh, Shader, Unity lifecycle 등 **엔진/라이브러리 제약**
- 코드만 보고는 드러나지 않는 **게임 규칙/의사결정 근거**
- 성능 최적화가 왜 필요한지 설명하는 **최신 의도 주석**

#### 삭제/축소 대상
- 코드 한 줄을 그대로 설명하는 절차형 주석
- 메서드/변수명이 이미 설명하는 중복 설명
- 죽은 디버그 주석, 오래된 TODO, placeholder 주석
- 과거 수정 이력 메모(`[REMOVED]`, 임시 실험 흔적 등)
- 한 메서드 안에서 단계마다 붙은 튜토리얼식 주석

#### 주석 정리 우선 대상 파일
- `Assets/Scripts/Map/ChunkInstance.cs`
- `Assets/Scripts/Weapons/NetworkedWeapon.cs`
- `Assets/Scripts/Player/PlayerController.cs`
- `Assets/Scripts/Player/PlayerInputHandler.cs`
- `Assets/Scripts/Items/NetworkedItem.cs`
- `Assets/Scripts/Rendering/FOVStencilClipPass.cs` 또는 동급 렌더 패스 파일

#### 원칙
1. 주석 정리는 **기능/성능 구조 정리 후 마지막 패스**로 수행한다.
2. 주석을 지우며 의도가 약해지면, 주석 대신 메서드명/헬퍼명/구조 분리로 의도를 드러낸다.
3. 주석 정리 커밋과 성능 최적화 커밋은 가능하면 분리한다.

## 5. 구현 전 검증 체크리스트

### 공통 베이스라인
- 동일 seed, 동일 플레이 수, 동일 장비 조건으로 재현 가능한 시나리오 고정
- Unity Profiler에서 최소 다음 항목 수집
  - CPU Usage
  - GC Alloc
  - Physics
  - Scripts
  - UI Rebuild / Canvas.SendWillRenderCanvases
- 서버/클라이언트 각각 3분 이상 soak test
- 모바일/PC 각각 최소 1회 동일 시나리오 비교

### 필수 회귀 시나리오
- 이동, 대시, 근접, 총기, 스킬
- 줍기, 드랍, 장착 교체, 소비 아이템 사용
- 몹 감지, 공격, 사망, 리스폰
- phase 진행, 맵 축소, 위험 구역 데미지
- late join, 로컬 플레이어 재생성, 카메라 재연결
- 카드 드로우, 중복 방지, 무기 타입 제한

### 작업 단위 규칙
- 한 번에 한 영역만 정리하고 수치를 남긴다.
- 네트워크가 얽힌 로직은 반드시 서버/클라이언트 동시 검증한다.
- `Instantiate/Destroy`를 줄이면 메모리 증가와 재사용 누수도 함께 본다.
- AI/맵 최적화는 평균 프레임보다 **스파이크 프레임 감소**를 우선 지표로 둔다.

## 6. 권장 실행 순서

1. **P0 측정 및 리팩터링**
   - `PlayerController`
   - `NetworkedWeapon`
   - `NetworkedItem` / `LootBox`
   - `MobAI`
2. **P1 측정 및 구조 분리**
   - `UIManager`
   - `OxygenDepletionManager`
   - `FOVController`
   - `ItemPickupDetector`
   - `MobileUIManager`
   - `MobSpawnManager` / `MobOverheadUI`
3. **P2 스파이크 완화 작업**
   - `MapGenerator` / `ChunkInstance`
   - `NetworkMapManager`
   - `GameStateManager`
   - `PlayerCardSystem`
4. **P3 주석 정리 전용 패스**
5. **문서 업데이트**
   - 실제 수정 후에는 이 문서 하단 또는 별도 작업 로그에 측정값과 회귀 결과를 누적한다.

## 7. 최종 결론

가장 먼저 손대야 할 곳은 **플레이어/무기/아이템/몹 AI의 실시간 루프**다. 다음으로 **UI polling, 산소 구역 갱신, FOV 상시 갱신**을 줄여야 한다. 그다음에 **맵 생성/동기화와 경기 상태 집계**처럼 스파이크를 만드는 넓은 책임 클래스들을 분리한다.

주석 정리는 독립 패스로 분리하되, **왜 필요한지와 어떤 제약이 있는지**를 설명하는 주석만 남기고, 절차 설명·중복 설명·죽은 메모는 제거한다.

이 문서를 현재 프로젝트의 **단일 실행 기준 계획서**로 사용한다.
