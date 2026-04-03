# FOV 재구성 시 유지할 기능 체크리스트

작성일: 2026-04-03  
기준 브랜치: `refs/heads/#16-카드-시스템`의 플레이 체감  
현재 분석 기준: `#17-Mobile` 작업 트리, `.omx/context/fov-system-rearchitecture-20260403T101304Z.md`

---

## 1. 목적

이 문서는 FOV 시스템을 처음부터 다시 정리하더라도 **반드시 유지해야 하는 플레이 체감과 연동 기능**을 고정하기 위한 체크리스트입니다.

핵심 원칙은 다음 2가지입니다.

1. **유지 기준은 구현 방식이 아니라 플레이 결과**다.
2. **`#16`에서 좋았던 체감은 유지하되, 성능 저하와 바닥선 아티팩트는 유지 대상이 아니다.**

---

## 2. 이번 체크리스트의 범위

### 포함
- 시야 계산 규칙
- 바닥 FOV/오버레이 시각 효과
- Player / Mob / Item / Projectile 가시성 규칙
- Overhead UI / Damage Indicator의 FOV 연동 규칙
- `FOVController` public API 호환성
- PC/모바일 공용 성공 기준

### 제외
- `PlayerOcclusionFader` 기반 카메라-플레이어 사이 벽 투명화 로직
  - 이 시스템은 FOV 밖/안 판정과 별개이며, 이번 재구성의 기준선에서 분리해 다뤄야 합니다.
- `#16`에서 이미 문제로 확인된 성능 저하, 바닥 경계선 비침 같은 결함
  - 이것은 **유지 기능이 아니라 제거 대상 문제**입니다.

---

## 3. 반드시 유지할 기능 체크리스트

## A. 시야 규칙 / 게임플레이 체감

- [ ] **기본 시야는 360도 원형 시야여야 한다.**
  - 근거: `Docs/FOVSystem.md`의 "원형 (Circular)" 정의, `Assets/Scripts/FOV/FOVController.cs`
- [ ] **원거리 무기 조준 시 부채꼴 시야로 전환되어야 한다.**
  - 근거: `Docs/FOVSystem.md`의 "부채꼴 (Fan)" / "복합 (Composite)" 설명
- [ ] **조준 전환 시 시야 변화가 즉시 튀지 않고 부드럽게 전환되어야 한다.**
  - 근거: `Docs/FOVSystem.md`의 "시야 전환 애니메이션"
- [ ] **장애물 뒤 영역은 시야에서 제외되어야 한다.**
  - 근거: `FOVCalculator` 기반 레이캐스트 구조, `Docs/FOVSystem.md` 2.3
- [ ] **로컬 플레이어만 자신의 FOV를 계산/표시해야 한다.**
  - 근거: `Assets/Scripts/FOV/FOVController.cs`, `OnStartClient()`에서 비소유자 비활성화
- [ ] **시야 질의(`IsInsideFOV`, `IsColliderInsideFOV`) 결과와 실제 화면 가시성이 체감상 일치해야 한다.**
  - 근거: UI/데미지 인디케이터가 이 API에 직접 의존함

## B. 바닥 시각 효과 / FOV 오버레이

- [ ] **플레이어 주변의 FOV 메시/오버레이는 계속 표시되어야 한다.**
  - 근거: `Docs/FOVSystem.md` 1.3, `FOVRenderPass`
- [ ] **FOV 외부 영역이 어두워지는 현재 표현은 유지되어야 한다.**
  - 근거: `Assets/Scripts/Rendering/FOVRendererFeature.cs`, `overlayMaterial`
- [ ] **Soft Edge / Falloff 기반의 경계 감쇠 체감은 유지되어야 한다.**
  - 근거: `Assets/Scripts/FOV/FOVController.cs`의 `_enableSoftEdge`, `_edgeSoftness`, `_falloffExp`
- [ ] **바닥 경계선/배경선이 플레이어·몬스터 몸 위로 덮이는 아티팩트는 재구성 후 없어야 한다.**
  - 비고: 이것은 기존 결함 제거 조건이며, 재현 대상이 아님
- [ ] **Scene View 가시성은 유지하되, Game View 가시성 로직과 혼선이 없어야 한다.**
  - 근거: `FOVRendererFeature`의 MainCamera / SceneView 분기, `FOVController`의 `_GlobalStencilComp` 초기화

## C. 게임 오브젝트 가시성

- [ ] **Player는 시야 안에서만 정상적으로 보여야 한다.**
- [ ] **Mob는 시야 안에서만 정상적으로 보여야 한다.**
- [ ] **Item은 시야 안에서만 정상적으로 보여야 한다.**
- [ ] **Projectile은 시야 안에서만 정상적으로 보여야 한다.**
- [ ] **"부분적으로 시야에 걸친 오브젝트"는 전부 사라지지 않고 자연스럽게 부분 가시성을 유지해야 한다.**
  - 비고: 단순 `forceRenderingOff` 일괄 차단은 기준 미달
- [ ] **시야 밖 오브젝트는 바닥 오버레이보다 앞에 잘못 떠 보이지 않아야 한다.**
- [ ] **주요 게임플레이 오브젝트는 가능하면 기본 렌더 경로 1회로 처리되어야 한다.**
  - 근거: `.omx/context/fov-system-rearchitecture-20260403T101304Z.md`의 성능 목표

### 현재 FOV 가시성 연동 대상(확인됨)
- `PlayerController`: `FOVStencilMaterialRuntimeApplier.ApplyToHierarchy(gameObject)`
- `MobAI`: `FOVStencilMaterialRuntimeApplier.ApplyToHierarchy(gameObject)`
- `NetworkedItem`: `FOVStencilMaterialRuntimeApplier.ApplyToHierarchy(gameObject)`
- `Projectile`: `FOVStencilMaterialRuntimeApplier.ApplyToHierarchy(gameObject)`

즉, 재구성 후에도 위 4종은 **기능적으로 동일한 가시성 규칙**을 유지해야 합니다.

## D. UI / 피드백 연동

- [ ] **Player Overhead UI는 대상이 FOV 안에 있을 때만 보여야 한다.**
  - 근거: `Assets/Scripts/UI/PlayerOverheadUI.cs`
- [ ] **Mob Overhead UI는 대상이 FOV 안에 있을 때만 보여야 한다.**
  - 근거: `Assets/Scripts/UI/MobOverheadUI.cs`
- [ ] **Damage Indicator는 생성 시점 기준 FOV 안일 때만 생성되어야 한다.**
  - 근거: `Assets/Scripts/UI/DamageIndicatorManager.cs`
- [ ] **콜라이더 일부라도 FOV 안에 들어오면 UI가 보이는 현재 규칙은 유지되어야 한다.**
  - 근거: `IsColliderInsideFOV(Collider)` 사용 경로

## E. API / 외부 의존성 호환

- [ ] **`FOVController.LocalInstance`는 계속 유효해야 한다.**
- [ ] **`IsInsideFOV(Vector3)` 시그니처와 의미는 유지해야 한다.**
- [ ] **`IsColliderInsideFOV(Collider)` 시그니처와 의미는 유지해야 한다.**
- [ ] **`ShowFOV()` / `HideFOV()` 호출 지점은 깨지지 않아야 한다.**
- [ ] **비소유 클라이언트에서 FOV 시스템이 비활성화되는 현재 네트워크 규칙은 유지해야 한다.**

## F. 플랫폼 / 성능 / 설정 일관성

- [ ] **PC와 Mobile 렌더러 모두 같은 FOV 기능 규칙을 만족해야 한다.**
- [ ] **PC/Mobile Renderer Asset의 FOV 관련 설정 차이는 의도적으로 관리되어야 한다.**
  - 현재 확인값:
    - `Assets/Settings/PC_Renderer.asset` → `stencilClipLayers = 0`, Opaque/Transparent mask = 전체 포함
    - `Assets/Settings/Mobile_Renderer.asset` → `stencilClipLayers = 0`, Opaque/Transparent mask = 현재 마스크 값 유지
- [ ] **모바일에서 추가 재렌더/머티리얼 인스턴스 난발로 성능이 무너지지 않아야 한다.**
- [ ] **별도 clip pass 재렌더가 다시 핵심 경로가 되지 않도록 주의해야 한다.**

---

## 4. 재구성 완료 판정용 최소 성공 기준

아래 항목이 모두 만족되어야 "유지 기능 보존"으로 판정합니다.

1. 플레이어 기본 상태에서 360도 시야가 정상 동작한다.
2. 총기 조준 시 부채꼴/복합 시야 전환이 자연스럽다.
3. 벽 뒤 영역은 계속 가려진다.
4. Player / Mob / Item / Projectile가 시야 안/밖에서 기대대로 보이거나 숨겨진다.
5. PlayerOverheadUI / MobOverheadUI / DamageIndicator의 표시 규칙이 깨지지 않는다.
6. 시야 밖 오브젝트에서 바닥선이 몸 위로 덮이는 아티팩트가 없다.
7. PC와 모바일 렌더러에서 동일한 플레이 규칙을 만족한다.
8. `IsInsideFOV`, `IsColliderInsideFOV`, `LocalInstance`에 의존하는 기존 코드가 그대로 동작한다.

---

## 5. 이후 단계에 넘길 메모

- 이 체크리스트는 **"무엇을 반드시 남길지"**를 고정하는 문서입니다.
- 별도 문서에서 정리할 항목:
  1. 버릴 구조 목록
  2. 목표 아키텍처
  3. Phase별 구현/검증 계획
- 구현 단계에서 논쟁이 생기면 **구현 편의보다 이 체크리스트가 우선**입니다.
