# SH Unity Profiler MCP 상태 정리 (2026-04-07)

## 1. 목적
이 문서는 현재 세션에서 진행한 **SH Unity Profiler MCP** 구현 상태를 다음 세션에서도 이어받을 수 있도록 정리한 문서입니다.

핵심 목표는 다음과 같습니다.
- Unity Editor와 Codex를 MCP로 연결한다.
- Codex가 `프로파일러 분석 시작해` 같은 요청으로 **세션 기반 수집**을 시작할 수 있게 한다.
- 사용자가 플레이하는 동안 데이터를 수집하고,
- 이후 그 세션 구간만 따로 분석한다.

---

## 2. 현재 구현된 구조

### 2.1 Unity 쪽 패키지
위치:
- `Packages/com.sh.unity-profiler-mcp/`

핵심 구성:
- Runtime Bridge
- Editor Menu / Editor Window
- Editor Control Bridge
- 선택적 Deep Analysis 구조

주요 파일:
- `Packages/com.sh.unity-profiler-mcp/Runtime/Core/ProfilerRuntimeBridge.cs`
- `Packages/com.sh.unity-profiler-mcp/Runtime/Core/ProfilerRecorderPool.cs`
- `Packages/com.sh.unity-profiler-mcp/Runtime/Config/ProfilerMcpFeatureFlags.cs`
- `Packages/com.sh.unity-profiler-mcp/Editor/ProfilerEditorMenu.cs`
- `Packages/com.sh.unity-profiler-mcp/Editor/ProfilerMcpEditorWindow.cs`
- `Packages/com.sh.unity-profiler-mcp/Editor/ProfilerEditorControlBridge.cs`
- `Packages/com.sh.unity-profiler-mcp/Editor/ProfilerDeepFrameAnalyzer.cs`

### 2.2 Codex MCP 서버
위치:
- `Tools/sh-unity-profiler-analysis-mcp/server.js`

역할:
- stdio MCP 서버로 동작
- Unity 패키지가 남긴 discovery / runtime data / session files 를 읽음
- 세션 시작 / 세션 분석 / stat catalog 조회를 제공

### 2.3 Codex 설정
위치:
- `.codex/config.toml`

등록된 MCP 서버 이름:
- `shUnityProfiler`

---

## 3. 현재 데이터 저장 구조
프로젝트 루트 아래:
- `.sh-unity-profiler-mcp/`

구성:
- `editor-control.json`
  - Unity Editor 제어 브리지 정보
- `editor-command.json`
  - Codex가 에디터에 명령을 전달할 때 사용하는 파일
- `instances/*.json`
  - 활성 runtime target discovery
- `runtime/<target-id>/`
  - 수집 데이터 저장 위치
- `sessions/*.json`
  - 세션 메타데이터

runtime data 예시:
- `snapshot.json`
- `runtime-context.json`
- `deep-analysis-hints.json`
- `latest-analysis-input.json`
- `analysis-window.json`
- `samples.ndjson`
- `stats-catalog.json`
- 가능 시 `deep-frame-analysis.json`

---

## 4. 현재 사용 가능한 MCP 도구
현재 `server.js` 에 구현된 주요 tool:
- `profiler_list_targets`
- `profiler_get_snapshot`
- `profiler_get_runtime_context`
- `profiler_get_deep_analysis_hints`
- `profiler_get_stats_catalog`
- `profiler_begin_analysis`
- `profiler_collect_analysis`
- `profiler_start_watch`
- `profiler_stop_watch`
- `profiler_analyze_runtime_window`

현재 실제로 중요한 도구:

### 4.1 세션 시작
- `profiler_begin_analysis`

역할:
- 새 세션 ID 발급
- 수집 시작 상태로 전환
- 사용자가 플레이하는 동안 데이터를 모을 준비를 함

### 4.2 세션 분석
- `profiler_collect_analysis`

역할:
- 마지막 세션의 시작 시점 이후 구간만 잘라 분석
- 평균/최대/P95 프레임 타임
- 최악 프레임
- CPU-bound 프레임
- 가능하면 deep frame analysis 포함

---

## 5. 현재 확인된 분석 결과 (참고)
이전 세션들에서 확인된 공통 패턴:
- `GamePlay` 씬에서 프레임 드랍 발생
- 주 병목은 **Main Thread CPU**
- Physics는 대부분 매우 낮음
- 메모리는 증가하지만 직접적인 GC 폭증형 병목은 뚜렷하지 않았음

대표 예시:
- Main Thread ≒ Frame Time
- Physics 거의 0ms 수준
- `GamePlay` 진입 후 spike 발생

즉 현재까지는:
> 주 원인은 Physics가 아니라 Main Thread 쪽으로 보임

---

## 6. 중요한 현재 상태
### 6.1 이미 해결된 것
- Codex MCP 등록 완료
- Unity Editor에서 Codex 설정 설치 메뉴 추가
- 프로젝트 로컬 discovery 폴더 사용
- 세션 기반 수집/분석 흐름 추가
- stats catalog 수집 구조 추가
- WSL/Windows 간 localhost 문제를 피하기 위해 파일 기반 흐름으로 전환

### 6.2 아직 미완성/주의점
#### A. Deep Frame Analysis
`ProfilerDeepFrameAnalyzer.cs` 를 추가했지만,
실제로 `deep-frame-analysis.json` 이 안정적으로 생성되는지는 아직 확정되지 않았습니다.

즉:
- 구조는 추가됨
- 하지만 실제 결과 파일 생성은 Unity 리컴파일/실행 상태에 따라 더 점검 필요

#### B. 실시간 부하
초기 구현은 너무 많은 데이터를 너무 자주 저장해서 Unity Editor가 끊겼습니다.
이를 줄이기 위해 다음과 같이 조정했습니다.
- 기본 샘플 간격: `1000ms`
- 대형 JSON 전체 재기록 감소
- `samples.ndjson` append 방식 도입
- 상시 deep frame analyzer 자동 실행 제거

현재도 “프로파일러 전체를 가능한 한 많이 수집”하는 쪽이므로,
실시간 부하는 완전히 0이 아닙니다.

---

## 7. 현재 권장 사용 흐름
### 7.1 Unity에서
- Play Mode 진입
- 필요 시 메뉴 실행:
  - `Tools/SH/Profiler MCP/런타임 브리지 시작`
- 상태 확인:
  - `Tools/SH/Profiler MCP/설정 창 열기`

### 7.2 Codex에서
세션 시작:
- `세션시작해`
- 또는 `분석시작해`

플레이 후 분석:
- `분석해`
- 또는 `이제 분석해`

### 7.3 현재 발급된 최신 세션 예시
최근 세션 ID 예시:
- `project-void-editor-26208-1775554322134`

새 세션을 다시 시작하면 이 값은 계속 바뀝니다.

---

## 8. 다음 세션에서 이어서 해야 할 일
다음 세션에서 우선순위:

### 1순위
- `deep-frame-analysis.json` 이 실제로 생성되는지 확인
- 생성된다면 `profiler_collect_analysis` 응답에 CPU Usage 세부 트리를 함께 해석해 넣기

### 2순위
- `stats-catalog.json` 기반으로 실제 의미 있는 카테고리/상위 stat를 분류
- 단순 dump가 아니라 카테고리별 정리 제공

### 3순위
- 실시간 부하를 더 줄여야 하면
  - 샘플 간격 추가 완화
  - runtime 저장 파일 수 축소
  - deep analysis를 완전 사후처리로 전환

---

## 9. 현재 상태를 다시 확인할 때 볼 파일
### 설정
- `.codex/config.toml`
- `Packages/com.sh.unity-profiler-mcp/Editor/ProfilerEditorMenu.cs`
- `Packages/com.sh.unity-profiler-mcp/Editor/ProfilerMcpEditorWindow.cs`

### 런타임 수집
- `.sh-unity-profiler-mcp/instances/`
- `.sh-unity-profiler-mcp/runtime/`
- `.sh-unity-profiler-mcp/sessions/`

### 서버
- `Tools/sh-unity-profiler-analysis-mcp/server.js`

---

## 10. 한 줄 요약
현재 SH Unity Profiler MCP는:

> **Codex에서 세션 기반으로 Unity 프로파일러 수집을 시작하고, 플레이 구간만 따로 분석할 수 있는 상태까지 구현됨**

하지만:

> **Profiler CPU Usage 세부 트리까지 완전 안정적으로 뽑아내는 deep frame analysis는 아직 추가 점검/고정이 필요함**

