---
description: Kör den gemensamma orkestern med BoosterAIs projektregler
argument-hint: "[område eller issuenummer]"
---

Starta orkestern för området: $ARGUMENTS.

Läs och följ [AGENTS.md](../../AGENTS.md) och [.orchestra/project.md](../../.orchestra/project.md).
Läs därefter den versionslåsta [kärnan](../../.orchestra/shared/ORCHESTRA.md),
[Issueflow-instruktionerna](../../.orchestra/shared/ISSUE-WORKFLOW.md) och
[kösamordningen](../../.orchestra/shared/COORDINATION.md).
Börja med att återuppta ofärdigt arbete i checkouten och worktrees
([Resume unfinished work first](../../.orchestra/shared/ORCHESTRA.md#resume-unfinished-work-first)):
finns det redan på `main` från en annan orkester, kasta det lokalt och ta nästa issue.
Använd [arbetsagentens korta uppdrag](../../.orchestra/shared/WORKER.md) vid delegering.
Svara på svenska. Projektpolicyn anger kön, behörigheter, ansvar, användarfrågor och tidsgränser.
Ställ inga frågor i chatten (inte heller med frågeverktyget): frågor som bara användaren kan besvara går som kommentar på berört issue enligt policyns rad Questions.
