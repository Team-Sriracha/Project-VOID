# Project VOID 서버 아키텍처 문서

## 1. 현재 구조

Project VOID는 현재 **Fish-Net + 자체 백엔드 + Unity Sessions** 구조를 사용합니다.

구성 요소:
1. **클라이언트 빌드**
- 로그인, 로비, 매칭 UI, 렌더링 담당
- 연습장 모드에서는 Yak 기반 로컬 Host를 띄워 서버 부담을 줄임

2. **게임 서버 빌드 (Linux 전용 운영 대상)**
- `ServerScene` 기반으로 실행
- Fish-Net 서버 연결, 세션 상태 광고, 플레이어 스폰/게임 상태 관리 담당
- 인증 백엔드 헬스 상태를 세션 `AuthReady` 속성에 반영

3. **자체 백엔드 (`Backend/self-hosted-api`)**
- `POST /auth/verify-id-token`
- `POST /profile/sync`
- `POST /profile/display-name`
- `POST /match/commit-result`
- `GET /health`

4. **Unity Sessions**
- 세션 목록 광고/검색에 사용
- `IdentityVerificationReady`, `IdentityVerificationStatus` 속성으로 접속 가능 세션 여부를 판정

## 2. 핵심 런타임 흐름

### 2-1. 일반 온라인 플레이

1. 클라이언트가 Firebase Auth로 로그인
2. 로비에서 Unity Sessions로 접속 가능한 세션 검색
3. 게임 서버는 시작 시 백엔드 `/health` 확인
4. 서버는 세션 `AuthReady` 상태를 Unity Sessions 속성에 반영
5. 클라이언트는 `AuthReady=true` 세션만 매칭 후보로 사용
6. 접속 후 ID Token 검증을 서버가 백엔드에 요청
7. 게임 종료 시 전적은 백엔드가 Firestore에 반영

### 2-2. 연습장 모드

1. 클라이언트가 `MatchmakingManager`를 통해 Yak Transport 기반 로컬 Host 시작
2. 로컬 프로세스 안에서 서버/클라이언트가 함께 동작
3. 전용 서버 운영 코드(`ServerBootstrap`, `ServerMonitorHttpServer`, `AutoStartServer`)와는 별도 경계
4. 목적은 실제 운영 서버 부하를 줄이면서 로컬 연습을 가능하게 하는 것

## 3. 코드 경계

### 3-1. 클라이언트/연습장 공용 게임플레이 코드

아래 코드는 연습장 로컬 Host에도 필요하므로 클라이언트 빌드에 포함됩니다.

- `NetworkManager`
- `GameStateManager`
- `NetworkMapManager`
- `PlayerController`
- `MobAI`, `MobCombat`, `MobSpawnManager`
- 무기/투사체 authoritative 처리 코드

### 3-2. 서버 운영 전용 코드

아래 코드는 클라이언트 플레이어 빌드에서 실제 구현이 제외됩니다.

- `ServerBootstrap`
- `AutoStartServer`
- `ServerMonitorHttpServer`

컴파일 조건:
- `UNITY_EDITOR`
- `UNITY_SERVER`
- `PROJECTVOID_SERVER_RUNTIME`

즉, 운영 서버 빌드 시에는 `PROJECTVOID_SERVER_RUNTIME` define가 필요합니다.

## 4. 빌드 정책

### 4-1. 클라이언트 빌드

메뉴:
- `Project VOID/Build/Build Windows Client`
- `Project VOID/Build/Build Android Client`
- `Project VOID/Build/Build iOS Client`

정책:
- `ServerScene` 제외
- 서버 런타임 define 자동 OFF
- Android/iOS는 `GamePlay` 씬에서
  - `Canvas`, `UIManager` 비활성화
  - `MobileCanvas`, `MobileUIManager` 활성화
  상태로 임시 전환 후 빌드

### 4-2. 서버 빌드

메뉴:
- `Project VOID/Build/Build Linux Server`

정책:
- `ServerScene + GamePlay` 포함
- 서버 런타임 define 자동 ON
- 현재 Linux 서버 빌드는 호스트 clang/IL2CPP 툴체인 문제를 피하기 위해 자동으로 **Mono 백엔드**로 전환 후 빌드

## 5. 운영 순서

권장 기동 순서:
1. 자체 백엔드 먼저 실행
2. 백엔드 `/health` 확인
3. 게임 서버 실행
4. 마지막에 클라이언트 실행

이유:
- 게임 서버는 시작 시점에 인증 백엔드 헬스를 확인해 세션 `AuthReady`를 광고함
- 백엔드보다 게임 서버가 먼저 뜨면 세션이 `AuthReady=false`로 등록되어 클라이언트가 매칭 후보에서 필터링할 수 있음

## 6. 보안 원칙

1. 클라이언트는 UID를 확정하지 않음
2. 서버는 ID Token 검증 결과만 신뢰
3. 닉네임/프로필 쓰기는 자체 백엔드 경유
4. 게스트는 온라인 인증 기반으로만 생성
5. `offline_token` 경로는 제거
6. `client_{ClientId}` fallback UID는 전적 저장에 사용하지 않음

## 7. 관련 문서

- [`Server/README.md`](/mnt/d/My/UnityProject/Project-VOID/Server/README.md)
- [`Backend/README.md`](/mnt/d/My/UnityProject/Project-VOID/Backend/README.md)
- [`Backend/self-hosted-api/README.md`](/mnt/d/My/UnityProject/Project-VOID/Backend/self-hosted-api/README.md)
- [`Firebase.md`](/mnt/d/My/UnityProject/Project-VOID/Firebase.md)
