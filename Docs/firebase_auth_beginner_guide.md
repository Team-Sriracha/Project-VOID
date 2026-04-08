# Firebase 완전 초보자 가이드 (Project-VOID)

## 0. 이 문서는 누구를 위한 문서인가

이 문서는 **Firebase를 한 번도 써본 적 없는 사람**을 기준으로 작성했습니다.
로그인 시스템 구현 전에 아래 내용을 먼저 이해하면, 이후 상세 가이드(`firebase_auth_full_setup_guide.md`)가 훨씬 쉬워집니다.

- 기준 날짜: 2026-03-03
- 대상: Unity 클라이언트 개발자, 서버 개발자, 운영자
- 목표: Project-VOID 로그인 시스템을 처음부터 끝까지 실제로 붙이기

정책 메모(2026-03-03 반영):
- Guest는 영구 저장 대상이 아닙니다.
- Guest 로그인은 가능하지만 Firestore 프로필/전적 커밋은 수행하지 않습니다.
- GuestId는 UID 기반 파생값으로 세션 표시용입니다.

---

## 1. Firebase를 한 줄로 설명하면

Firebase는 Google이 제공하는 백엔드 플랫폼입니다.
앱에서 자주 필요한 기능(인증, DB, 로그 등)을 빠르게 붙일 수 있게 해줍니다.

이 프로젝트에서 사용하는 Firebase 기능은 2개입니다.

1. `Firebase Authentication`
- 사용자 로그인 처리(이메일/비밀번호, 익명, Google, Apple)

2. `Cloud Firestore`
- 플레이어 프로필/닉네임/접속 시간 저장

---

## 2. Project-VOID에서 Firebase가 하는 역할

이 프로젝트의 핵심은 아래 흐름입니다.

1. 플레이어가 로그인 버튼 클릭
2. Firebase Auth가 로그인 성공/실패를 반환
3. 성공하면 `Firebase UID`가 생김
4. Firestore에 `players/{uid}` 프로필을 읽거나 생성
5. 멀티플레이 입장 시 클라이언트는 UID 직접 제출이 아니라 `ID Token` 제출
6. 서버가 백엔드 API로 토큰 검증
7. 검증 성공한 UID만 서버가 최종 신원으로 등록

핵심 포인트:
- **클라이언트가 자기 UID를 확정하면 안 됩니다.**
- 서버(또는 신뢰 백엔드) 검증 결과만 신뢰해야 합니다.

---

## 3. 꼭 알아야 하는 기본 용어

| 용어 | 의미 | 초보자 포인트 |
|---|---|---|
| Firebase Project | Firebase 콘솔에서 만드는 프로젝트 단위 | 게임 1개당 보통 1개 이상(dev/prod 분리 권장) |
| App 등록 | Android/iOS 앱 식별자 등록 | 패키지명/번들ID가 틀리면 인증 실패 |
| Authentication | 로그인 기능 | Provider를 켜야 해당 로그인 사용 가능 |
| Provider | 로그인 방식 | Email/Password, Anonymous, Google, Apple |
| UID | Firebase가 계정에 부여하는 고유 ID | 계정의 진짜 식별자 |
| ID Token | 로그인 증명 토큰(JWT) | 서버 검증용, 유효시간이 짧음 |
| Refresh Token | ID Token 갱신용 토큰 | SDK가 내부적으로 관리 |
| Anonymous | 익명 계정(Guest) | UID가 생기며 실제 계정으로 취급됨 |
| Linking | 여러 로그인 수단을 하나 UID에 연결 | Guest -> Email 업그레이드 핵심 |
| Firestore | 문서형 DB | `컬렉션/문서` 구조 |
| Collection | 문서 묶음 | 예: `players` |
| Document | 실제 데이터 1개 | 예: `players/{uid}` |
| Rules | Firestore 접근 권한 규칙 | 잘못 열어두면 보안 사고 가능 |
| Admin SDK | 서버 전용 Firebase SDK | ID Token 검증은 서버에서 이걸로 수행 |
| Console | Firebase 웹 관리 화면 | Provider ON/OFF, Rules 수정 등을 여기서 함 |

---

## 4. 먼저 이해해야 할 계정 구조

Project-VOID는 아래 정책입니다.

1. 모든 플랫폼에서 Guest(Anonymous) 로그인 허용
2. Guest는 계속 플레이 가능
3. 업그레이드 시 신규 계정 생성보다 **Linking 우선**
4. Linking 성공 시 UID 유지(게스트 저장 데이터는 없음)

예시:

1. 처음 Guest 로그인 -> UID `abc123`
2. 나중에 Email 계정 연결(Link) -> 여전히 UID `abc123`
3. 업그레이드 시점부터 회원 프로필/전적 저장 시작

이 구조 덕분에 “게스트로 하던 데이터 날아감” 문제를 줄일 수 있습니다.

---

## 5. 전체 구성도(개념)

```text
[Unity Client]
  ├─ LoginUI (이메일/게스트/소셜 버튼)
  ├─ FirebaseAuthService
  └─ FirestorePlayerDataService
        │
        │ (ID Token)
        ▼
[Game Server]
  └─ BackendIdentityVerificationService
        │
        │ (HTTP POST /auth/verify-id-token)
        ▼
[Trusted Backend]
  └─ Firebase Admin SDK로 토큰 검증
        │
        ▼
[Firebase Auth + Firestore]
```

---

## 6. 지금 바로 해야 하는 준비물

## 6-1. 계정/도구

1. Google 계정(필수)
2. Firebase Console 접근 권한
3. Unity 6.0 프로젝트 실행 가능 환경
4. Android/iOS 실제 테스트 기기(권장)

## 6-2. 결정해야 할 값

1. Android Package Name
2. iOS Bundle Identifier
3. Firebase 프로젝트 이름(예: `project-void-dev`)
4. 서버 API 주소(verify/commit)

이 값은 중간에 바꾸면 재설정 범위가 커지므로 먼저 확정하는 것이 좋습니다.

---

## 7. Firebase Console 처음 세팅 (클릭 경로 포함)

## 7-1. 프로젝트 생성

1. `https://console.firebase.google.com` 접속
2. `프로젝트 추가` 클릭
3. 프로젝트 이름 입력(예: `project-void-dev`)
4. Analytics는 팀 정책에 따라 ON/OFF
5. 생성 완료 후 프로젝트 대시보드 진입

기록할 값:
- Project ID
- Project Number

## 7-2. Authentication 활성화

경로: `Build > Authentication > Get started`

1. 시작 버튼 클릭
2. `Sign-in method` 탭 이동
3. 아래 Provider를 활성화
- Email/Password: Enable
- Anonymous: Enable
- Google: Enable (모바일)
- Apple: Enable (iOS)

초보자 실수:
- Provider를 켜지 않으면 코드가 맞아도 로그인 실패합니다.

## 7-3. Firestore 생성

경로: `Build > Firestore Database > Create database`

1. Native mode 선택
2. 리전 선택(서버와 가까운 리전 권장)
3. 생성 완료

초기 상태는 보안 규칙을 바로 점검하세요.

---

## 8. 앱 등록과 설정 파일 받기

## 8-1. Android 등록

1. `프로젝트 설정 > 내 앱 > Android 앱 추가`
2. Android package name 입력
3. SHA-1/SHA-256 등록(소셜 로그인 준비 시 중요)
4. `google-services.json` 다운로드
5. Unity 프로젝트 `Assets/google-services.json`에 배치

## 8-2. iOS 등록

1. `프로젝트 설정 > 내 앱 > iOS 앱 추가`
2. iOS Bundle ID 입력
3. `GoogleService-Info.plist` 다운로드
4. Unity 프로젝트 `Assets/GoogleService-Info.plist`에 배치

주의:
- 파일명 변경 금지
- 경로 오타 금지

---

## 9. Unity 쪽 세팅(초보자 버전)

## 9-1. Firebase SDK 설치

1. Firebase Unity SDK 릴리즈 다운로드
2. Unity Package Manager에서 필요한 패키지 추가
- Firebase App
- Firebase Auth
- Firebase Firestore
3. Unity 재시작
4. 콘솔 에러 확인

## 9-2. Login 씬 필수 오브젝트

1. `AuthServiceInitializer` 배치
- `_forceOfflineMode = false`
- `_dontDestroyOnLoad = true`

2. `LoginUI` 필드 연결
- 이메일/비밀번호 입력 TMP 필드
- Guest/Google/Apple 버튼
- 상태 텍스트/로딩 인디케이터

3. Build Settings 확인
- `Login`, `Lobby` 씬 포함 여부

---

## 10. Firestore 데이터 구조를 쉽게 이해하기

기본 컬렉션 2개(회원 계정 기준):

1. `players/{uid}`
- 플레이어 1명의 프로필 문서

2. `displayNames/{normalizedName}`
- 닉네임 중복 체크용 인덱스 문서

`players/{uid}` 예시(회원 계정):

```json
{
  "Uid": "abc123",
  "GuestId": "",
  "IsGuest": false,
  "DisplayName": "Player_A1B2",
  "Email": "user@example.com",
  "CreatedAtUtc": "2026-03-03T10:00:00Z",
  "LastLoginAtUtc": "2026-03-03T10:05:00Z",
  "TotalMatches": 12,
  "Wins": 3,
  "Kills": 21,
  "Deaths": 17
}
```

참고:
- Guest는 `players/{uid}` 문서를 만들지 않습니다.
- Guest 전적도 커밋 대상에서 제외됩니다.

---

## 11. Firestore Rules(권한) 왜 중요한가

Rules는 “누가 어떤 문서를 읽고/쓰는지”를 정합니다.

초보자가 많이 하는 위험한 설정:
- `allow read, write: if true;`

이렇게 열어두면 누구나 DB를 수정할 수 있어 위험합니다.

권장 기본 규칙:

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
      allow read, create, update, delete: if isOwner(uid);
    }

    match /displayNames/{nameKey} {
      allow read: if isSignedIn();
      allow write: if false;
    }
  }
}
```

---

## 12. 왜 서버에서 ID Token을 다시 검증하나

클라이언트는 변조될 수 있기 때문입니다.

안전한 방식:
1. 클라이언트는 ID Token 전달만 수행
2. 서버가 백엔드 API로 검증 요청
3. 백엔드가 Firebase Admin SDK로 토큰 검증
4. 검증된 UID만 서버 신원으로 사용

Project-VOID 코드에서 사용하는 API:
- `POST /auth/verify-id-token`
- `POST /match/commit-result`

---

## 13. Guest에서 일반 계정으로 업그레이드(중요)

권장 순서:

1. Guest 로그인
2. 플레이 진행(데이터 생성)
3. 업그레이드 버튼 클릭
4. `LinkWithEmailAsync` 또는 소셜 Linking 호출
5. 성공 후 UID 동일 여부 확인
6. `IsGuest=false` 전환

왜 Linking을 쓰는가:
- 신규 로그인으로 갈아타면 UID가 바뀌어 데이터 분리 가능성이 큼
- Linking은 UID를 유지한 채 인증수단만 추가

---

## 14. 단계별 테스트(초보자 체크리스트)

1. 이메일 회원가입 성공
2. 이메일 로그인 성공
3. Guest 로그인 성공
4. Guest 프로필 미생성 확인(`players/{uid}` 없음)
5. Lobby 씬 이동 확인
6. 서버에서 ID Token 검증 성공 로그 확인
7. Guest 경기 종료 시 `commit-result` 커밋 스킵 확인
8. Guest -> Email Linking 후 UID 동일 확인

---

## 15. 자주 막히는 문제와 해결

## 15-1. Firebase 모드가 아니라 Offline만 뜸

원인 후보:
1. SDK 미설치
2. 패키지 일부 누락(App/Auth/Firestore)
3. 설정 파일 경로 오류

해결:
1. SDK 설치 상태 재확인
2. `Assets/google-services.json`, `Assets/GoogleService-Info.plist` 위치 재확인
3. 의존성 체크 로그 확인

## 15-2. 이메일 로그인 실패

원인 후보:
1. Authentication에서 Email/Password 미활성화
2. 비밀번호 길이 부족
3. 계정 없음

## 15-3. Google/Apple 버튼 눌렀는데 실패

원인 후보:
1. 토큰 제공자 미등록
2. 플랫폼 SDK 초기화 안 됨
3. 콘솔 OAuth 설정 불일치

## 15-4. Guest 업그레이드 후 데이터가 꼬임

원인 후보:
1. Linking 대신 신규 로그인 처리
2. 충돌(`already-in-use`) 처리 미구현

해결:
1. Linking 우선 정책 준수
2. 충돌 시 서버 병합 API 경로 준비

---

## 16. 운영 전에 반드시 해야 할 것

1. Firebase 프로젝트 분리(dev/stage/prod)
2. Firestore Rules 최소 권한
3. 서버 API URL 주입(verify/commit)
4. 로그에 토큰/개인정보 마스킹
5. 실기기 로그인 테스트(Android/iOS)

---

## 17. “나는 지금 뭘 하면 되나?” 1페이지 요약

1. Firebase 프로젝트 생성
2. Authentication Provider 4종 활성화
- Email/Password, Anonymous, Google, Apple
3. Firestore 생성 + Rules 적용
4. Android/iOS 앱 등록 + 설정 파일 다운로드
5. Unity `Assets/`에 설정 파일 배치
6. Firebase SDK(App/Auth/Firestore) 설치
7. Login 씬에 `AuthServiceInitializer`, `LoginUI` 연결
8. 서버에 `verify-id-token`, `commit-result` URL 주입
9. Guest 로그인 -> 업그레이드 -> UID 유지 테스트

---

## 18. 다음 문서 순서

1. 이 문서(`firebase_auth_beginner_guide.md`) 먼저 읽기
2. 상세 설정 문서(`firebase_auth_full_setup_guide.md`) 진행
3. 루트 계획 문서(`Firebase.md`)로 구현/현황 확인
