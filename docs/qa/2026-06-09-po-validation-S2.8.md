# PO Validation Report — Story 2.8 (F5+: Expansão agêntica do chatbot — Fase A)

> **Agent:** Pax (@po) · **Date:** 2026-06-09 · **Mode:** YOLO (autonomous)
> **Task:** `validate-next-story.md` · **Checklist:** 10-point story-draft validation (`story-lifecycle.md`)
> **Target:** `docs/stories/2.8.story.md` (Status at validation start: Draft)
> **Sources of truth read:** ADE-006, ADE-002, EPIC-002, Story 2.5 (molde, Done), and real code in `src/Fifa2026.V2.McpServer/` + migration `2026-05-08-group-stage-72.sql`.

---

## Verdict

**GO — Implementation Readiness Score: 9/10 — Confidence: High**

Story 2.8 is a clean additive extension of the validated S2.5 pattern, fully traceable to ADE-006 (Catálogo Fase A) and ADE-002 invariants. Every AC maps to source documents or real code; no invention detected. Three should-fix items were applied directly to the story (file-name accuracy, branch decision, classification-empty behavior). One minor watch-item documented. No critical blockers.

---

## 10-Point Checklist

| # | Criterion | Verdict | Notes |
|---|-----------|---------|-------|
| 1 | Clear and objective title | ✅ PASS | Title names phase, scope (4 read-only tools), and bound (Fase A). |
| 2 | Complete description (problem/need) | ✅ PASS | User story + canonical questions + "no hallucination / no write vector" rationale. |
| 3 | Testable acceptance criteria | ✅ PASS | 12 ACs, each with verifiable conditions (tool attrs, `tools/list`=7, test count ≥49, smoke questions). |
| 4 | Well-defined scope (IN and OUT) | ✅ PASS | Explicit "Fora do escopo" excludes Fase B/C, gateway/front/Functions changes, `standings` table. |
| 5 | Dependencies mapped | ✅ PASS | Depends on S2.5 (verified **Done**); ADE-006/ADE-002 cited; branch decision now recorded (applied). |
| 6 | Complexity estimate | ✅ PASS | MEDIUM, justified (established pattern; aggregation query = hardest point). |
| 7 | Business value | ✅ PASS | "Explore the whole World Cup system" — pedagogical + product value clear. |
| 8 | Risks documented | ✅ PASS | Troubleshooting table + 6 gotchas + SM handoff risks now resolved in-story. |
| 9 | Criteria of Done | ✅ PASS | AC-5/7/9/10/12 give concrete done definitions (count, smoke, golden rule). |
| 10 | Alignment with PRD/Epic/ADE | ✅ PASS | Exact match to ADE-006 Catálogo Fase A (4 tools, `consultar_resultados` consolidated) and ADE-002 SDK invariants. |

**Score: 10/10 criteria PASS → readiness 9/10** (1 point withheld for the residual frontend-path/test-conflict watch-items, both non-blocking).

---

## Template Compliance

All template sections present: Executor Assignment, Story, AC, Out-of-scope, CodeRabbit Integration, Tasks/Subtasks, Dev Notes, Troubleshooting, Testing, Change Log, Dev Agent Record, QA Results. No unfilled placeholders.

**Executor Assignment validation (Step 1.1):**
- `executor: @dev`, `quality_gate: @architect`, `quality_gate_tools` non-empty array ✓
- `executor != quality_gate` ✓
- Code/Feature work → @dev executor + @architect gate = consistent ✓

**CodeRabbit Integration (Step 8):** `coderabbit_integration.enabled: true` in core-config.yaml → section validated. Story type (API+Integration / Security / Documentation), complexity (MEDIUM), primary @dev + supporting agents, quality gates (Pre-Commit/Pre-PR/Pre-Deployment), self-healing (light, 2 iter), focus areas (SQL parametrizado, ReadOnly=true, `= null` defaults) — all present and accurate. **PASS.**

---

## Anti-Hallucination Verification (Constitution Art. IV — No Invention)

Every technical claim cross-checked against real code/migrations:

| Claim in story | Source verified | Result |
|---|---|---|
| `stage = 'Fase de Grupos'` (accent/spaces) | `2026-05-08-group-stage-72.sql` lines 8+ | ✅ Real |
| `round_of_32/16`, `quarter_final`, `semi_final`, `third_place`, `final` | `MapRodadaToStage` in `FifaQueryRepository.cs` L200-236 + BracketStageMappingTests | ✅ Real |
| No `standings` table → aggregation | Schema in ADE-006 + repo (only SELECT) | ✅ Honest |
| Tools pattern (DI params, `EntraOidContext.GetMaskedOidForLog()`, `[McpServerTool(ReadOnly=true)]`) | `FifaTickerTools.cs` L23-88 | ✅ Real |
| `WithToolsFromAssembly()` auto-discovery, stateless `/mcp` | `Program.cs` L48-55, L67 | ✅ Real |
| Parametrized SQL via `CommandDefinition`, no concatenation | `FifaQueryRepository.cs` all methods | ✅ Real |
| `CategoryLabelMapper` real labels `VIP Premium`/`Categoria 1`/`Categoria 2` (M-1) | ADE-002 Decision C + repo L80-83 | ✅ Real |
| SDK pinned 1.4.0; `= null` default bug | ADE-002 Inv 1 + Inv 6 (gate S2.5) | ✅ Real |
| Baseline 41 tests → ≥49 | Counted: 16+9+5+4+4+2+1 = **41** runnable | ✅ Accurate |
| gateway cache GET-only (commit `e5451ce`) | git log + AC-9 | ✅ Real |
| `gemini.ts` model `gemini-2.5-flash` (commit `9c9a549`) | `Lovable/World Cup Tickets Hub/src/lib/llm/gemini.ts` L24 | ✅ **Already fixed** |

**No invented tables, columns, libraries, or patterns. Art. IV: PASS.**

---

## SM Handoff Risks — PO Adjudication

**Risk 1 — Classification query for groups with no matches played (Task 3.2 ambiguity):**
- **[AUTO-DECISION]** Required behavior → **Option (a): return empty list** when no group match has `home_score`. Reason: it is the truthful aggregation result (no data = no standings), matches ADE-006 "Honestidade de schema", avoids fabricating a synthetic 0-pts row set, and the `[Description]` + chatbot already surface "ainda sem jogos disputados" (AC-10). Option (b) would require a second query inventing rows the aggregation doesn't produce. **Applied to story:** AC-2 and Task 3.2 now mandate (a) as the required behavior (option (b) removed as a permitted alternative).

**Risk 2 — `MapFaseToStage` vs `MapRodadaToStage` duplication:**
- **Adjudication:** Real `MapRodadaToStage` (L200) already covers all knockout stages. The correct, low-duplication approach is `MapFaseToStage` delegating to `MapRodadaToStage` and adding only the `'Fase de Grupos'` case. **Applied:** Dev Notes Gotcha 1 already prescribes this; added an explicit note to AC-1 to reuse-not-duplicate. Watch-item: existing `BracketStageMappingTests.Returns_null_for_unknown_or_empty` asserts `"grupo A" → null` for **`MapRodadaToStage`** — that test must stay valid (new "grupos" mapping lives in `MapFaseToStage`, not in `MapRodadaToStage`). Documented in story so @dev does not break the existing test.

**Risk 3 — Branch (orchestrator decision by Orion):**
- **Applied:** Recorded in Dev Notes that implementation occurs on the current branch `phase-06-flow-visualizer` (cumulative epic TIP; S2.5 fixes already committed there; a new phase branch would break cumulativity). Ambiguity eliminated.

**Risk 4 — Task 7.1 (gemini-2.5-flash) — hard AC or verification task?:**
- **[AUTO-DECISION]** Keep as **verification task**, not a hard AC. Reason: verified `gemini.ts` L24 already reads `gemini-2.5-flash` (fix `9c9a549` landed). Promoting to a hard AC would create a redundant gate on already-correct code. AC-10's note ("confirm `gemini.ts` updated before smoke") is sufficient. No change needed beyond the note already present.

---

## Fixes Applied to Story (should-fix, within @po authority)

1. **File-name accuracy (anti-hallucination):** AC-1 and Tasks referenced `FifaTicketTools.cs`, but the real file is `FifaTickerTools.cs` (typo "Ticker" in repo) with class `FifaTicketTools`; the **test** file is `FifaTicketToolsTests.cs`. Corrected references so @dev edits the right file. (Dev Notes already had the correct `FifaTickerTools.cs`.)
2. **Risk 1 resolution:** AC-2 + Task 3.2 now require empty-list behavior (option a) — ambiguity removed.
3. **Risk 3 resolution:** branch `phase-06-flow-visualizer` recorded in Dev Notes.

---

## Critical Issues (Must Fix — Story Blocked)

**None.**

## Should-Fix Issues

All identified should-fix items were applied during validation (see "Fixes Applied"). None remain open.

## Nice-to-Have / Watch-items (non-blocking)

- **DTO field-name consistency:** existing `BracketMatchResult` uses `jogo`; new `MatchResult` proposes `partida`. Both valid (different tools); not a conflict, but @dev should keep PT-BR `[JsonPropertyName]` consistent within each DTO.
- **`MapRodadaToStage` test invariant** must remain green (see Risk 2).

---

## Final Assessment

- **Decision:** **GO**
- **Implementation Readiness Score:** **9/10**
- **Confidence Level:** **High**
- **Status transition:** Draft → **Ready** (applied in story file + Change Log per Step 12)
- **Next:** `@dev *develop 2.8` on branch `phase-06-flow-visualizer`.
