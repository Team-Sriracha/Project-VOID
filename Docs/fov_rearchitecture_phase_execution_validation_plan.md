# FOV 재구성 Phase별 구현/검증 계획

작성일: 2026-04-03
전제: 이 문서는 **분석 + 계획 수립 전용**이며, 현재 단계에서는 코드 수정 범위를 확정만 합니다.
기준 문서:
- `Docs/fov_rearchitecture_preservation_checklist.md`
- `implementation_plan.md`
- `.omx/context/fov-system-rearchitecture-20260403T101304Z.md`

---

## 1. 계획 목적

이 계획의 목적은 FOV 재구성을 "좋아 보이는 리팩터링"이 아니라 **단계별 구현/검증이 가능한 작업 순서**로 쪼개는 것입니다.

핵심 원칙은 다음과 같습니다.

1. 한 Phase는 하나의 책임만 바꾼다.
2. 매 Phase 종료 시점마다 **플레이 체감 + API + 성능**을 같이 검증한다.
3. `Player / Mob / Item / Projectile`를 다시 그리는 구조는 최종 목표에서 제거한다.
4. `LocalInstance`, `IsInsideFOV`, `IsColliderInsideFOV`를 쓰는 기존 코드는 최대한 유지한다.

---

## 2. 전체 순서 요약

| Phase | 목적 | 핵심 산출물 | 선행 조건 |
| --- | --- | --- | --- |
| Phase 0 | 기준선 고정 | 유지 기능 체크리스트, 자산 inventory, 성공 기준 | 없음 |
| Phase 1 | 시야 계산층 분리 | 단일 FOV 데이터 모델 / 질의 기준 통합 | Phase 0 |
| Phase 2 | 오버레이 렌더층 단순화 | 바닥 FOV 메시/오버레이 전용 렌더 구조 | Phase 1 |
| Phase 3 | 오브젝트 가시성층 교체 | 재렌더 없는 Player/Mob/Item/Projectile 가시성 경로 | Phase 1, 2 |
| Phase 4 | UI/API/플랫폼 정합성 마감 | UI 연동, Renderer Asset 정리, 성능/회귀 검증 | Phase 3 |

---

## 3. Phase 0 — 기준선 동결

## 목표
- 이후 구현 단계에서 "무엇을 유지해야 하는가"를 다시 논쟁하지 않도록 기준선을 먼저 고정합니다.

## 이번 Phase 산출물
- `Docs/fov_rearchitecture_preservation_checklist.md`
- 유지 대상 API / UI / 오브젝트 목록
- PC/Mobile 렌더러 현재 설정 스냅샷

## 확인 대상 파일
- `Assets/Scripts/FOV/FOVController.cs`
- `Assets/Scripts/UI/PlayerOverheadUI.cs`
- `Assets/Scripts/UI/MobOverheadUI.cs`
- `Assets/Scripts/UI/DamageIndicatorManager.cs`
- `Assets/Scripts/Player/PlayerController.cs`
- `Assets/Scripts/Mobs/MobAI.cs`
- `Assets/Scripts/Items/NetworkedItem.cs`
- `Assets/Scripts/Weapons/Projectile.cs`
- `Assets/Settings/PC_Renderer.asset`
- `Assets/Settings/Mobile_Renderer.asset`

## 검증
### 문서 검증
- 유지 기능이 시야 규칙 / 오버레이 / 오브젝트 / UI / API / 플랫폼 관점으로 분리되어 있는가
- 제거 대상 결함(바닥선 아티팩트, 재렌더 성능 문제)이 유지 목록에 섞여 있지 않은가

### 기준선 검증
- `PlayerOverheadUI`, `MobOverheadUI`, `DamageIndicatorManager`가 실제로 `FOVController` 질의에 의존하는지 코드로 확인
- `Player/Mob/Item/Projectile`가 현재 FOV 대응 경로에 올라타는지 확인

## 완료 조건
- 구현자가 이 문서만 보고 "유지해야 할 기능"을 바로 판단할 수 있어야 함

---

## 4. Phase 1 — 시야 계산층 분리

## 목표
- 렌더링과 무관한 **단일 FOV 데이터 모델**을 먼저 분리합니다.
- 질의 API와 렌더 입력이 같은 기준 데이터를 쓰도록 맞춥니다.

## 예상 수정 범위
- `Assets/Scripts/FOV/FOVController.cs`
- `Assets/Scripts/FOV/FOVCalculator.cs`
- 필요 시 신규 파일:
  - `Assets/Scripts/FOV/FOVVisibilityModel.cs`
  - `Assets/Scripts/FOV/FOVVisibilityQuery.cs`

## 구현 포인트
1. `FOVController`에서 "상태 관리"와 "질의 계산"을 분리
2. `_smoothedForward`, `_lastHitDistances`, 각도 범위, range를 한 데이터 모델로 묶기
3. `IsInsideFOV`, `IsColliderInsideFOV`가 이 모델만 참조하도록 정리
4. 이후 렌더 단계도 같은 모델을 읽도록 연결

## 위험
- 현재는 렌더와 질의가 100% 같은 forward를 쓰지 않을 수 있어 체감 차이가 발생할 수 있음
- 이 단계에서 public API 의미가 바뀌면 UI가 먼저 깨질 수 있음

## 검증
### 자동/정적 검증
- 수정 파일 대상 컴파일 에러 0
- `FOVController` public API 시그니처 유지 확인

### 수동 플레이 검증
- 플레이어 정면/측면/후면 기준 `IsInsideFOV` 결과가 화면 체감과 맞는지 확인
- 빠른 회전/조준 전환 시 UI 표시 지연이나 깜빡임이 없는지 확인
- 큰 Collider를 가진 Mob이 "일부만 FOV 안"일 때 UI가 계속 보이는지 확인

## 완료 조건
- 이후 렌더 구조를 바꿔도 `LocalInstance`, `IsInsideFOV`, `IsColliderInsideFOV`의 외부 사용처를 건드리지 않아도 됨

---

## 5. Phase 2 — 오버레이 렌더층 단순화

## 목표
- 바닥 FOV 메시/오버레이만 담당하는 가벼운 렌더층으로 정리합니다.
- 바닥 효과가 캐릭터 가시성 책임을 가지지 않도록 분리합니다.

## 예상 수정 범위
- `Assets/Scripts/Rendering/FOVRendererFeature.cs`
- `Assets/Scripts/Rendering/FOVRenderPass.cs`
- `Assets/Scripts/Rendering/FOVStencilWriterPass.cs`
- `Assets/Scripts/FOV/FOVMeshGenerator.cs`
- `Assets/Shaders/FOVMesh.shader`
- `Assets/Shaders/FOVOverlay.shader`

## 구현 포인트
1. FOV 메시 렌더링과 오버레이 렌더링의 책임을 명확히 분리
2. stencil이 필요해도 **바닥 오버레이 보조 용도**로만 축소
3. MainCamera / SceneView 분기 로직을 유지하되, Game View 판단 기준을 단순화
4. 바닥선이 높은 오브젝트 위로 보이는 구조적 경로를 차단

## 위험
- 오버레이 단순화 중 Scene View 편의 기능이 깨질 수 있음
- overlay material과 FOV mesh material의 파라미터 동기화가 어긋날 수 있음

## 검증
### 수동 플레이 검증
- 기본 이동/조준/회전 중 바닥 FOV 메시가 자연스럽게 갱신되는지 확인
- 시야 밖 오버레이가 유지되는지 확인
- 플레이어/몹 위로 바닥 경계선이 올라오는 현상이 사라졌는지 확인

### Frame Debugger 검증
- 오버레이 관련 패스 수가 명확히 분리되어 있는지 확인
- 오버레이 패스가 Player/Mob/Projectile/Item 재렌더를 유발하지 않는지 확인

## 완료 조건
- 바닥 효과만 켜고 꺼도 게임플레이 오브젝트 가시성 판단은 별개로 유지될 수 있어야 함

---

## 6. Phase 3 — 오브젝트 가시성층 교체

## 목표
- `Player / Mob / Item / Projectile`를 별도 clip pass 재렌더 없이 처리하는 새 가시성 경로를 도입합니다.

## 예상 수정 범위
- `Assets/Scripts/FOV/FOVStencilMaterialRuntimeApplier.cs`
- `Assets/Scripts/Player/PlayerController.cs`
- `Assets/Scripts/Mobs/MobAI.cs`
- `Assets/Scripts/Items/NetworkedItem.cs`
- `Assets/Scripts/Weapons/Projectile.cs`
- `Assets/Shaders/LitFOV.shader`
- `Assets/Shaders/ToonFOVClipped.shader`
- 필요 시 `Assets/Shaders/ParticleFOVClipped.shader`
- 축소/제거 검토 대상: `Assets/Scripts/Rendering/FOVStencilClipPass.cs`

## 구현 포인트
1. 대상 오브젝트를 **주력 경로**와 **fallback 경로**로 분류
2. 주력 경로는 같은 FOV 데이터 모델을 이용해 per-pixel 또는 per-object 가시성 처리
3. fallback은 단순 hide/show 또는 제한적 렌더 제어로 처리
4. `FOVStencilClipPass`는 핵심 경로에서 제거하거나 fallback 전용으로 축소
5. 런타임 material 인스턴스화가 꼭 필요한 경우를 최소화

## 위험
- 셰이더별 구현 차이로 Player/Mob/Item/Projectile가 서로 다른 규칙처럼 보일 수 있음
- 런타임 material 접근이 남아 있으면 모바일 메모리/배치 비용이 다시 커질 수 있음

## 검증
### 기능 검증
- Player / Mob / Item / Projectile가 모두 같은 FOV 규칙으로 보이고 숨겨지는지 확인
- 시야 경계에 걸친 오브젝트가 "통째로 꺼지지" 않는지 확인
- 시야 밖 오브젝트가 배경 위로 잘못 떠 보이지 않는지 확인

### 성능 검증
- Frame Debugger 기준으로 주요 오브젝트 late clip pass 재렌더가 사라졌는지 확인
- 모바일 렌더러에서도 동일한 규칙이 유지되는지 확인

## 완료 조건
- 주요 게임플레이 오브젝트가 기본 렌더 경로 중심으로 동작하고, 기존 체감이 깨지지 않아야 함

---

## 7. Phase 4 — UI/API/플랫폼 정합성 마감

## 목표
- 재구성 결과가 기존 UI/API 의존 지점과 플랫폼 설정까지 모두 맞물리도록 마감합니다.

## 예상 수정 범위
- `Assets/Scripts/UI/PlayerOverheadUI.cs`
- `Assets/Scripts/UI/MobOverheadUI.cs`
- `Assets/Scripts/UI/DamageIndicatorManager.cs`
- `Assets/Settings/PC_Renderer.asset`
- `Assets/Settings/Mobile_Renderer.asset`
- 필요 시 정리 대상 문서와 obsolete 코드

## 구현 포인트
1. UI 세 경로가 새 질의 기준과 여전히 일치하는지 확인
2. PC/Mobile Renderer Asset의 FOV 관련 설정을 동일한 의도로 정리
3. obsolete pass / 불필요 fallback / 임시 디버그 경로 제거
4. 최종 성공 기준을 체크리스트 기준으로 다시 대조

## 검증
### 기능 회귀 검증
- PlayerOverheadUI 표시/숨김
- MobOverheadUI 표시/숨김
- DamageIndicator 생성 조건
- 조준 전환 / 기본 시야 / 장애물 차폐 / 부분 가시성

### 플랫폼 검증
- PC_Renderer / Mobile_Renderer 모두에서 동일 규칙 확인
- Scene View / Game View의 기대 동작 분리 확인

### 정리 검증
- 사용하지 않는 재렌더 경로가 남아 있지 않은지 확인
- 임시 디버그 코드/실험 셰이더 파라미터가 남아 있지 않은지 확인

## 완료 조건
- 체크리스트의 모든 유지 항목을 다시 PASS/FAIL로 채울 수 있어야 함

---

## 8. 권장 검증 순서

매 Phase마다 아래 순서로 검증합니다.

1. **컴파일/정적 검증**
   - 수정 파일 컴파일 에러 확인
2. **플레이 체감 검증**
   - 기본 시야 / 조준 시야 / 장애물 차폐 / 부분 가시성
3. **UI 검증**
   - Overhead UI / Damage Indicator
4. **렌더 검증**
   - Frame Debugger, Scene View / Game View
5. **플랫폼 검증**
   - PC Renderer, Mobile Renderer

---

## 9. Phase 중단 기준

아래 상황이면 다음 Phase로 넘어가지 말고 현재 Phase에서 멈춰야 합니다.

- `IsInsideFOV`와 실제 화면 체감이 불일치함
- Player / Mob / Item / Projectile 중 하나라도 다른 규칙으로 보임
- 바닥선 아티팩트가 재발함
- 별도 재렌더 경로가 다시 주요 경로로 돌아옴
- Mobile Renderer에서 규칙 또는 성능이 크게 달라짐

---

## 10. 최종 메모

- 이 문서의 목적은 "코드를 어떻게 예쁘게 바꿀까"가 아니라 **"어떤 순서로 바꿔야 리스크를 제어할 수 있나"**를 고정하는 데 있습니다.
- 구현 단계에서는 **Phase 1 → Phase 2 → Phase 3 → Phase 4 순서를 뒤집지 않는 것**이 중요합니다.
- 특히 `FOVStencilClipPass` 제거/축소는 반드시 **Phase 3 후반**에 다뤄야 합니다. 그 전에 제거하면 기능 회귀 원인 추적이 어려워집니다.
