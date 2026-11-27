#!/usr/bin/env python3
"""Keeps docs/releases.json, the data behind the release page docs/releases.html (#245), in step with the tags.

  tools/release-page.py update vX.Y.Z [previous-tag]  add or refresh the entry of a release (before or after tagging)
  tools/release-page.py check vX.Y.Z [previous-tag]   fail when the entry for vX.Y.Z is missing or stale (release.yml)
  tools/release-page.py rebuild                       refresh every final release from its tag

An entry holds the version, date, the sv/en summary and the notable features of .github/release-summary.txt
(SUMMARY_FILE overrides the path; for rebuild the file as it was at each tag) and the commit subjects since the
previous final tag, without the version bumps, release summary and subtree squash commits. Features already in
the JSON are kept when the summary file has none, so hand-written backfills survive a rebuild. Pre-releases
(tags with a -suffix) are not listed. Feature blocks in the summary file:

  feature: 245                      issue number (may be empty); starts a new feature
  title.sv: / title.en:             short heading
  howto.sv: / howto.en:             how to use it, one line each
  image: releases/0.55.0/page.png   screenshot, relative to docs/ (image.sv: / image.en: per language)
  docs: DETAILS.md#updates          docs/ file and anchor with more detail
  topic: tab-splash                 optional id that groups features about the same thing across releases
  topictitle.sv: / topictitle.en:   optional heading of the merged card of that topic

A feature without a topic in the summary file keeps the topic (and topicTitle) of the feature with the same
issue already in the JSON, so a rebuild does not drop hand-written grouping. How the page merges topics is
described under "Release page" in docs/DEVELOPMENT.md.
"""
import datetime
import json
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, "docs", "releases.json")
SUMMARY = os.environ.get("SUMMARY_FILE") or os.path.join(ROOT, ".github", "release-summary.txt")
SKIP = re.compile(r"^(Version \S+ after the v\S+ release|Release summary for v\S+|Squashed '[^']+' (changes from|content from) .*)$")
FEATURE_KEYS = ("title.sv", "title.en", "howto.sv", "howto.en", "image", "image.sv", "image.en", "docs",
                "topic", "topictitle.sv", "topictitle.en")
TOPIC = re.compile(r"^[a-z0-9][a-z0-9-]*$")


def git(*args, check=True):
    r = subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True)
    if check and r.returncode != 0:
        sys.exit(f"git {' '.join(args)}: {r.stderr.strip()}")
    return r.stdout if r.returncode == 0 else None


def version_key(v):
    core, _, suffix = v.partition("-")
    nums = [int(x) for x in core.split(".")]
    return (nums, suffix == "", suffix)


def fail(msg, file=DATA):
    print(f"::error file={os.path.relpath(file, ROOT)}::{msg}", file=sys.stderr)
    sys.exit(1)


def parse_summary(text):
    """version, {sv, en} and the feature list of a release-summary.txt."""
    version, summary, features, cur = None, {}, [], None
    for line in (text or "").splitlines():
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        key, sep, value = line.partition(":")
        if not sep:
            continue
        key, value = key.strip(), value.strip()
        if key == "version" and version is None:
            version = value
        elif key in ("sv", "en") and key not in summary:
            summary[key] = value
        elif key == "feature":
            cur = {"issue": int(value.lstrip("#"))} if value.lstrip("#").isdigit() else {}
            features.append(cur)
        elif key in FEATURE_KEYS and cur is not None:
            group, _, lang = key.partition(".")
            if group == "image" and not lang:
                lang = "*"
            if lang:
                cur.setdefault(group, {})[lang] = value
            else:
                cur[group] = value
    for f in features:
        # image becomes {"sv": ..., "en": ...}: a plain image: fills the languages without their own.
        img = f.pop("image", {})
        sv_img = img.get("sv") or img.get("*") or img.get("en")
        en_img = img.get("en") or img.get("*") or img.get("sv")
        if sv_img:
            f["image"] = {"sv": sv_img, "en": en_img}
        if "topictitle" in f:
            f["topicTitle"] = f.pop("topictitle")
        if "topic" in f and not TOPIC.match(f["topic"]):
            fail(f"topic {f['topic']!r} must be lower-case letters, digits and dashes", SUMMARY)
    for v in summary.values():
        if "-->" in v or "<!--" in v:
            fail("the summary must not contain comment markers", SUMMARY)
    return version, summary, features


def previous_tag(ref):
    out = git("describe", "--tags", "--abbrev=0", "--match", "v[0-9]*", "--exclude", "*-*", ref, check=False)
    return out.strip() if out else None


def build_entry(tag, prev=None, summary_text=None, old=None):
    version = tag[1:] if tag.startswith("v") else tag
    tagged = git("rev-parse", "-q", "--verify", f"refs/tags/{tag}", check=False) is not None
    ref = tag if tagged else "HEAD"
    if prev is None:
        # The previous final tag before the release commit (HEAD itself when the tag does not exist yet).
        prev = previous_tag(f"{ref}^" if tagged else "HEAD")
        if prev == tag:
            prev = previous_tag(f"{tag}^")
    rng = f"{prev}..{ref}" if prev else ref
    subjects = [s for s in git("log", "--no-merges", "--format=%s", rng).splitlines() if s and not SKIP.match(s)]
    date = git("log", "-1", "--format=%cs", ref).strip() if tagged else datetime.date.today().isoformat()
    sv, summary, features = parse_summary(summary_text)
    old = old or {}
    if sv == version and features:
        inherit_topics(features, old.get("features", []))
    entry = {
        "version": version,
        "date": date,
        "previous": prev[1:] if prev else None,
        "summary": summary if sv == version and summary else old.get("summary", {}),
        "features": features if sv == version and features else old.get("features", []),
        "changes": subjects,
    }
    return entry


def inherit_topics(features, old_features):
    """Give a feature without a topic the topic of the old feature with the same issue."""
    by_issue = {f["issue"]: f for f in old_features if "issue" in f and f.get("topic")}
    for f in features:
        o = by_issue.get(f.get("issue"))
        if o and "topic" not in f:
            f["topic"] = o["topic"]
            if "topicTitle" in o and "topicTitle" not in f:
                f["topicTitle"] = o["topicTitle"]


def load():
    if not os.path.exists(DATA):
        return []
    with open(DATA, encoding="utf-8") as f:
        return json.load(f).get("releases", [])


def save(releases):
    releases = sorted(releases, key=lambda r: version_key(r["version"]), reverse=True)
    with open(DATA, "w", encoding="utf-8") as f:
        json.dump({"releases": releases}, f, ensure_ascii=False, indent=2)
        f.write("\n")


def check_images(entry):
    for feat in entry.get("features", []):
        for path in set((feat.get("image") or {}).values()):
            if path and not os.path.isfile(os.path.join(ROOT, "docs", path)):
                fail(f"v{entry['version']}: feature image docs/{path} is missing")


def read_summary():
    if not os.path.exists(SUMMARY):
        return None
    with open(SUMMARY, encoding="utf-8") as f:
        return f.read()


def main(argv):
    if not argv or argv[0] not in ("update", "check", "rebuild"):
        sys.exit(__doc__)
    cmd = argv[0]
    releases = load()
    by_version = {r["version"]: r for r in releases}
    if cmd == "rebuild":
        tags = [t for t in git("tag", "-l", "v[0-9]*").split() if "-" not in t]
        for tag in tags:
            text = git("show", f"{tag}:.github/release-summary.txt", check=False)
            by_version[tag[1:]] = build_entry(tag, summary_text=text, old=by_version.get(tag[1:]))
        for e in by_version.values():
            check_images(e)
        save(list(by_version.values()))
        print(f"{os.path.relpath(DATA, ROOT)}: {len(by_version)} releases")
        return
    if len(argv) < 2:
        sys.exit(__doc__)
    tag = argv[1] if argv[1].startswith("v") else "v" + argv[1]
    prev = argv[2] if len(argv) > 2 else None
    if "-" in tag:
        print(f"{tag} is a pre-release: not listed on the release page")
        return
    entry = build_entry(tag, prev, read_summary(), by_version.get(tag[1:]))
    if cmd == "update":
        check_images(entry)
        by_version[entry["version"]] = entry
        save(list(by_version.values()))
        print(f"{os.path.relpath(DATA, ROOT)}: v{entry['version']} with {len(entry['changes'])} changes, "
              f"{len(entry['features'])} features")
        return
    have = by_version.get(entry["version"])
    if have is None:
        fail(f"v{entry['version']} is missing: run tools/release-page.py update {tag} and commit docs/releases.json before tagging")
    if not entry["summary"].get("sv") or not entry["summary"].get("en"):
        fail(f"v{entry['version']} has no sv/en summary", SUMMARY)
    for key in ("summary", "features", "changes"):
        if have.get(key) != entry[key]:
            fail(f"v{entry['version']}'s {key} is stale: run tools/release-page.py update {tag} and commit docs/releases.json")
    check_images(have)
    print(f"{os.path.relpath(DATA, ROOT)}: v{entry['version']} is current")


if __name__ == "__main__":
    main(sys.argv[1:])
