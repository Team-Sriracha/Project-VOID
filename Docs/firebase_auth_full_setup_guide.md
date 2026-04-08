# Firebase 인증/프로필 시스템 상세 설정 가이드 (Project-VOID)

## 0. 문서 목적

이 문서는 Project-VOID에 적용된 로그인 시스템(`Email/Password + Anonymous`, 모바일 소셜 확장)을
실제로 동작시키기 위한 **설정 절차 전체**를 단계별로 설명합니다.

- 대상: Unity 클라이언트 개발자, 서버 운영자, 백엔드 개발자
- 범위: Firebase Console, Unity 프로젝트, 서버 실행 인자, Firestore Rules, 연동 검증
- 기준 날짜: 2026-03-03

관련 구현/계획 문서:
- 루트 문서: `Firebase.md`
- 초보자 문서: `Docs/firebase_auth_beginner_guide.md`
- 본 가이드: `Docs/firebase_auth_full_setup_guide.md`

정책 메모(2026-03-03 반영):
- Guest는 영구 저장 대상이 아닙니다.
- Guest 로그인 시 Firestore 프로필 생성/갱신과 전적 커밋을 수행하지 않습니다.
- `GuestId`는 UID 기반 파생값으로 세션 표시용으로만 사용합니다.
- Login 씬 UI는 런타임 자동 생성 없이, 인스펙터에 연결된 UI만 사용합니다.
- Lobby 시작 버튼은 인증 서비스 미등록 상태도 예외 없이 Login 씬으로 리다이렉트합니다.

---

## 1. 현재 코드 기준 동작 구조 요약

현재 코드에서 인증/데이터 서비스는 아래처럼 동작합니다.

1. `AuthServiceInitializer`
- Firebase 의존성 체크 성공 시:
  - `FirebaseAuthService`
  - `FirestorePlayerDataService`
  등록
- 실패 시:
  - `OfflineAuthService`
  - `OfflinePlayerDataService`
  등록

2. 로그인 경로
- Email/Password 로그인 및 회원가입
- Anonymous(Guest) 로그인
- 모바일 소셜은 토큰 제공자(`IGoogleAuthTokenProvider`, `IAppleAuthTokenProvider`)가 등록된 경우 활성 동작
- Login 씬은 `EntryPanel -> Login/Register` 전환 구조를 기본으로 사용
- 로그인 UI 필수 참조 누락 시 에러 로그를 출력하고 `LoginUI`를 비활성화

3. 서버 검증 경로
- 클라이언트는 UID가 아니라 `ID Token` 전송
- 서버에서 `BackendIdentityVerificationService`로 검증
- 검증 성공 UID만 `NetworkManager`에 등록

4. 전적 반영
- `GameStateManager` 종료 시 서버 authoritative 집계
- `BackendMatchResultService`로 커밋 API 호출(Guest 제외)

---

## 2. 사전 준비

## 2-1. 필수 준비물

1. Unity 6.0 (6000.0.58f2) 프로젝트 실행 가능 환경
2. Firebase Console 접근 권한
3. Android 패키지명 / iOS Bundle ID 확정
4. 백엔드 API 배포 주소
- `verify-id-token`
- `commit-result`

## 2-2. 권장 준비물

1. Android 실제 기기 1대 이상
2. iOS 실제 기기 1대 이상
3. 서버 실행 환경(Docker/systemd/직접 실행)
4. Firebase 프로젝트 운영/개발 분리

---

## 3. Firebase Console 설정

## 3-1. Firebase 프로젝트 생성

1. Firebase Console에서 새 프로젝트 생성
2. Analytics는 팀 정책에 맞게 활성/비활성
3. 프로젝트 ID 기록
- 서버 환경변수/운영 문서에 동일 값 사용

## 3-2. 앱 등록

1. Android 앱 등록
- Android package name 입력
- SHA-1/SHA-256(소셜 로그인 예정이면 권장) 등록
2. iOS 앱 등록
- iOS Bundle ID 입력
- Apple Team 연결(Apple 로그인 예정 시)

## 3-3. 설정 파일 다운로드 및 배치

1. Android: `google-services.json` 다운로드
2. iOS: `GoogleService-Info.plist` 다운로드
3. Unity 프로젝트에 배치
- `Assets/google-services.json`
- `Assets/GoogleService-Info.plist`

주의:
- 파일명을 바꾸지 않습니다.
- 다중 환경(dev/prod)을 운영하면 빌드 파이프라인에서 파일 교체 규칙을 분리합니다.

## 3-4. Authentication Provider 활성화

Firebase Console > Authentication > Sign-in method

1. Email/Password 활성화
2. Anonymous 활성화
3. Google 활성화 (모바일 확장용)
4. Apple 활성화 (iOS 확장용)

Apple 주의:
- Service ID, Team ID, Key ID, Private Key 설정이 필요합니다.
- 리디렉션 URI와 Apple Developer 설정이 일치해야 합니다.

## 3-5. Firestore 생성

1. Firestore Database 생성 (Native mode)
2. 리전 선택
- 서버 리전과 근접하게 선택
3. 초기 규칙은 잠금 모드로 시작 후 아래 규칙 적용

---

## 4. Firestore Rules / 데이터 모델 설정

## 4-1. 컬렉션 모델

1. `players/{uid}`
- 프로필 본문
2. `displayNames/{normalizedName}`
- 닉네임 인덱스

권장 필드(`players/{uid}`):
- `Uid`, `GuestId`, `IsGuest`, `DisplayName`, `Email`
- `CreatedAtUtc`, `LastLoginAtUtc`
- `TotalMatches`, `Wins`, `Kills`, `Deaths`

## 4-2. Rules 예시

```javascript
rules_version = '2';
service cloud.firestore {
  match /databases/{database}/documents {

    function isSignedIn() {
      return request.auth != null;
    }

    function isOwner(uid) {
      return isSignedIn() && request.auth.uid == uid;
    }

    match /players/{uid} {
      allow read, update, delete: if isOwner(uid);
      allow create: if isOwner(uid);
    }

    // 닉네임 인덱스는 클라이언트 직쓰기 금지 권장
    match /displayNames/{nameKey} {
      allow read: if isSignedIn();
      allow write: if false;
    }
  }
}
```

운영 권장:
- `displayNames` 생성/수정은 서버(Cloud Functions/Cloud Run)에서만 처리
- 현재 클라이언트 경로를 유지한다면 Rules와 코드 권한 모델이 충돌하지 않도록 조정 필요

## 4-3. 인덱스

기본 동작에는 복합 인덱스가 크게 필요하지 않지만, 리더보드/검색 추가 시 별도 인덱스 생성이 필요합니다.

---

## 5. Unity 프로젝트 설정

## 5-1. Firebase SDK 설치

`LoginUI`/`AuthServiceInitializer` 동작을 위해 최소 `App + Auth + Firestore`가 필요합니다.

### 권장 경로 A: `.unitypackage` 가져오기

1. Firebase Unity SDK 릴리즈 압축 파일 다운로드 후 압축 해제
2. Unity에서 순서대로 `Assets > Import Package > Custom Package...` 실행
- `FirebaseApp.unitypackage` (필수, 이름이 `FirebaseAI`가 아님)
- `FirebaseAuth.unitypackage`
- `FirebaseFirestore.unitypackage`
3. 버전에 따라 의존 패키지(`FirebaseInstallations`) import 요구가 뜨면 함께 import
4. Android 타깃이면 `External Dependency Manager` Resolve 수행
- 메뉴: `Assets > External Dependency Manager > Android Resolver > Resolve`
5. Unity 재시작 후 콘솔 에러 확인

### 대체 경로 B: UPM `.tgz` 설치

1. `Window > Package Manager` 열기
2. `+` 버튼 > `Add package from tarball...`
3. `Firebase App`, `Firebase Auth`, `Firebase Firestore` 순으로 추가
4. 설치 후 Unity 재시작

### 설치 확인 체크

1. 코드에서 아래 네임스페이스 참조가 컴파일되는지 확인
- `using Firebase;`
- `using Firebase.Auth;`
- `using Firebase.Firestore;`
2. 플레이 시작 시 `AuthServiceInitializer` 로그 확인
- Firebase 정상 감지 시: `Mode=Firebase`
- 누락 시 자동 폴백: `Mode=Offline`

## 5-2. 로그인 씬 구성 (최신 구현 기준)

현재 `LoginUI`는 인스펙터에서 수동 바인딩한 UI만 사용합니다.

### 5-2-A. 공통 필수 오브젝트

1. `AuthServiceInitializer` 오브젝트 배치
- `_forceOfflineMode`: 운영은 `false`
- `_dontDestroyOnLoad`: `true` 권장

2. `LoginUI` 오브젝트 배치
- 최소한 `LoginUI` 컴포넌트는 씬에 존재해야 합니다.

3. Build Settings 씬 순서 확인
- `Login` -> `Lobby` -> `Matchmaking` -> `GamePlay`

### 5-2-B. 권장(수동 바인딩) 구성

다음 필드는 인스펙터 바인딩을 권장합니다.

1. 패널
- `_entryPanel`
- `_loginPanel`
- `_registerPanel`

2. 진입 버튼(EntryPanel 내부)
- `_showLoginPanelButton` (기존 ID로 로그인)
- `_showRegisterPanelButton` (가입하기)
- `_entryGuestLoginButton` (게스트로 시작)

3. 뒤로가기 버튼(각 패널, 선택)
- `_loginBackButton` (LoginPanel -> EntryPanel)
- `_registerBackButton` (RegisterPanel -> EntryPanel)

4. 입력 필드
- `_loginEmailInput`
- `_loginPasswordInput`
- `_registerEmailInput`
- `_registerPasswordInput`
- `_registerDisplayNameInput`

5. 상태 UI
- `_statusText`
- `_loadingIndicator`

6. 소셜 버튼(선택)
- `_googleSignInButton`
- `_appleSignInButton`

### 5-2-C. 바인딩 누락 시 동작

`LoginUI.Start()`에서 필수 참조를 검사합니다.

1. 필수 참조가 모두 있으면 정상 동작합니다.
2. 하나라도 누락되면 누락 항목별 에러 로그를 출력하고 `LoginUI` 컴포넌트를 비활성화합니다.

주의:
- 자동으로 `RuntimeLoginUi`를 만들지 않습니다.
- 로그인 씬 UI는 반드시 직접 구성/연결해야 합니다.

## 5-3. 서비스 로딩 확인 포인트

플레이 시작 시 콘솔에서 아래 로그 확인:

1. Firebase 사용 모드
- `[AuthServiceInitializer] Auth services registered. Mode=Firebase`
2. Firebase 미탐지 폴백
- Firebase SDK 누락 시 `Mode=Offline`

## 5-4. Lobby 진입 가드 확인

- `LobbyUI` 시작 버튼은 로그인 상태를 검사합니다.
- 인증 서비스 미등록이면 `Login` 씬으로 리다이렉트되고 매칭 시작이 차단됩니다.
- 인증 서비스 등록 상태에서 미로그인이어도 `Login` 씬으로 리다이렉트됩니다.
- `authService.IsSignedIn == true`일 때만 `Matchmaking` 흐름으로 진행됩니다.

---

## 6. 소셜 로그인(모바일) 연결

현재 코드는 Firebase 측 소셜 Credential 생성까지 구현되어 있습니다.
실제 SDK 로그인은 **토큰 제공자 구현체 등록**이 필요합니다.

## 6-1. 인터페이스

- `IGoogleAuthTokenProvider`
- `IAppleAuthTokenProvider`
- 토큰 구조체: `SocialAuthToken { IdToken, AccessToken, RawNonce }`

## 6-2. 필수 작업

1. Google SDK(또는 GPGS)에서 ID/Access 토큰 획득 구현
2. Apple 로그인 SDK에서 Identity Token/Nonce 획득 구현
3. 초기 부트스트랩에서 ServiceLocator 등록

```csharp
ServiceLocator.Register<IGoogleAuthTokenProvider>(new YourGoogleTokenProvider());
ServiceLocator.Register<IAppleAuthTokenProvider>(new YourAppleTokenProvider());
```

주의:
- 구현체 미등록 시 기본 `Unsupported*TokenProvider`가 예외 메시지를 제공합니다.

---

## 7. 서버 백엔드 연동 설정

## 7-1. verify-id-token API 계약

요청(JSON):
```json
{ "IdToken": "..." }
```

응답(JSON):
```json
{
  "IsValid": true,
  "FirebaseUid": "uid",
  "GuestId": "G-AB12CD34",
  "DisplayName": "Player_1234",
  "Email": "user@example.com",
  "IsAnonymous": false,
  "ErrorMessage": ""
}
```

실패 시:
- `IsValid=false`
- `ErrorMessage`에 사유 기입

## 7-2. commit-result API 계약

요청(JSON):
- `MatchId`, `Mode`, `StartedAtUtc`, `EndedAtUtc`, `WinnerUid`, `Players[]`

응답(JSON):
```json
{ "Success": true, "ErrorMessage": "" }
```

참고:
- 빈 응답 바디도 현재 코드에서 성공으로 처리합니다.

## 7-3. 서버 실행 인자/환경변수

둘 중 하나 사용:

1. 실행 인자
- `--verify-token-url=https://your.domain/auth/verify-id-token`
- `--match-result-url=https://your.domain/match/commit-result`

2. 환경변수
- `PROJECTVOID_VERIFY_TOKEN_URL`
- `PROJECTVOID_MATCH_RESULT_URL`

Linux(systemd) 예시:

```ini
Environment="PROJECTVOID_VERIFY_TOKEN_URL=https://your.domain/auth/verify-id-token"
Environment="PROJECTVOID_MATCH_RESULT_URL=https://your.domain/match/commit-result"
```

---

## 8. 계정 정책 검증 포인트

## 8-1. Guest 지속성

1. Guest 로그인
2. 앱 재시작
3. 다시 Guest 로그인
4. Guest 프로필 문서가 생성되지 않는지 확인(`players/{uid}` 미생성)
5. `GuestId`가 UID 기반 파생값으로 표시되는지 확인

## 8-2. Guest -> Email 업그레이드

1. Guest 상태에서 업그레이드 수행
2. 업그레이드 후 UID 동일성 확인
3. 업그레이드 시점에 회원 프로필이 생성되는지 확인

## 8-3. 서버 검증 경계

1. 비정상 토큰 전송
2. 서버에서 신원 등록 실패 + 연결 종료(킥) 확인

---

## 9. 테스트 시나리오 (권장 순서)

1. EntryPanel에서 `기존 ID로 로그인` 클릭 시 `LoginPanel`이 표시되는지 확인
2. EntryPanel에서 `가입하기` 클릭 시 `RegisterPanel`이 표시되는지 확인
3. Email 회원가입 -> 로그인 -> Lobby 이동 확인
4. Guest 로그인 -> Lobby 이동 확인
5. Guest로 1경기 진행 -> 결과 커밋 스킵 로그 확인
6. Guest -> Email 업그레이드 -> UID 유지 확인
7. 소셜 로그인(구현체 연결 후) 동작 확인
8. 로그인 씬 필수 참조를 일부 비운 상태에서 에러 로그 출력 + `LoginUI` 비활성화 확인
9. AuthService 미등록 상태에서 Lobby 시작 클릭 시 Login 리다이렉트 확인
10. AuthService 등록 + 미로그인 상태에서 Lobby 시작 클릭 시 Login 리다이렉트 확인
11. 서버 URL 제거 상태에서 폴백 동작 확인
12. Firestore Rules 위반 요청 차단 확인

---

## 10. 장애 대응 체크리스트

## 10-1. `Mode=Offline`로만 뜨는 경우

1. Firebase SDK 설치 여부 확인
2. `Firebase App/Auth/Firestore` 패키지 import 확인
3. `google-services.json`/`GoogleService-Info.plist` 위치 확인
4. `CheckAndFixDependenciesAsync` 결과 로그 확인

## 10-2. 이메일 로그인 실패

1. Firebase Authentication에서 Email/Password 활성화 확인
2. 입력 이메일 형식/비밀번호 길이 확인
3. Firebase 콘솔 Auth 사용자 생성 여부 확인

## 10-3. 회원 계정 전적 커밋 실패

1. 서버 URL 주입 여부 확인
2. 백엔드 응답 HTTP 코드(2xx 여부) 확인
3. `commit-result` 응답 필드명 일치 확인

## 10-4. 소셜 로그인 버튼 오류

1. 토큰 제공자 구현체 등록 여부 확인
2. 플랫폼별 SDK 초기화 여부 확인
3. Google/Apple 콘솔 설정 및 OAuth 리디렉션 확인

---

## 11. 운영 전 최종 점검

1. 개발/스테이징/운영 Firebase 프로젝트 분리
2. 서버 API 키/시크릿 보관(코드 하드코딩 금지)
3. Firestore Rules 최소 권한 적용
4. 로그에서 토큰/개인정보 마스킹
5. 강제 업데이트/버전 호환 정책 확정

---

## 12. 남은 고도화 항목

1. `displayNames` 원자성 트랜잭션 강화(서버 전용 권장)
2. `credential-already-in-use` 충돌 시 서버 병합 API 완성
3. 모바일 소셜 제공자 구현체 템플릿 추가
4. 자동화 테스트(로그인/업그레이드/커밋) 구축

---

## 13. 로그인 씬 인스펙터 바인딩 상세

이 섹션은 실제로 Login 씬에서 자주 발생하는 누락을 방지하기 위한 상세 체크리스트입니다.

## 13-1. 권장 오브젝트 트리

```text
LoginScene
├─ AuthServiceInitializer
├─ Canvas
│  ├─ EntryPanel
│  │  ├─ Button_ShowLogin (기존 ID로 로그인)
│  │  ├─ Button_ShowRegister (가입하기)
│  │  └─ Button_GuestFromEntry (게스트로 시작)
│  ├─ LoginPanel
│  │  ├─ Input_LoginEmail (TMP_InputField)
│  │  ├─ Input_LoginPassword (TMP_InputField)
│  │  ├─ Button_Login
│  │  ├─ Button_Guest
│  │  ├─ Button_Google
│  │  ├─ Button_Apple
│  │  ├─ Button_ToRegister
│  │  └─ Button_BackEntry
│  ├─ RegisterPanel
│  │  ├─ Input_RegisterEmail (TMP_InputField)
│  │  ├─ Input_RegisterPassword (TMP_InputField)
│  │  ├─ Input_RegisterDisplayName (TMP_InputField)
│  │  ├─ Button_Register
│  │  ├─ Button_ToLogin
│  │  └─ Button_BackEntry
│  ├─ Text_Status (TMP_Text)
│  └─ LoadingIndicator (GameObject)
└─ EventSystem
```

## 13-2. `LoginUI` 필드 연결표

| `LoginUI` Serialized Field | 연결 대상 | 필수 여부 | 비고 |
|---|---|---|---|
| `_entryPanel` | `EntryPanel` | 선택 | 미연결 시 Entry UX 축소 가능 |
| `_loginPanel` | `LoginPanel` | 필수 | 누락 시 `LoginUI` 비활성화 |
| `_registerPanel` | `RegisterPanel` | 필수 | 누락 시 `LoginUI` 비활성화 |
| `_showLoginPanelButton` | `Button_ShowLogin` | 선택 | EntryPanel 버튼 |
| `_showRegisterPanelButton` | `Button_ShowRegister` | 선택 | EntryPanel 버튼 |
| `_entryGuestLoginButton` | `Button_GuestFromEntry` | 선택 | EntryPanel 게스트 시작 버튼 |
| `_loginBackButton` | `Button_BackFromLogin` | 선택 | LoginPanel -> EntryPanel 복귀 |
| `_registerBackButton` | `Button_BackFromRegister` | 선택 | RegisterPanel -> EntryPanel 복귀 |
| `_loginEmailInput` | `Input_LoginEmail` | 필수 | 누락 시 `LoginUI` 비활성화 |
| `_loginPasswordInput` | `Input_LoginPassword` | 필수 | 누락 시 `LoginUI` 비활성화 |
| `_registerEmailInput` | `Input_RegisterEmail` | 필수 | 누락 시 `LoginUI` 비활성화 |
| `_registerPasswordInput` | `Input_RegisterPassword` | 필수 | 누락 시 `LoginUI` 비활성화 |
| `_registerDisplayNameInput` | `Input_RegisterDisplayName` | 필수 | 누락 시 `LoginUI` 비활성화 |
| `_statusText` | `Text_Status` | 필수 | 누락 시 `LoginUI` 비활성화 |
| `_loadingIndicator` | `LoadingIndicator` | 필수 | 누락 시 `LoginUI` 비활성화 |
| `_googleSignInButton` | `Button_Google` | 선택 | 플랫폼별 자동 노출 제어 |
| `_appleSignInButton` | `Button_Apple` | 선택 | iOS에서만 노출 |

## 13-3. 버튼 OnClick 연결표

| 버튼 | Target 오브젝트 | 함수 |
|---|---|---|
| ShowLogin(Entry) | `LoginUI`가 붙은 오브젝트 | `LoginUI.ShowLoginPanel` |
| ShowRegister(Entry) | 동일 | `LoginUI.ShowRegisterPanel` |
| GuestFromEntry | 동일 | `LoginUI.OnClickGuestLogin` |
| Login | `LoginUI`가 붙은 오브젝트 | `LoginUI.OnClickLogin` |
| Guest | 동일 | `LoginUI.OnClickGuestLogin` |
| Register | 동일 | `LoginUI.OnClickRegister` |
| Google | 동일 | `LoginUI.OnClickGoogleSignIn` |
| Apple | 동일 | `LoginUI.OnClickAppleSignIn` |
| ToRegister(LoginPanel) | 동일 | `LoginUI.ShowRegisterPanel` |
| ToLogin(RegisterPanel) | 동일 | `LoginUI.ShowLoginPanel` |
| BackEntry(LoginPanel) | 동일 | `LoginUI.ShowEntryPanel` |
| BackEntry(RegisterPanel) | 동일 | `LoginUI.ShowEntryPanel` |

## 13-4. 미바인딩 동작 체크

1. `LoginUI` 필수 참조 중 1개 이상을 비운 뒤 플레이를 시작합니다.
2. 콘솔에 누락 필드별 에러 로그가 출력되는지 확인합니다.
- 예: `[LoginUI] _loginEmailInput 참조가 비어 있습니다.`
3. 콘솔에 아래 로그가 출력되는지 확인합니다.
- `[LoginUI] UI 참조 누락으로 LoginUI를 비활성화합니다. 런타임 UI 자동 생성은 사용하지 않습니다.`
4. Hierarchy에 `RuntimeLoginUi` 같은 자동 생성 오브젝트가 생기지 않는지 확인합니다.

## 13-5. 씬 전환 관련 체크

1. `Lobby` 씬 이름이 정확히 `Lobby`인지 확인
2. `Build Settings`에 `Login`, `Lobby`가 모두 포함되어 있는지 확인
3. `AuthServiceInitializer`가 `DontDestroyOnLoad`를 유지하는지 확인

---

## 14. 계정 Linking(UID 통일) 상세 플로우

## 14-1. 기본 원칙

1. 최초 진입은 Guest(Anonymous) 허용
2. 계정 업그레이드는 항상 `LinkWith*Async` 우선
3. 성공하면 **UID는 기존 Guest UID 유지**
4. 실패 시(이미 다른 계정에 연결됨)는 기존 계정 로그인 후 데이터 병합

## 14-2. Guest -> Email Linking 성공 플로우

1. Guest 로그인 성공
2. 게스트 프로필은 생성하지 않고 세션 표시용 `GuestId`만 파생
3. 사용자가 이메일 업그레이드 선택
4. `IAuthService.LinkWithEmailAsync(email, password, displayName)` 호출
5. Firebase가 현재 Anonymous User에 Email Credential 연결
6. UID 동일성 유지 확인
7. 업그레이드 시점에 회원 프로필 생성 및 `Email`, `DisplayName` 저장
8. 이후 로그인은 Email/Password 또는 기존 연결된 소셜로 가능

## 14-3. Guest -> Google/Apple Linking 성공 플로우

1. Guest 로그인
2. 플랫폼 SDK에서 소셜 토큰 획득
3. `LinkWithGoogleAsync` 또는 `LinkWithAppleAsync` 호출
4. Firebase Credential 연결 성공
5. UID 유지 + 프로필 비게스트 전환

## 14-4. Linking 충돌 플로우(필수 대응)

대표 오류: `credential-already-in-use`, `email-already-in-use`

1. Linking 실패 감지
2. 해당 자격증명으로 기존 계정 로그인
3. 서버 병합 API 호출
- 원본 Guest UID 데이터 -> 대상 UID로 이관
- 닉네임/통계 충돌 규칙 적용
4. 병합 완료 후 원본 Guest 데이터 정리

운영 메모:
- 현재 코드베이스는 Linking 우선 경로를 갖췄고, 충돌 병합 API는 후속 고도화 항목입니다.

---

## 15. 플랫폼별 상세 설정 절차

## 15-1. Android

1. `Project Settings > Player > Android`에서 Package Name 확정
2. Firebase Android App 등록 시 동일 Package Name 입력
3. 디버그/릴리즈 서명키 SHA-1, SHA-256 등록
4. `google-services.json`을 `Assets/` 루트 배치
5. Android 실제 기기에서 Google 로그인 버튼 노출 확인

### SHA 추출 (Windows PowerShell, 디버그 키)

아래를 그대로 실행하면 `SHA1`, `SHA256` 값을 얻을 수 있습니다.

```powershell
# 1) Unity OpenJDK keytool 경로 예시 (버전에 맞게 수정)
$keytool = "C:\Program Files\Unity\Hub\Editor\6000.0.58f2\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK\bin\keytool.exe"

# 2) debug.keystore 경로
$keystore = "$env:USERPROFILE\.android\debug.keystore"

# 3) SHA 출력
& $keytool -list -v `
  -alias androiddebugkey `
  -keystore $keystore `
  -storepass android `
  -keypass android
```

### keytool 인식 실패 시 점검

1. `keytool` 명령이 안 잡히면 JDK 경로를 절대 경로로 지정해서 실행
2. `debug.keystore`가 없으면 Android 빌드를 1회 수행해 파일 생성
3. 경로를 줄바꿈으로 끊지 말고 한 줄 문자열로 입력
4. 출력된 `SHA1`, `SHA256`를 Firebase Console의 `디지털 지문 추가`에 각각 등록

### 릴리즈 키 SHA 추출(출시용)

```powershell
& $keytool -list -v `
  -alias <릴리즈_별칭> `
  -keystore "<릴리즈_keystore_절대경로>" `
  -storepass <스토어비밀번호> `
  -keypass <키비밀번호>
```

## 15-2. iOS

1. `Project Settings > Player > iOS` Bundle Identifier 확정
2. Firebase iOS App 등록 시 동일 Bundle ID 입력
3. `GoogleService-Info.plist`를 `Assets/` 루트 배치
4. Apple Sign In 활성화
- Firebase Authentication > Apple Provider 활성화
- Apple Developer의 Service ID/Key/Team 설정 일치
5. iOS 실제 기기 테스트(시뮬레이터 의존 테스트 지양)

## 15-3. PC(Windows)

1. Email/Password + Guest 경로만 사용
2. Google/Apple 버튼은 `LoginUI`에서 자동 비노출
3. `AuthServiceInitializer` 초기화 실패 시 Offline 폴백 동작 확인

---

## 16. 서버/백엔드 계약 검증 샘플

## 16-1. verify-id-token 샘플 요청/응답

요청:
```json
{
  "IdToken": "eyJhbGciOiJSUzI1NiIs..."
}
```

응답 성공:
```json
{
  "IsValid": true,
  "FirebaseUid": "firebase_uid_1234",
  "GuestId": "G-AB12CD34",
  "DisplayName": "Player_1234",
  "Email": "user@example.com",
  "IsAnonymous": false,
  "ErrorMessage": ""
}
```

응답 실패:
```json
{
  "IsValid": false,
  "FirebaseUid": "",
  "ErrorMessage": "Token expired"
}
```

## 16-2. commit-result 샘플 요청/응답

요청:
```json
{
  "MatchId": "M_20260303_001",
  "Mode": "Normal",
  "StartedAtUtc": "2026-03-03T04:00:00.0000000Z",
  "EndedAtUtc": "2026-03-03T04:11:42.0000000Z",
  "WinnerUid": "firebase_uid_1234",
  "Players": [
    {
      "Uid": "firebase_uid_1234",
      "Kills": 3,
      "Deaths": 1,
      "Rank": 1,
      "SurvivalSeconds": 701.2,
      "IsGuest": false
    }
  ]
}
```

응답:
```json
{
  "Success": true,
  "ErrorMessage": ""
}
```

---

## 17. 실행/검증 절차 (운영 리허설용)

## 17-1. 서버 실행 인자 예시

```bash
./ProjectVoidServer.x86_64 \
  --verify-token-url=https://api.example.com/auth/verify-id-token \
  --match-result-url=https://api.example.com/match/commit-result
```

환경변수 방식 예시:

```bash
export PROJECTVOID_VERIFY_TOKEN_URL="https://api.example.com/auth/verify-id-token"
export PROJECTVOID_MATCH_RESULT_URL="https://api.example.com/match/commit-result"
./ProjectVoidServer.x86_64
```

## 17-2. 기대 로그

1. 로그인 시스템
- `[AuthServiceInitializer] Auth services registered. Mode=Firebase`
2. 신원 등록 성공
- `[NetworkManager] Player identity registered. ClientId=..., Uid=..., Guest=...`
3. 백엔드 URL 미설정(개발 스텁)
- `[BackendMatchResultService] Endpoint 미설정. 스텁 성공 처리 ...`

## 17-3. 실패 로그 기준

1. 토큰 검증 실패
- `Identity verification failed. ClientId=..., Reason=...`
2. Firestore 연동 실패
- `FirestorePlayerDataService ... 폴백` 경고 반복 여부 확인
3. 소셜 토큰 제공자 미등록
- `Google/Apple 토큰 제공자가 등록되지 않았습니다` 메시지 확인

---

## 18. 운영 전환 Runbook

## 18-1. 배포 순서

1. Firebase Console 설정 확정(Provider/Rules)
2. 백엔드 배포(`verify-id-token`, `commit-result`)
3. 서버 실행 인자/환경변수 반영
4. QA 빌드 배포 후 시나리오 테스트
5. 운영 빌드 배포

## 18-2. 롤백 전략

1. 로그인 장애 시 서버 URL 임시 해제(개발/스테이징 한정)
2. Firebase Provider 오설정 시 직전 설정으로 복원
3. 클라이언트 치명 오류 시 직전 안정 빌드 롤백

## 18-3. 운영 모니터링 포인트

1. 로그인 성공률(Email/Guest/소셜 별)
2. 토큰 검증 실패율
3. 전적 커밋 실패율
4. 닉네임 충돌/생성 실패율
5. Guest -> 계정 업그레이드 전환율

---

## 19. 최종 점검 요약(Go-Live 직전)

1. Firebase SDK/설정 파일 배치 확인
2. Provider 활성화(Email/Password, Anonymous, Google, Apple) 확인
3. Firestore Rules 최소 권한 적용 확인
4. 서버 `verify-id-token`/`commit-result` URL 주입 확인
5. Guest 로그인/업그레이드/UID 유지 테스트 통과 확인
6. 모바일 실기기 소셜 로그인 테스트 통과 확인
