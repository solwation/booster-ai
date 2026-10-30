#!/usr/bin/env bash
# Writes the GitHub release body for a tag to stdout (used by .github/workflows/release.yml).
#   tools/release-notes.sh v0.14.0 [previous-tag]
# Needs the tag's history (fetch-depth: 0 / tags) and .github/release-summary.txt (SUMMARY_FILE overrides the
# path) with `version:`, `sv:` and `en:` lines. For a final release a missing, stale or empty summary is an
# error; a pre-release (tag with -suffix) may omit it. The app reads the <!-- summary:xx: --> markers.
set -euo pipefail
cd "$(dirname "$0")/.."

tag=${1:?usage: release-notes.sh vX.Y.Z [previous-tag]}
prev=${2:-$(git describe --tags --abbrev=0 --match 'v[0-9]*' --exclude '*-*' "$tag^" 2>/dev/null || true)}
file=${SUMMARY_FILE:-.github/release-summary.txt}
pre=0; case "$tag" in *-*) pre=1 ;; esac

field() { [ -f "$file" ] || return 0; sed -n "s/^$1:[[:space:]]*//p" "$file" | head -n1 | sed 's/[[:space:]]*$//'; }
fail() { echo "::error file=$file::$*" >&2; exit 1; }

ver=$(field version); sv=$(field sv); en=$(field en)
if [ "$pre" = 0 ]; then
  [ -f "$file" ] || fail "$file is missing: write the release's one-line summary (sv and en) before tagging"
  [ "$ver" = "${tag#v}" ] || fail "$file is for version '${ver:-?}', not ${tag#v}: update it before tagging"
  [ -n "$sv" ] && [ -n "$en" ] || fail "$file needs non-empty sv: and en: lines"
fi
# A marker must stay one HTML comment line: no --> inside.
for v in "$sv" "$en"; do case "$v" in *-\-\>*|*\<\!--*) fail "the summary must not contain comment markers" ;; esac; done

if [ -n "$sv$en" ]; then
  [ -n "$sv" ] && echo "<!-- summary:sv: $sv -->"
  [ -n "$en" ] && echo "<!-- summary:en: $en -->"
  echo
  [ -n "$en" ] && echo "$en"
  [ -n "$sv" ] && echo "$sv"
  echo
fi

echo "## Changes"
echo
range=${prev:+$prev..}$tag
git log --no-merges --format='%s' "$range" | grep -v -E '^Version [^ ]+ after the v[^ ]+ release$' | sed 's/^/- /' || true
echo
[ -n "$prev" ] && echo "**Full Changelog**: https://github.com/solwation/booster-ai/compare/$prev...$tag"
exit 0
