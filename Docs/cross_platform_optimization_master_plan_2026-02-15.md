# Project VOID 크로스플랫폼 최적화 마스터 플랜 (2026-02-15)

## 1. 목표

- PC 품질을 유지하면서 모바일(Android/iOS) 성능을 안정화한다.
- Fish-Net 서버 권한 구조를 유지하며 네트워크 비용을 줄인다.
- 에디터에서 즉시 가능한 작업을 먼저 적용해 빠른 성능 개선을 확보한다.

## 2. 현재 진단 요약

### 2-1. 구조/용량
- `Assets/Content`와 `Assets/External Assets` 비중이 매우 크다.
- 빌드 씬은 `ServerScene`, `Lobby`, `Matchmaking`, `GamePlay`로 구성된다.

### 2-2. 설정
- 품질 레벨이 사실상 PC 중심 1세트로 운영되어 모바일 분리가 약하다.
- 모바일 전용 RP Asset은 존재하나 실제 적용 경로가 불명확하다.

### 2-3. 에셋 병목
- TMP 폰트 아틀라스가 8192 기반으로 매우 크다.
- 4096 고해상도 텍스처와 압축 미적용 자산이 다수 존재한다.
- 모델 임포트가 기본값(카메라/라이트/블렌드셰이프 포함)으로 남아있는 케이스가 많다.

### 2-4. 코드 병목
- 루프 내 탐색(`FindObjectsByType`)과 할당(`OverlapSphere`, `Instantiate/Destroy`)이 존재한다.
- 고빈도 `Debug.Log`가 런타임 비용을 늘린다.
- 일부 `Update`가 과도한 책임을 가진다.

## 3. 타겟 KPI

### 3-1. 프레임
- PC: 120 FPS 목표, 1% low 프레임타임 16.6ms 이내
- 모바일(중상급): 60 FPS 목표, 1% low 프레임타임 22ms 이내
- 모바일(보급형): 30 FPS 안정 유지

### 3-2. 메모리
- Android 중상급: 플레이 중 1.3GB 이내
- Android 보급형: 플레이 중 900MB 이내
- 씬 왕복 5회 후 메모리 누수 패턴 없음

### 3-3. 네트워크
- 전투 밀집 시 클라이언트 평균 40KB/s 이하
- 평시 15KB/s 이하

## 4. 실행 순서

## Phase 0. 기준선 측정

1. 동일 시나리오로 Profiler 기준선 저장
- `Lobby` 3분 대기
- `GamePlay` 솔로 전투 5분
- `GamePlay` 밀집 전투 5분

2. 디바이스 매트릭스
- PC 2종, Android 2종, iOS 1종 이상

3. 저장 규칙
- `Docs/Optimization/Baseline/YYYY-MM-DD` 경로로 측정 파일 관리

## Phase 1. 에디터 무코드 최적화 (최우선)

### 1) 품질/렌더링 분리
- Quality를 `PC_High`, `Mobile_Mid`, `Mobile_Low`로 분리
- Android/iOS 기본 품질을 모바일 프리셋으로 고정
- 품질별 URP Asset 연결 명확화

### 2) 텍스처 임포트 정책
- Android/iOS는 ASTC 중심으로 통일
- 월드 텍스처는 2048 기본, 핵심만 4096 허용
- UI/아이콘은 용도별 Max Size 축소
- UI 성격 텍스처는 Mipmap Off

### 3) TMP 폰트 재구성 (핵심)
- 8192 아틀라스를 1024~2048 중심으로 축소
- Static/Dynamic 폰트 에셋 분리
- Fallback 체인을 단순화하고 미사용 폰트는 빌드 제외

### 4) 모델 임포트 정리
- `Read/Write Enabled`는 필요 모델만 허용
- `Import Cameras/Lights` 기본 Off
- `BlendShapes`는 실제 사용 모델만 유지
- LOD 미적용 대형 오브젝트 우선 적용

### 5) 빌드/셰이더 정리
- 데모/테스트 씬 빌드 제외
- `Always Included Shaders` 최소화
- Shader Variant 로그 기반으로 변형 수를 정리

## Phase 2. 코드 최적화 (승인 후 구현)

### 2-1. 할당/탐색 제거
- `OxygenDepletionManager`: 주기적 `FindObjectsByType` 제거, 캐시 기반 처리
- `NetworkedItem`/`ItemPickupDetector`/`NetworkedWeapon`: `OverlapSphereNonAlloc` 전환

### 2-2. 풀링 도입
- 전투 VFX `Instantiate/Destroy`를 풀링으로 전환
- UI 목록 엔트리 재사용 구조로 변경

### 2-3. Update 슬림화
- UI/게임 매니저 로직을 주기 분리 및 Dirty Flag 구조로 재편

### 2-4. 렌더링 연산 최적화
- `PlayerOcclusionFader`의 머티리얼 접근 비용 절감
- 모바일에서 FOV 레이 수/업데이트 주기 하향

## Phase 3. Fish-Net 네트워크 최적화 (승인 후 구현)

1. SyncVar/SyncList 범위를 최소 상태로 정리
2. RPC를 이벤트 전용으로 재정리하고 빈번 이벤트는 스로틀링
3. Observer 범위를 점검해 불필요 전송 제거
4. Late Join 동기화 페이로드를 단계화

## Phase 4. 런타임 적응 품질 (승인 후 구현)

1. 디바이스 클래스 분류(PC/모바일 상중하)
2. 자동 품질 프리셋 적용
3. FPS 저하 시 단계적 품질 하향(히스테리시스 포함)

## Phase 5. 회귀 방지

1. 릴리즈 후보마다 동일 시나리오 재측정
2. 에셋 임포트 규칙 위반 검사 자동화
3. 로그 과다/폰트 아틀라스 상한 검사 자동화
4. Host + Dedicated 모두 Observer/RPC 리허설

## 5. 사용자가 에디터에서 바로 할 작업

1. Quality 레벨 3종 분리 + 플랫폼 매핑
2. URP Asset 품질별 연결 확정
3. TMP 폰트 아틀라스 축소
4. 대형 텍스처/스카이박스 압축 재설정
5. 모델 임포트 기본 규칙 정리
6. 빌드 씬 및 Always Included Shaders 정리
7. Baseline 측정 파일 저장

## 6. 예상 효과 (보수적)

- 모바일 메모리 20~40% 절감 가능
- 모바일 CPU 프레임타임 15~30% 개선 가능
- 빌드 용량 10~25% 절감 가능
- 네트워크 트래픽 10~30% 절감 여지

## 7. 리스크와 대응

1. 화질 저하 체감
- 대응: 플랫폼별 스크린샷 비교와 예외 자산 화이트리스트

2. 폰트 글리프 누락
- 대응: Dynamic fallback + 실제 문자열 샘플 검증

3. NonAlloc 버퍼 초과
- 대응: 초과 카운트 로깅 + 안전 fallback

4. 동기화 축소로 상태 누락
- 대응: SyncVar/RPC 역할 표 먼저 확정 후 단계 반영

## 8. 결론

- 최우선은 Phase 1(에디터 무코드)이다.
- Phase 1 결과를 기준으로 Phase 2~4 코드 최적화를 순차 적용한다.
- 모든 단계는 Profiler 기준선과 비교하여 수치로 검증한다.
