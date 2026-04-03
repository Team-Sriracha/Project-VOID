# FOV 리팩터링에서 버릴 구조 정리

## 목적
현재 FOV/Stencil 렌더링 구현에서 **동작 자체는 유지하되 구조적으로 폐기해야 할 부분**을 식별한다. 기준은 다음 3가지다.

1. 렌더링 책임이 한 곳에 과도하게 몰려 있는가
2. 런타임 결합이 강해서 다른 시스템까지 전파되는가
3. 같은 가시성 규칙이 여러 위치에 중복 구현되어 있는가

## 우선 폐기 대상

### 1. `FOVController`의 단일 거대 책임 구조
- 파일: `Assets/Scripts/FOV/FOVController.cs`
- 현재 한 클래스가 아래를 모두 담당한다.
  - 무기/조준 상태에 따른 FOV 형태 결정
  - 전환 애니메이션
  - 레이캐스트 결과 캐시
  - 메시 생성 호출
  - 전역 셰이더 프로퍼티 갱신
  - 로컬 싱글톤 제공 (`LocalInstance`)
  - 에디터/런타임 stencil 전역 상태 초기화
- 문제점
  - 게임플레이 규칙과 렌더링 규칙이 강하게 결합되어 교체 단위가 너무 크다.
  - `Awake`, `OnEnable`, `OnStartClient`, `OnDestroy`에 초기화가 분산돼 있어 수명주기 추적이 어렵다.
  - 이후 아키텍처 변경 시 이 파일 하나를 계속 수정하게 되는 중심 병목이 된다.
- 폐기 원칙
  - **현재의 단일 컨트롤러 구조는 버린다.**
  - 단, 외부에서 필요한 공개 행위(현재 시야 질의 등)는 더 작은 서비스/어댑터로 분리해 유지한다.

### 2. `FOVMeshGenerator -> FOVRenderPass` 정적 전역 전달 구조
- 파일
  - `Assets/Scripts/FOV/FOVMeshGenerator.cs`
  - `Assets/Scripts/Rendering/FOVRenderPass.cs`
- 현재 구조
  - `FOVMeshGenerator.UpdateMesh()`가 `FOVRenderPass.SetFOVMeshData()`로 정적 상태를 밀어 넣는다.
  - `FOVRenderPass`는 `static Mesh`, `static Matrix4x4`, `static bool`로 전역 렌더 상태를 보관한다.
- 문제점
  - 씬에 FOV 소유자가 하나라는 가정에 묶인다.
  - 렌더 패스가 데이터 소유권을 갖지 않고 외부 정적 상태를 읽어 예측이 어렵다.
  - 테스트/디버깅 시 프레임 간 잔존 상태를 의심해야 한다.
- 폐기 원칙
  - **정적 전역 메시 전달 구조는 버린다.**
  - 메시/마스크 데이터는 명시적 owner 또는 렌더용 provider를 통해 주입되는 형태로 바꿔야 한다.

### 3. 런타임 material 인스턴스 변조 구조
- 파일
  - `Assets/Scripts/FOV/FOVStencilMaterialRuntimeApplier.cs`
  - 호출부: `Assets/Scripts/Items/NetworkedItem.cs`, `Assets/Scripts/Mobs/MobAI.cs`, `Assets/Scripts/Player/PlayerController.cs`, `Assets/Scripts/Weapons/Projectile.cs`
- 현재 구조
  - 각 네트워크 오브젝트의 `OnStartClient()`에서 hierarchy 전체 렌더러를 순회한다.
  - `_GlobalStencilComp` 프로퍼티가 있으면 material 인스턴스를 생성하고 값을 바꾼다.
- 문제점
  - FOV 렌더링 요구사항이 플레이어, 몹, 아이템, 투사체 생성 경로까지 침투한다.
  - `renderer.materials` 접근은 런타임 material 복제를 일으켜 비용과 추적 부담이 생긴다.
  - 어떤 오브젝트가 FOV clip 대상인지가 prefab/renderer 설정이 아니라 코드 호출 여부에 달려 있다.
- 폐기 원칙
  - **hierarchy 순회 + material 변조 방식은 버린다.**
  - clip 대상 선언은 레이어/렌더러 feature/명시적 태그 수준으로 이동해야 한다.

### 4. 셰이더별 FOV 대응 포크 구조
- 파일
  - `Assets/Shaders/LitFOV.shader`
  - `Assets/Shaders/ToonFOVClipped.shader`
  - `Assets/Shaders/ParticleFOVClipped.shader`
  - `Assets/Shaders/FOVClippedDecal.shader`
- 현재 구조
  - 동일한 가시성 규칙이 여러 셰이더 변형에 분산되어 있다.
  - `_GlobalStencilComp`, `_FOVCenter`, `_FOVHitDistances` 등 FOV 전역값에 직접 의존한다.
- 문제점
  - 셰이더가 늘어날수록 같은 수정이 여러 파일로 복제된다.
  - “무엇이 보이는가” 규칙이 머티리얼 종류별로 흩어져 유지보수 난도가 급상승한다.
- 폐기 원칙
  - **FOV 전용 셰이더 포크를 기본 전략으로 삼는 구조는 버린다.**
  - 가능하면 공통 마스크/공통 clip 경로를 두고, 셰이더별 특수 처리는 최소화해야 한다.

### 5. Legacy Execute + 부분 RenderGraph 혼합 구조
- 파일
  - `Assets/Scripts/Rendering/FOVRendererFeature.cs`
  - `Assets/Scripts/Rendering/FOVRenderPass.cs`
  - `Assets/Scripts/Rendering/FOVStencilWriterPass.cs`
  - `Assets/Scripts/Rendering/FOVStencilClipPass.cs`
- 현재 구조
  - 어떤 패스는 `RecordRenderGraph`, 어떤 패스는 legacy `Execute`만 사용한다.
  - Scene View/Game View 분기와 stencil bypass 정책이 패스별로 흩어져 있다.
- 문제점
  - 렌더 경로가 단일 모델로 정리돼 있지 않아 이후 수정 시 회귀 위험이 크다.
  - API 호환성 대응 흔적이 구조로 굳어져 있다.
- 폐기 원칙
  - **혼합 렌더 경로 구조는 버린다.**
  - 이후 목표 아키텍처는 한 가지 렌더링 경로로 통일하는 것이 안전하다.

## 버리면 안 되는 것
아래는 “현재 구현은 손봐도 되지만 기능 요구 자체는 유지”해야 하는 항목이다.

- 장애물 기반 가시성 계산이라는 핵심 규칙 (`FOVCalculator`가 담당하는 역할)
- UI가 사용하는 단순 FOV 질의 기능 (`IsInsideFOV`, `IsColliderInsideFOV` 수준의 외부 계약)
- 무기/조준 상태에 따라 시야 모양이 바뀌는 게임플레이 요구
- Scene View에서 디버깅 가능한 최소한의 시각화 요구

## 결론
폐기 우선순위는 다음 순서가 적절하다.

1. `FOVStencilMaterialRuntimeApplier`와 셰이더 포크 전략 제거
2. `FOVMeshGenerator -> FOVRenderPass` 정적 상태 전달 제거
3. `FOVController`의 거대 책임 분해
4. 렌더 패스 구현을 단일 경로로 통일

즉, 이번 리팩터링의 핵심은 **기능을 버리는 것이 아니라, 현재의 결합 방식과 전역 상태 중심 구조를 버리는 것**이다.
