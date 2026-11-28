#!/usr/bin/env python3
"""Fail-closed Opsis 3.0 release/upgrade gate; Python stdlib only.

Compares all 205 source-verified public auto-property keys of release 2.8.0.2510
with the current source. Does not modify files, registry, Windows or SSD config.
"""
from __future__ import annotations
import json
import os
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
BASE = ROOT / "docs/OPSIS-2.8.0.2510-SETTINGS-BASELINE.json"
DELTA = ROOT / "docs/OPSIS-3.0.0.3200-SETTINGS-DELTA.json"
GATES = ROOT / "release/3.0.0.3200-readiness.json"
VERSION = ROOT / "Version.props"
CLASS_PATTERN = re.compile(r"^\s*public\s+(?:sealed\s+)?class\s+(\w+)\b")
PROPERTY_PATTERN = re.compile(r"^\s*public\s+(.+?)\s+(\w+)\s*\{\s*get;\s*set;\s*\}")

def load(path: pathlib.Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8-sig"))

def current_keys(baseline: dict) -> dict[str, dict]:
    all_keys: dict[str, dict] = {}
    for path in sorted(set(p["source"] for p in baseline["properties"])):
        lines = (ROOT / path).read_text(encoding="utf-8-sig").splitlines()
        owners = {p["owner"] for p in baseline["properties"] if p["source"] == path}
        for owner in owners:
            start = next((i for i, line in enumerate(lines)
                          if (m := CLASS_PATTERN.match(line)) and m.group(1) == owner), -1)
            if start < 0:
                raise ValueError(f"Baseline class {owner} missing from {path}")
            stop = next((i for i in range(start + 1, len(lines))
                         if CLASS_PATTERN.match(lines[i])), len(lines))
            for index in range(start, stop):
                match = PROPERTY_PATTERN.match(lines[index])
                if match is None:
                    continue
                key = f"{owner}.{match.group(2)}"
                if key in all_keys:
                    raise ValueError(f"Duplicate current property: {key}")
                all_keys[key] = {
                    "owner": owner, "name": match.group(2),
                    "type": match.group(1).strip(), "source": path, "line": index + 1
                }
    return all_keys

def check() -> None:
    baseline, delta, readiness = load(BASE), load(DELTA), load(GATES)
    if baseline["baseline"] != "2.8.0.2510" or readiness["targetVersion"] != "3.0.0.3200":
        raise ValueError("Baseline or target version identity mismatch")
    before = {f'{p["owner"]}.{p["name"]}': p for p in baseline["properties"]}
    if len(before) != baseline["unique_property_keys"] or baseline["duplicate_property_keys"]:
        raise ValueError("Baseline contains duplicate property keys")
    now = current_keys(baseline)
    missing = sorted(set(before) - set(now))
    changed = sorted(k for k in set(now) & set(before)
                     if now[k]["type"] != before[k]["type"])
    new = sorted(set(now) - set(before))
    documented_new = sorted(p["owner"] + "." + p["name"] for p in delta["additions"])
    if missing or changed:
        raise ValueError(f"BREAKING 2.8 settings migration: removed={missing}, retyped={changed}")
    if new != documented_new:
        raise ValueError(f"Settings inventory must be refreshed: new={new}, documented={documented_new}")
    if delta["baseline_properties"] != len(before) or delta["current_properties"] != len(now):
        raise ValueError("Configuration delta counts are outdated")
    if delta["missing"] or delta["typeChanges"] or delta["duplicate_count"]:
        raise ValueError("Configuration delta reports a release-blocking regression")
    version_text = VERSION.read_text(encoding="utf-8-sig")
    match = re.search(r"<OpsisAppVersion>\s*([^<]+?)\s*</OpsisAppVersion>", version_text)
    if not match:
        raise ValueError("Version.props missing canonical OpsisAppVersion")
    current_version = match.group(1).strip()
    if current_version not in ("2.8.0.2510", "3.0.0.3200"):
        raise ValueError(f"Unsupported version stamp: {current_version}")
    if current_version == "3.0.0.3200":
        incomplete = [p["id"] for p in readiness["gates"] if p["status"] != "passed"]
        rc2 = (os.environ.get("OPSIS_RC_VALIDATION") == "RC2"
               and readiness.get("channel") == "RC2"
               and readiness.get("state") == "rc-validation"
               and readiness.get("releaseBlocked") is True
               and readiness.get("requireAllGatesPassed") is True)
        if rc2:
            print("RC2 validation only; production release remains blocked")
        elif readiness["releaseBlocked"] or readiness["state"] != "ready" or incomplete:
            raise ValueError(f"3.0 release stamp blocked until all gates pass: {incomplete}")
    elif readiness["state"] == "ready" and not readiness["releaseBlocked"]:
        raise ValueError("Ready release must use 3.0.0.3200 canonical version")
    print(f"PASS baseline 2.8: {len(before)} properties retained, {len(new)} additive, "
          f"0 removed/retyped; identity={current_version}, target=3.0.0.3200")
    print(f"PASS release gate: {sum(x['status'] == 'passed' for x in readiness['gates'])}/"
          f"{len(readiness['gates'])} criteria passed; premature release stamp prevented")

if __name__ == "__main__":
    try:
        check()
    except (OSError, ValueError, TypeError, KeyError, json.JSONDecodeError) as exc:
        print(f"FAIL Opsis integration baseline/release gate: {exc}", file=sys.stderr)
        raise SystemExit(1)
