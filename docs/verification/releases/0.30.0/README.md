# BoosterAI 0.30.0 release verification

- Tag `v0.30.0` points to `4417290`; the release summary was committed before tagging.
- [Release workflow](https://github.com/solwation/booster-ai/actions/runs/42050182862)
  passed: **760 tests**, 0 failures/skips, both packages and release publication.
- Downloaded the published Windows ZIP and Linux tar.gz; `sha256sum -c SHA256SUMS`
  reports OK for both. The downloaded [checksums](SHA256SUMS) are retained here.
- GitHub `releases/latest` reports `v0.30.0`, not a draft or prerelease, with the
  two archives and SHA256SUMS. Both localized summary markers are present.
- The local default version was bumped to0.30.0 afterward in `032e235`; this is
  version metadata only. All functional source is covered by the release tag.
- The session review covered `e22ff14..f107de5`: combined implementation,
  acceptance/evidence, current README/docs/strings, ownership/worktrees/processes
  and installed Windows build. The conductor and a bounded read-only helper found
  no concrete integration defects. No review issues were created.

Per-issue verification is under104–113. In particular, [113](../../113/README.md)
records one-dialog alias replacement, cross-platform tests, cancellation/write
checks and the fresh isolated real Windows Terminal marker; [111](../../111/README.md)
records keyboard/hover/status/output lifecycle. Historical104 evidence describes
its original conservative behavior, which the later explicit request113 replaces.

Final local-version Release build passed with zero warnings/errors; self-contained
publish succeeded with the existing IL3000 warning. Main screenshots were regenerated
for0.30.0 and inspected. Normal installation delivered `0.30.0+032e235`: one new
installed background process, old process exited, unchanged focus, restored80% zoom
(font9.6) and matching published/installed executable hashes. See
[installation.json](installation.json). The release tests above apply to the tag;
only version metadata and evidence/documentation follow it, with no untagged
functional product change.
