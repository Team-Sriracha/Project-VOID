# Notion MCP 연결 이슈 해결 정리 (2026-03-06)

## 1. 이슈 요약
- 목적: Codex 환경에서 Notion MCP를 연결해 작업 컨텍스트(노션 페이지/데이터베이스)에 접근.
- 증상: Cursor 설정 방식(`.cursor/mcp.json`)과 Codex 설정 방식을 혼동하여 연결 경로가 일치하지 않음.

## 2. 원인
- 현재 사용 클라이언트는 `Codex CLI`인데, 초기에는 `Cursor` 기준 설정을 시도함.
- Codex는 별도 설정 파일을 직접 편집하기보다 `codex mcp` 명령으로 서버 등록/관리하는 방식이 정석.

## 3. 최종 해결 방법
아래 명령으로 Codex 전역 MCP 서버에 Notion 서버를 등록함.

```bash
codex mcp add notionApi \
  --env NOTION_TOKEN='(발급받은 Notion Integration Token)' \
  -- npx -y @notionhq/notion-mcp-server
```

## 4. 검증 결과
다음 명령으로 등록 상태 확인 가능.

```bash
codex mcp list
codex mcp get notionApi
```

정상 상태 기준:
- 서버 이름: `notionApi`
- 상태: `enabled`
- 명령: `npx -y @notionhq/notion-mcp-server`
- 환경변수: `NOTION_TOKEN` 마스킹 표시

## 5. 운영 가이드
- 토큰 교체(권장):
```bash
codex mcp remove notionApi
codex mcp add notionApi --env NOTION_TOKEN='새 토큰' -- npx -y @notionhq/notion-mcp-server
```

- 서버 삭제:
```bash
codex mcp remove notionApi
```

## 6. 보안 주의사항
- 채팅/로그에 토큰이 노출되었으면 즉시 Notion 개발자 콘솔에서 기존 토큰 폐기 후 재발급 권장.
- 토큰은 문서/코드에 하드코딩하지 말고, MCP 서버 환경변수 방식으로만 관리.

## 7. 참고
- Notion 공식 패키지: `@notionhq/notion-mcp-server`
- 공식 안내에서 장기적으로는 Notion 원격 MCP 사용을 우선 권장하고 있으므로, 추후 필요 시 원격 MCP로 전환 검토.
