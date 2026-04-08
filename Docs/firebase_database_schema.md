# Firebase 데이터베이스 스키마 (정리본)

## 목적
- 소셜 로그인(Google/Apple) 계정의 영구 프로필만 Firestore에 저장합니다.
- 게스트 계정은 Firestore에 저장하지 않습니다.

## Firestore 컬렉션

### 1) `players/{uid}`
소셜 로그인 사용자의 프로필 문서입니다.

저장 필드:
- `Uid` (string)
- `DisplayName` (string)
- `IsDisplayNameConfirmed` (bool)
- `Email` (string)
- `CreatedAtUtc` (DateTime)
- `LastLoginAtUtc` (DateTime)
- `TotalMatches` (int)
- `Wins` (int)
- `Kills` (int)
- `Deaths` (int)

비고:
- 닉네임 미확정 사용자는 `IsDisplayNameConfirmed = false`로 생성됩니다.
- 닉네임 확정 시 `DisplayName`과 `IsDisplayNameConfirmed = true`가 함께 갱신됩니다.

### 2) `displayNames/{normalizedName}`
닉네임 중복 방지용 인덱스 문서입니다.

저장 필드:
- `Uid` (string)
- `CreatedAtUtc` (DateTime)

정규화 규칙:
- `normalizedName = displayName.Trim().ToLowerInvariant()`

## 저장하지 않는 데이터
아래 항목은 Firestore 영구 저장 대상이 아닙니다.
- `GuestId`
- `IsGuest`

해당 값은 게스트 세션/런타임 판단용으로만 사용합니다.
게스트 제한(랭크 차단 등) 판정은 `AuthService.IsAnonymous`를 기준으로 수행합니다.

## 마이그레이션 메모
- 기존 문서에 `GuestId`, `IsGuest`가 남아 있어도 런타임 동작에는 영향을 주지 않습니다.
- 신규/갱신 경로에서는 위 두 필드를 더 이상 쓰지 않습니다.
