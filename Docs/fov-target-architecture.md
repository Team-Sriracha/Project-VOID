# FOV Target Architecture (#17-Mobile 기준 재구성안)

## 1. 설계 목표
- `#16-카드-시스템`의 기능 완성도는 유지한다.
  - 로컬 플레이어만 FOV 계산 수행
  - 무기/조준 상태에 따른 원형/부채꼴/복합 FOV 전환
  - 시야 밖 화면 dim + 시야 안 재노출(stencil reveal) 모델
- `#17-Mobile`에서 드러난 문제를 제거한다.
  - 프레임마다 전역 float array + 여러 셰이더 동기화로 인한 비용 증가
  - 게임플레이 코드에서 재질 스텐실 값을 직접 덮어쓰는 결합
  - 월드 좌표 기반 projected fade 때문에 플레이어 몸체 위로 바닥 경계선이 떠 보이는 버그
  - PC/Mobile 렌더러 분리가 생겼지만 정책/품질 프로필이 구조화돼 있지 않음
- 필수 요구사항 유지:
  - **stencil 오브젝트가 FOV 가장자리에 들어올 때 경계 fade가 서서히 적용되어야 한다.**

## 2. 현재 브랜치 비교 요약
### `#16-카드-시스템`
- 장점
  - `FOVController` → `FOVMeshGenerator` → `FOVRenderPass/FOVOverlay` 흐름이 단순하다.
  - `FOVRendererFeature`가 mask/overlay/clip 역할을 분리하고 있다.
- 한계
  - `ToonFOVClipped` / `LitFOV`의 dim 계산이 반경 기반이라 실제 obstacle 경계와 정확히 맞지 않는다.
  - 모바일 품질/비용 제어 지점이 부족하다.

### `#17-Mobile`
- 개선점
  - `FOVController`가 hit distance 배열과 투영 정보를 셰이더에 전달해 obstacle 기반 경계 dim을 시도한다.
  - `PC_Renderer.asset`, `Mobile_Renderer.asset`가 분리됐다.
- 문제점
  - `FOVController`가 전역 셰이더 계약을 과도하게 소유한다.
  - `FOVStencilMaterialRuntimeApplier`가 `PlayerController`, `MobAI`, `NetworkedItem` 등 게임플레이 진입점에 퍼져 있다.
  - `ToonFOVClipped` / `LitFOV`의 per-pixel projected fade가 tall actor에서 떠 보이는 경계선을 만든다.

## 3. 목표 아키텍처

### 3.1 계층 분리
1. **FOV Domain Layer**
   - 책임: 시야 프로필 결정, obstacle sampling, 경계 snapshot 생성
   - 예시 구성
     - `FOVProfileResolver`: 무기/조준/카드 상태를 받아 목표 range/angle/profile 결정
     - `FOVBoundarySampler`: 레이캐스트와 hit distance 계산 담당
     - `FOVStateSnapshot`: 렌더러에 넘길 읽기 전용 데이터(중심, 방향, 각도, boundary samples, edge widths)

2. **FOV Render Layer**
   - 책임: snapshot을 사용해 mask/overlay/reveal 렌더링
   - 예시 구성
     - `FOVMaskPass`: 메시 또는 폴리곤으로 stencil/depth mask 기록
     - `FOVOverlayPass`: 전체 화면 dim + 바깥 영역/경계 fade 처리
     - `FOVRevealPass`: 선택된 reveal 레이어(플레이어/몹/아이템/데칼 등)를 FOV 안쪽에서 재노출

3. **FOV Integration Layer**
   - 책임: 어떤 오브젝트가 어떤 reveal 정책을 가지는지 선언
   - 예시 구성
     - `FOVRevealAgent`(프리팹 단위): `None | StencilOnly | UniformEdgeFade`
     - gameplay code는 renderer/material 내부 상태를 직접 수정하지 않고 reveal policy만 노출

### 3.2 핵심 데이터 흐름
1. 로컬 플레이어만 `FOVProfileResolver`와 `FOVBoundarySampler`를 업데이트한다.
2. sampler는 **변경 시점 기반**(무기 변경, 조준 상태 변경, 플레이어 회전/이동 임계치 초과, obstacle dirty)으로 snapshot을 갱신한다.
3. snapshot은 렌더러에 한 번 전달되고, 렌더러는 다음 세 단계로 처리한다.
   - **Mask**: FOV 내부를 stencil/depth에 기록
   - **Overlay**: 화면 전체를 dim 처리하되 경계에서는 soft fade 적용
   - **Reveal**: actor/item/decal 등 선택 레이어만 FOV 안에서 다시 보여 준다

## 4. 경계 fade의 목표 구현 방식

### 4.1 월드 전체(dim)는 overlay에서 처리
- 현재처럼 `LitFOV`, `ToonFOVClipped`가 각자 projected world position으로 경계선을 계산하지 않는다.
- 대신 **overlay pass가 FOV boundary distance를 사용해 soft falloff를 계산**한다.
- 효과
  - static geometry와 바닥 경계 fade는 한 곳에서 일관되게 계산된다.
  - tall actor 표면 위로 경계선이 떠 보이는 현상을 줄일 수 있다.

### 4.2 stencil 오브젝트의 edge fade는 “오브젝트 단위”로 처리
- 플레이어/몹/아이템 같은 reveal 대상은 per-pixel projected fade 대신 **anchor 기반 uniform fade**를 기본값으로 둔다.
  - anchor 예: pivot, 발 위치, bounds center + foot offset
- `FOVRevealAgent`가 anchor의 boundary distance를 계산하고 `MaterialPropertyBlock`으로 `_FOVRevealAlpha`만 넘긴다.
- 효과
  - 오브젝트가 경계에 걸릴 때 전체가 자연스럽게 어두워지며,
  - 캐릭터 몸체 중간에 바닥 경계선이 그어지는 문제를 피할 수 있다.

### 4.3 예외 정책
- 데칼/바닥 표시기처럼 실제로 ground-plane 기반 clip이 필요한 오브젝트만 `StencilOnly` 또는 별도 ground policy를 사용한다.
- 즉, **모든 FOV 대응을 모든 셰이더에 내장하지 않는다.**

## 5. 셰이더/머티리얼 정책
- 유지
  - FOV 전용 reveal 셰이더 계열은 유지 가능 (`ToonFOVClipped`, `LitFOV` 계열)
- 변경
  - 셰이더가 obstacle boundary 샘플 전체를 직접 해석하는 구조는 최소화한다.
  - reveal 셰이더는 공통 입력만 받는다.
    - `_FOVRevealAlpha`
    - `_FOVStencilEnabled`
    - 필요 시 공통 include 한 곳의 함수만 사용
- 제거 대상 구조
  - `FOVStencilMaterialRuntimeApplier`를 플레이어/몹/아이템 spawn 경로에서 호출하는 방식
- 대체
  - 프리팹 authoring 또는 centralized registry에서 reveal material/policy를 선언한다.

## 6. PC / Mobile 목표 구조
- **공통 코드 1개 + 품질 프로필 분기**가 목표다.
- `PC_Renderer.asset`, `Mobile_Renderer.asset`는 유지하되, 차이는 기능 온오프보다 **품질 파라미터** 중심이어야 한다.
  - PC
    - 높은 ray count
    - 더 잦은 boundary refresh
    - overlay fade 고품질
    - reveal 대상 레이어 넓게 허용
  - Mobile
    - 낮은 ray count
    - dirty/interval 기반 refresh
    - overlay fade 단순화
    - reveal 대상 최소화
- 즉 renderer asset은 두 개일 수 있어도, FOV 아키텍처는 하나여야 한다.

## 7. 구현 후 기대되는 책임 경계
- `FOVController`
  - 현재처럼 전역 셰이더 프로퍼티를 모두 세팅하는 god object가 아님
  - domain snapshot 생산자 역할로 축소
- Render Feature / Pass
  - 시각화 책임 집중
- Gameplay 스크립트(`PlayerController`, `MobAI`, `NetworkedItem` 등)
  - FOV 머티리얼 내부 구현을 모름
  - reveal policy 등록/해제만 수행

## 8. 수용 기준(아키텍처 기준)
- 로컬 플레이어 외에는 FOV 계산을 하지 않는다.
- 카드/무기/조준 상태에 따른 FOV profile 변화가 유지된다.
- 시야 밖 dim, 시야 안 reveal, edge fade가 동일한 snapshot 기준으로 동작한다.
- tall actor에 바닥 경계선이 떠 보이지 않는다.
- 모바일에서는 경계 샘플/셰이더 동기화 비용이 현재보다 줄어든다.
- gameplay 코드가 shader global/material mutation에 직접 의존하지 않는다.

## 9. 권장 마이그레이션 순서
1. `FOVStateSnapshot`와 `FOVProfileResolver`를 먼저 분리
2. overlay fade 계산을 render layer로 이동
3. reveal 대상에 `FOVRevealAgent`/policy 도입
4. `FOVStencilMaterialRuntimeApplier` 제거
5. 마지막에 PC/Mobile 품질 프로필만 조정

## 10. 결론
가장 중요한 방향은 **“경계 계산의 단일화” + “render 책임의 집중” + “gameplay 코드에서 FOV 머티리얼 제어 제거”**이다. `#17-Mobile`의 obstacle-aware boundary 아이디어는 유지하되, 그 계산 결과를 모든 셰이더와 스폰 코드에 흩뿌리는 대신 snapshot/overlay/reveal 구조로 재배치하는 것이 목표 아키텍처로 적합하다.
