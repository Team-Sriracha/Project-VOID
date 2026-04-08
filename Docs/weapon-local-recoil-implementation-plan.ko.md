# 무기 발사 반동 로컬 오프셋 전환 계획

작성일: 2026-04-06  
성격: 구현 전 계획 문서  
범위: **무기 발사 시 무기 Animator의 `Fire` 재생을 로컬 오프셋 반동으로 대체**  
비범위: 플레이어 전신/상체 애니메이션 구조 변경, 재장전 애니메이션 제거, 근접 무기 타격 타이밍 재설계

## 1. 목적

현재 발사 반동은 `NetworkedWeapon.OnFireCounterChanged()`에서 무기 프리팹 내부 `Animator`를 찾아 `Play("Fire")`로 재생한다.

이번 변경의 목적은 다음과 같다.

1. 총기 발사 반동을 **애니메이션 클립 의존**에서 **코드 기반 로컬 오프셋**으로 전환한다.
2. 연사/반자동/버스트에서도 같은 반동 로직을 재사용 가능하게 만든다.
3. 무기별 반동 강도/복귀 속도를 데이터로 조절 가능하게 만든다.

## 2. 현재 구조 분석

### 확인한 사실
- `Assets/Scripts/Weapons/NetworkedWeapon.cs`
  - `OnFireCounterChanged()`에서 `_currentEquippedItem.GetComponentInChildren<Animator>()` 후 `weaponAnimator.Play("Fire")` 호출
  - `OnWeaponAttached()`에서 `item.transform.Find("FirePoint")`로 총구 위치를 찾음
- `Assets/Scripts/Items/NetworkedItem.cs`
  - 장착 상태에서 `UpdateAttachment()`가 매 프레임 `transform.localPosition = Vector3.zero`, `transform.localRotation = Quaternion.identity`를 강제로 맞춤
- `Assets/Scripts/Player/PlayerAnimationController.cs`
  - 무기별 애니메이션 오버라이드는 **플레이어 상체/재장전 쪽**에 사용되고 있음
- 총기 프리팹 예시
  - `Assets/Prefab/Weapon/Gun/Pistol1/Pistol.prefab`
  - `Assets/Prefab/Weapon/Gun/Rifle1/Rifle.prefab`
  - 모두 `FirePoint`와 별도 시각 메쉬/Animator 구조를 가짐

### 핵심 제약
- `NetworkedItem.UpdateAttachment()`는 장착 상태에서 무기 루트의 `localPosition/localRotation`을 매 프레임 0으로 되돌린다.
- 따라서 반동 타겟은 장착 루트 전체보다 **실제 보이는 총기 메쉬 child**를 우선 사용하는 편이 안전하다.
- 1차 구현은 `LateUpdate()`에서 recoil target의 로컬 위치/회전을 다시 적용하는 방식으로 간다.

## 3. 변경 방향

### 옵션 A. 장착 무기 루트를 `LateUpdate()`에서 반동시키기
- 장점: 프리팹 구조 수정 없이 즉시 적용 가능
- 단점: `UpdateAttachment()`와 직접 충돌하고 `FirePoint`까지 함께 흔들린다
- 결론: **채택하지 않음**

### 옵션 B. 무기 프리팹 내부 child를 recoil pivot으로 사용
- 장점: 실제 보이는 총기 모델만 움직일 수 있다
- 장점: `FirePoint`는 고정하고 시각 메쉬만 반동시킬 수 있다
- 단점: 프리팹 구조 탐색 규칙이 필요하다
- 결론: **1차 구현안으로 채택**

## 4. 구현 계획

### 4.1 데이터 확장
대상 파일:
- `Assets/Scripts/Data/GunData.cs`

추가 예정:
- 반동 뒤로 밀림 거리
- 반동 회전 각도
- 반동 복귀 속도
- 발사 시 즉시 적용 강도

목적:
- 총기별 손맛 차이를 코드가 아닌 데이터로 조절

### 4.2 반동 컴포넌트 추가
대상 파일:
- 신규 파일 예시: `Assets/Scripts/Weapons/WeaponRecoilOffset.cs`

책임:
- 장착 무기 recoil target의 기준 local position/local rotation 캐시
- 발사 시 impulse 누적
- `LateUpdate()`에서 부드럽게 복귀

예상 공개 API:
- `Bind(Transform target, GunData gunData)`
- `PlayFireRecoil()`
- `ResetAndClear()`

### 4.3 `NetworkedWeapon` 연동 변경
대상 파일:
- `Assets/Scripts/Weapons/NetworkedWeapon.cs`

변경 예정:
- `OnFireCounterChanged()`에서 더 이상 `Animator.Play("Fire")`를 직접 호출하지 않음
- 대신 `WeaponRecoilOffset.PlayFireRecoil()` 호출
- `OnWeaponAttached()` 시 현재 장착 무기 시각 child와 반동 데이터 연결
- `OnWeaponDetached()` 시 recoil 상태 초기화

### 4.4 1차 적용 규칙
1. 총기 반동은 현재 장착된 `NetworkedItem` 아래에서 **첫 번째 유효 Renderer child**를 recoil target으로 잡는다.
2. 이름이 `FirePoint`인 transform은 recoil target에서 제외한다.
3. 반동 적용은 `LateUpdate()`에서 수행한다.
4. 총기 데이터가 없거나 장착 무기가 없으면 즉시 원위치로 복귀하고 바인딩을 해제한다.

### 4.5 유지할 것
- 플레이어 상체/재장전 Animator 경로 유지
- `FirePoint` 기반 발사체 생성 유지
- 머즐 플래시/사운드/탄약/판정 로직 유지

## 5. 위험 요소

### 위험 1. 프리팹 구조 불일치
- 일부 무기는 첫 Renderer child가 기대한 시각 루트와 다를 수 있음
- 대응:
  - 대표 총기(권총/소총/샷건)로 먼저 체감 확인
  - 필요 시 `RecoilPivot` 명시 child 규칙으로 확장
  - 최소 2~3개 대표 총기로 확인

### 위험 2. 반동과 `FirePoint` 위치 불일치
- 시각 메쉬만 움직이고 `FirePoint`는 고정되므로 총알/머즐 위치와 시각 반동 사이에 미세한 차이가 날 수 있다.
- 대응:
  - 1차는 총기별 `GunData` 값으로 체감 조정
  - 필요 시 후속 단계에서 `FirePoint`까지 포함한 `RecoilPivot` 구조로 확장

### 위험 3. 원격 클라이언트/Owner 모두에서 이중 재생
- `FireCounter.OnChange`는 모든 클라이언트에서 실행됨
- 대응:
  - 현재 구조를 유지하되, 기존 Fire 애니메이션 대신 동일 시점의 로컬 반동만 재생

## 6. 검증 계획

### 기능 회귀
- 권총 1종, 소총 1종, 샷건 1종
- 단발 / 연사 / 버스트
- 장착 직후 첫 발
- 무기 교체 후 첫 발
- 드랍 후 재장착

### 시각 검증
- 반동이 발사 직후 뒤로 밀리고 자연스럽게 복귀하는지
- 연사 시 반동이 끊기지 않고 누적/복귀하는지
- 플레이어 손 위치와 무기 위치가 과도하게 어긋나지 않는지
- 머즐 플래시 위치가 크게 어긋나지 않는지

### 네트워크 검증
- Host
- 원격 클라이언트 observer
- late join은 발사 중간 상태 동기화 대상이 아니므로 “다음 발부터 정상 연출” 기준으로 확인

## 7. 구현 순서

1. `GunData`에 반동 파라미터 추가
2. `WeaponRecoilOffset` 컴포넌트 추가
3. `NetworkedWeapon`의 fire animation 경로를 recoil 호출로 교체
4. 대표 총기 프리팹 2~3종에서 recoil target 탐색 결과 확인
5. 플레이 테스트 및 수치 조정

## 8. 최종 제안

이번 변경은 **무기 발사 반동만 Animator 의존에서 빼고**,  
플레이어 상체/재장전 애니메이션은 그대로 유지하는 방향이 가장 안전하다.

핵심 구현 포인트는 **`FirePoint`를 제외한 실제 총기 시각 메쉬 child를 recoil target으로 잡고 `LateUpdate()`에서 로컬 반동을 적용하는 것**이다.
