# 프로덕션 레벨 성능 정적 분석 보고서

작성일: 2026-04-07
분석 방식: OMX 팀 기반 3인 병렬 정적 분석
범위:
- Game Logic / Network / Fish-Net / CSP / AI / Physics
- Graphics / Rendering / FOV / URP / Shader / Material
- Asset 규모 / Quality Settings / Build Settings / 메모리 / 플랫폼 최적화 리스크

## 1. 요약

현재 프로젝트의 프로덕션 레벨 성능 리스크는 크게 세 축으로 정리됩니다.

1. **실시간 전투/AI/FOV 루프가 매 프레임 또는 매 틱마다 CPU 비용과 메모리 churn을 동시에 키우는 구조**
2. **렌더링 경로에서 material 인스턴스 복제, 다중 패스, 전역 셰이더 업로드가 누적되는 구조**
3. **대형 폰트/외부 에셋/플러그인/품질 프리셋 드리프트로 인해 모바일 메모리·빌드 용량·재현성이 흔들리는 구조**

가장 우선적으로 손봐야 할 P0는 다음입니다.

- 전투 판정/이펙트 경로의 `OverlapSphere` 할당 + RPC/VFX fan-out
- `MobAI`의 `SetDestination`/`ResetPath` churn
- `ItemPickupDetector`의 `Physics.SyncTransforms()` 기반 stall
- `FOVController`의 owner별 매 프레임 전체 재계산
- `PlayerOcclusionFader`의 `renderer.materials`/`new Material` churn
- 초대형 TMP SDF 폰트, 공유 품질 프리셋 드리프트, 메모리 정책 기본값 방치

보정 메모:
- 네트워크 오브젝트 풀링은 **FishNet native pooling 인프라가 이미 존재**합니다.
- 따라서 풀링 이슈는 “커스텀 풀을 새로 만든다”가 아니라 **반복 스폰 hot path가 FishNet의 retrieval 경로로 정렬돼 있는지**를 검증하는 문제로 다시 정의해야 합니다.
- 반대로 총구 화염, 근접 스윙, 근접 히트, 스킬 VFX, 투사체 히트 이펙트처럼 `NetworkObject`가 아닌 일반 `GameObject` VFX는 **FishNet 풀링 대상이 아니므로 별도 로컬 VFX 재사용 구조**로 봐야 합니다.

---

## 2. P0 리스크

### P0-1. 근접/스킬 판정의 물리 할당 + RPC/VFX fan-out
- 근거 파일:
  - `Assets/Scripts/Weapons/NetworkedWeapon.cs:736-792`
  - `Assets/Scripts/Weapons/NetworkedWeapon.cs:1002-1046`
  - `Assets/Scripts/Weapons/NetworkedWeapon.cs:1196-1254`
- 문제:
  - 근접/스킬 판정이 `Physics.OverlapSphere()`를 직접 호출해 배열을 매 공격마다 할당합니다.
  - 히트마다 `GetComponent<IDamageable>()`를 다시 조회합니다.
  - 타격/스윙/스킬 이펙트를 각각 `ObserversRpc`로 전파하고 `Instantiate/Destroy` 합니다.
- 영향:
  - 다수 타깃 전투에서 **GC + 물리 비용 + 네트워크 트래픽 + 클라이언트 VFX 생성 비용**이 동시에 증가합니다.
- 권장 조치:
  - `OverlapSphereNonAlloc` + 재사용 버퍼
  - `IDamageable`/`Projectile` 캐시
  - 반복 스폰되는 발사체는 `Instantiate + Spawn` 관성을 유지하기보다 **FishNet native pooling retrieval 경로(`GetPooledInstantiated`) 정렬 여부**를 우선 검증
  - 반복 스폰 prefab의 `NetworkObject` 기본 `DespawnType`이 `Destroy`로 남아 있지 않은지 확인
  - 총구 화염/근접 스윙/근접 히트/스킬 VFX/투사체 히트 이펙트는 **FishNet과 분리된 로컬 VFX 재사용 구조**로 관리
- 검증 방법:
  - 10/30/50 타깃 근접 전투에서 `GC Alloc/frame`, `Physics.Processing`, RPC count, bytes/sec 비교

### P0-2. Mob AI tick/NavMesh churn
- 근거 파일:
  - `Assets/Scripts/Mobs/MobAI.cs:132-156`
  - `Assets/Scripts/Mobs/MobAI.cs:220-258`
  - `Assets/Scripts/Mobs/MobAI.cs:411-445`
  - `Assets/Scripts/Mobs/MobAI.cs:468-523`
  - `Assets/Scripts/Mobs/MobAI.cs:548-623`
- 문제:
  - 서버 Tick마다 상태를 처리하고, 추격 중 `SetDestination()`을 반복 호출합니다.
  - 상태 전이 전반에서 `ResetPath()`가 자주 호출됩니다.
  - 감지 루프에서 후보별 `GetComponentInParent<PlayerCombat>()`, `GetComponent<NetworkObject>()`, 거리 계산이 반복됩니다.
- 영향:
  - 몹 수가 늘수록 **AI self time + NavMesh 재계산 + 감지 루프 비용**이 누적됩니다.
- 권장 조치:
  - 목적지 변화 임계치 이하에서는 `SetDestination()` 생략
  - `ResetPath()`를 상태 전이 시점으로 제한
  - 컴포넌트/타깃 캐시 강화
  - 거리 계산은 `sqrMagnitude` 우선
- 검증 방법:
  - 몹 10/30/50/100에서 AI ms, path recalculation count, 서버 프레임 spike 측정

### P0-3. 아이템 감지 경로의 전역 Transform 동기화 stall
- 근거 파일:
  - `Assets/Scripts/Player/ItemPickupDetector.cs:79-147`
- 문제:
  - 감지 주기마다 `Physics.SyncTransforms()`를 호출한 뒤 `Physics.OverlapSphere()`를 수행합니다.
  - `_previousItems.Contains()` 기반 비교로 목록 diff를 계산합니다.
- 영향:
  - 아이템 밀집 구역에서 **전역 물리 동기화 + 배열 할당 + 목록 비교 비용**이 메인스레드를 멈추게 할 수 있습니다.
- 권장 조치:
  - `SyncTransforms()` 제거 또는 텔레포트 직후 조건부 호출
  - `OverlapSphereNonAlloc` 적용
  - `HashSet` 기반 diff로 변경
- 검증 방법:
  - 아이템 20/50/100개 밀집 상황에서 SyncTransforms cost, 감지당 GC, UI refresh 횟수 비교

### P0-4. FOV 전체 재계산이 owner마다 매 프레임 발생
- 근거 파일:
  - `Assets/Scripts/FOV/FOVController.cs:167-176`
  - `Assets/Scripts/FOV/FOVController.cs:343-377`
  - `Assets/Scripts/FOV/FOVCalculator.cs:56-63`
  - `Assets/Scripts/FOV/FOVCalculator.cs:78-109`
- 문제:
  - Owner 플레이어는 `LateUpdate()`마다 FOV shape 결정과 메시 재계산을 수행합니다.
  - 다중 Raycast + 메시 갱신 + 셰이더 전역 업로드가 한 프레임에 묶여 있습니다.
- 영향:
  - 특히 모바일에서 **Physics.Raycast 비용 + 렌더 데이터 갱신 비용**이 누적됩니다.
- 권장 조치:
  - 이동/회전/조준/무기 변경 기반 dirty-update 구조로 전환
  - 모바일 전용 rayCount/갱신 주기 하향
  - hit distance/mesh 버퍼 재사용
- 검증 방법:
  - Unity Profiler Timeline, 모바일 실기기 프레임타임, Frame Debugger 비교

### P0-5. PlayerOcclusionFader의 material churn
- 근거 파일:
  - `Assets/Scripts/Rendering/PlayerOcclusionFader.cs:166-168`
  - `Assets/Scripts/Rendering/PlayerOcclusionFader.cs:238-252`
  - `Assets/Scripts/Rendering/PlayerOcclusionFader.cs:535-580`
- 문제:
  - 청크/벽 캐시 과정에서 hierarchy 순회를 크게 수행합니다.
  - fade 시점에 `renderer.materials` 접근과 `new Material(original)`이 반복됩니다.
- 영향:
  - **GC/메모리 churn, SRP Batcher 붕괴, draw-call 증가**가 발생할 수 있습니다.
- 권장 조치:
  - `renderer.materials` 접근 제거
  - `sharedMaterials + MaterialPropertyBlock` 또는 디더 fade 전환
  - wall group registry 사전 계산
- 검증 방법:
  - Memory Profiler(material count), GC Alloc, SRP Batcher 통계, 벽 밀집 환경 stress test

### P0-6. 초대형 TMP SDF 폰트 / 품질 프리셋 드리프트 / 메모리 정책 기본값
- 근거 파일:
  - `Assets/Content/Fonts/ONE Mobile POP SDF.asset`
  - `Assets/Content/Fonts/ONE Mobile POP SDF 1.asset`
  - `Assets/Content/Fonts/Paperlogy-5Medium SDF.asset`
  - `Assets/Content/Fonts/Paperlogy-6SemiBold SDF.asset`
  - `Assets/Content/Fonts/Paperlogy-6SemiBold-shadow SDF.asset`
  - `ProjectSettings/QualitySettings.asset`
  - `ProjectSettings/QualitySettings.asset.private.0`
  - `ProjectSettings/MemorySettings.asset`
  - `ProjectSettings/ProjectSettings.asset`
- 문제:
  - TMP SDF 5개가 각각 약 132~134MB 수준입니다.
  - 공유 품질 프리셋은 2단, private 설정은 6단으로 드리프트가 있습니다.
  - 메모리 정책은 플랫폼별 설정 없이 기본값 위주입니다.
- 영향:
  - **모바일 메모리 예산 초과**, 품질 재현성 저하, GC spike 가능성이 큽니다.
- 권장 조치:
  - 폰트 아틀라스 1024~2048 기준 재생성 및 중복 제거
  - 공유 품질 프리셋 3단(예: `PC_High`, `Mobile_Mid`, `Mobile_Low`) 재정의
  - 플랫폼별 메모리 버짓/allocator 명문화
- 검증 방법:
  - Build Report, Memory Profiler, 플랫폼별 clean build 재현 테스트

---

## 3. P1 리스크

### P1-1. CSP 입력 수집 hot path 비용
- 근거 파일:
  - `Assets/Scripts/Player/PlayerController.cs:228-242`
  - `Assets/Scripts/Player/PlayerController.cs:291-337`
  - `Assets/Scripts/Player/PlayerController.cs:347-389`
- 문제:
  - 입력 수집 중 `GetComponent<PlayerCombat>()`, `GetComponent<PlayerInputHandler>()`가 반복 호출됩니다.
  - 매 Tick replicate/reconcile가 항상 수행됩니다.
- 권장 조치:
  - 컴포넌트 캐시
  - idle 상태 replicate/reconcile 빈도 축소
- 검증 방법:
  - 8/16/32 플레이어 환경에서 bytes/player, prediction CPU 측정

### P1-2. SyncVar fan-out
- 근거 파일:
  - `Assets/Scripts/Player/PlayerAnimationController.cs:38-49,106-110`
  - `Assets/Scripts/Mobs/MobAnimationController.cs:31-38,91-107`
  - `Assets/Scripts/Weapons/NetworkedWeapon.cs:66-86`
- 문제:
  - 애니메이션/무기 상태가 다수 SyncVar로 세분화되어 있습니다.
- 영향:
  - dirty check/직렬화/late join payload가 커질 수 있습니다.
- 권장 조치:
  - transient 상태 묶음화
  - threshold 기반 업데이트
- 검증 방법:
  - dirty count, bytes/sec, late join snapshot 비교

### P1-3. 맵 초기화 / late join / NavMesh spike
- 근거 파일:
  - `Assets/Scripts/Network/NetworkMapManager.cs:156-172,434-458,652-705`
  - `Assets/Scripts/Map/NavMeshBaker.cs:69-204`
- 문제:
  - SyncList 복제와 NavMesh 빌드가 초기화 타이밍에 몰립니다.
  - `NavMesh.CalculateTriangulation()`도 추가 비용을 발생시킵니다.
- 권장 조치:
  - 맵 동기화 payload 단순화
  - NavMesh 결과 캐시 및 불필요 계산 제거
- 검증 방법:
  - 맵 생성/late join spike, bake ms, spawn latency 측정

### 보정 메모. FishNet native pooling 재판정
- 근거 파일:
  - `Assets/FishNet/Runtime/Managing/NetworkManager.cs:324-325`
  - `Assets/FishNet/Runtime/Managing/NetworkManager.ObjectPooling.cs:12-32`
  - `Assets/FishNet/Runtime/Object/NetworkObject/NetworkObject.cs:311-318`
  - `Assets/FishNet/Runtime/Utility/Performance/DefaultObjectPool.cs:58-96`
  - `Assets/FishNet/Runtime/Utility/Performance/DefaultObjectPool.cs:152-178`
  - `Assets/Scripts/Weapons/NetworkedWeapon.cs:699-706`
  - `Assets/Scripts/Mobs/MobSpawnManager.cs:277-294`
  - `Assets/Scripts/Items/LootBox.cs:288-313`
  - `Assets/Scripts/Network/NetworkMapManager.cs:233-294`
  - `Assets/Scripts/Weapons/NetworkedWeapon.cs:1013-1046`
  - `Assets/Scripts/Weapons/NetworkedWeapon.cs:1237-1254`
  - `Assets/Scripts/Weapons/Projectile.cs:208-213`
- 정정 판단:
  - FishNet 런타임은 기본적으로 `DefaultObjectPool`을 보유하며, `GetPooledInstantiated(...)` 계열 API도 이미 제공합니다.
  - 다만 현재 프로젝트의 반복 스폰 hot path는 정적으로 볼 때 대부분 `Instantiate(...)` 후 `ServerManager.Spawn(...)` 패턴을 사용하고 있어, **실제 retrieval 경로가 FishNet 풀로 정렬돼 있다고 단정하기 어렵습니다.**
  - 또한 FishNet에서 despawn 시 풀로 되돌아가려면 해당 `NetworkObject`의 기본 `DespawnType` 설정도 함께 맞아 있어야 합니다.
  - 반면 총구 화염/근접 히트/스킬 VFX/투사체 히트 이펙트는 `NetworkObject`가 아닌 일반 `GameObject`라서 **FishNet 풀링 범위 밖**입니다.
- 수정된 권장 조치:
  - 커스텀 풀 추가보다 **FishNet native pooling 사용 경로 정렬 여부 검증**을 우선
  - 발사체/몹/드랍 아이템/맵 매니저처럼 반복 스폰되는 네트워크 프리팹은 `GetPooledInstantiated` 적용 가능성을 확인
  - 각 프리팹의 `NetworkObject` 기본 `DespawnType`을 점검
  - 비네트워크 VFX만 별도 로컬 재사용 풀 대상으로 분리
- 검증 방법:
  - 동일 프리팹에 대해 `Instantiate + Spawn` 경로와 FishNet pooled retrieval 경로를 비교해 spawn ms, GC Alloc, active object churn 측정
  - despawn 후 실제 pool return 여부를 object count와 재활용 횟수로 확인

### P1-4. RevealAgent/RuntimeApplier의 spawn 경로 침투
- 근거 파일:
  - `Assets/Scripts/FOV/FOVRevealAgent.cs:122-145`
  - `Assets/Scripts/FOV/FOVRevealAgent.cs:141-164`
  - `Assets/Scripts/FOV/FOVStencilMaterialRuntimeApplier.cs:25-65`
  - `Assets/Scripts/FOV/FOVStencilMaterialRuntimeApplier.cs:73-93`
- 문제:
  - reveal 정책 적용 시 hierarchy 전체 renderer/material 수정이 발생합니다.
- 영향:
  - spawn spike와 material duplication 리스크가 있습니다.
- 권장 조치:
  - prefab authoring/registry/layer 기반 reveal 선언으로 이동
- 검증 방법:
  - 몹/아이템/투사체 대량 spawn 시 material instance 수 및 spawn frame spike 비교

### P1-5. FOVRendererFeature의 추가 패스 누적
- 근거 파일:
  - `Assets/Scripts/Rendering/FOVRendererFeature.cs:40-70`
  - `Assets/Scripts/Rendering/FOVRendererFeature.cs:97-125`
  - `Assets/Settings/PC_Renderer.asset`
  - `Assets/Settings/Mobile_Renderer.asset`
- 문제:
  - MainCamera당 stencil writer / FOV mesh / overlay stencil / overlay 등 추가 패스가 상시 삽입됩니다.
- 영향:
  - GPU overdraw와 draw submission이 증가합니다.
- 권장 조치:
  - FOV inactive early-out, 패스 통합, mobile 단순 경로 분리
- 검증 방법:
  - Frame Debugger, RenderDoc, GPU frame time 비교

### P1-6. 외부 샘플 자산 과다 적재
- 근거 파일:
  - `Assets/External Assets/...`
- 정량 근거:
  - `Assets/External Assets`: **1.9GB**
  - 15MB급 8K VFX 텍스처, 10MB급 스카이박스 텍스처 다수 존재
- 권장 조치:
  - 데모/샘플 자산 분리, 플랫폼별 Max Size/압축 프리셋 강제
- 검증 방법:
  - Library 재생성 시간, Build Report 포함 자산 diff, 임포트 규칙 검사

### P1-7. Firebase/플러그인 비대 및 빌드 재현성 불명확
- 근거 파일:
  - `Assets/Firebase/...`
  - `Assets/Plugins/...`
  - `Assets/Scripts/Editor/ProjectBuildAutomation.cs`
  - `ProjectSettings/EditorBuildSettings.asset`
- 정량 근거:
  - `Assets/Firebase`: **390MB**
  - `Assets/Plugins`: **174MB**
- 권장 조치:
  - 플랫폼별 실제 출시 대상 플러그인만 남기고 분리
  - Build Profile/빌드 입력값 명문화
- 검증 방법:
  - 플랫폼별 build 산출물 native library 목록, clean build 재현 비교

### P1-8. 전달 전략(Addressables/원격 전달) 부재
- 근거 파일:
  - `Packages/manifest.json`
  - `ProjectSettings/EditorBuildSettings.asset`
- 문제:
  - 대형 에셋 규모 대비 명시적 전달/스트리밍 전략 근거가 부족합니다.
- 권장 조치:
  - 선택 콘텐츠를 Addressables/원격 전달 대상으로 분리
- 검증 방법:
  - 베이스 빌드 vs 원격 번들 용량/로딩 메모리 비교

---

## 4. P2 리스크

### P2-1. 서버 세션 상태 async 갱신 중첩 가능성
- 근거 파일:
  - `Assets/Scripts/Core/GameStateManager.cs:260-340`
- 문제:
  - `async void` 기반 주기 호출은 백엔드 지연 시 중첩 가능성이 있습니다.
- 권장 조치:
  - in-flight guard, cancellation, backoff 도입
- 검증 방법:
  - 느린 백엔드 환경에서 동시 요청 수와 중첩률 측정

### P2-2. 셰이더 포크 / variant 관리 리스크
- 근거 파일:
  - `Docs/FOV/fov-discard-structure.md:60-74`
  - `Docs/fov-target-architecture.md:67-98`
  - `ProjectSettings/GraphicsSettings.asset:29-38`
- 문제:
  - 문서 기준으로 FOV 대응 셰이더 포크/variant 관리 복잡도가 높습니다.
- 권장 조치:
  - 공통 include 함수화, Always Included Shaders 최소화, variant 로그 기반 정리
- 검증 방법:
  - Build Report + Shader Variant 로그 + warmup hitch 측정

---

## 5. 자산/설정 정량 근거

### 상위 자산 볼륨
- `Assets/External Assets`: **1.9GB**
- `Assets/Content`: **767MB**
- `Assets/Firebase`: **390MB**
- `Assets/Plugins`: **174MB**
- `Assets/Prefab`: **31MB**
- `Assets/TextMesh Pro`: **14MB**

### 파일 수 개요
- `.meta`: 10878개
- `prefab`: 2696개
- `png`: 1917개
- `fbx`: 1764개
- `mat`: 953개
- `wav`: 486개

### 텍스처 설정 참고
- `maxTextureSize: 4096` 메타 항목: **103개**
- `maxTextureSize: 2048` 메타 항목: **8407개**

### 대형 텍스처 예시
- `UniqueProjectilesVol2_10X10_8K.png`: 15.9MB
- 다수 대형 스카이박스 텍스처: 8~10MB대

---

## 6. 우선 처리 순서 제안

1. `NetworkedWeapon` / `MobAI` / `ItemPickupDetector`의 실시간 루프 비용 제거
2. 반복 스폰 `NetworkObject` 경로의 **FishNet native pooling 정렬 여부** 검증
3. `FOVController` dirty-update화 + `PlayerOcclusionFader` material churn 제거
4. TMP SDF 폰트 축소 + 품질 프리셋 공유 기준 정리 + 메모리 정책 명문화
5. 외부 샘플 자산 / Firebase / 플랫폼 플러그인 정리
6. Build Profile/전달 전략 재현성 확보
7. SyncVar fan-out, late join/NavMesh spike, 셰이더 variant 리스크 정리

---

## 7. 검증 권장 체크리스트

- Unity Profiler Timeline: CPU, Physics, Rendering, GC Alloc
- Memory Profiler: material instance 수, 폰트 메모리, 씬 왕복 메모리 plateau
- Frame Debugger / RenderDoc: FOV 패스 수, overdraw, draw-call
- Fish-Net 통계: RPC count, bytes/sec, dirty count, late join snapshot
- Build Report: 폰트/플러그인/외부 자산 포함 용량
- 모바일 실기기: 프레임타임, thermal, 메모리 headroom

---

## 8. 결론

이 프로젝트는 이미 **기능 구현은 상당히 진행**되어 있지만, 프로덕션 수준 성능 관점에서는 아직 다음과 같은 구조적 위험이 남아 있습니다.

- 전투/AI/FOV가 **실시간 루프 비용을 직접 키우는 구조**
- 렌더링 경로가 **material churn과 추가 패스를 누적시키는 구조**
- 에셋/폰트/플러그인/품질설정이 **모바일 메모리와 빌드 재현성을 흔드는 구조**
- 풀링 이슈가 **“커스텀 풀 부재”가 아니라 “FishNet native pooling 경로 정렬 + 로컬 VFX 재사용 분리” 문제**로 남아 있는 구조

따라서 다음 단계는 “무작정 전역 최적화”가 아니라,
**P0 병목부터 계측 기반으로 한 축씩 제거하는 프로덕션 최적화 패스**로 진행하는 것이 가장 안전합니다.
