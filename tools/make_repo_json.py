#!/usr/bin/env python3
"""Write the Dalamud custom-repository file (repo.json) for a release.

usage: VERSION=0.1.0.0 TAG=v0.1.0 REPO=owner/name python3 make_repo_json.py <plugin manifest .json> <out repo.json>

Players add https://raw.githubusercontent.com/<owner>/<name>/<branch>/repo.json under
/xlsettings -> Experimental -> Custom Plugin Repositories, then install from /xlplugins.
"""
import json, os, sys, time

manifest_path, out_path = sys.argv[1], sys.argv[2]
version, tag, repo = os.environ["VERSION"], os.environ["TAG"], os.environ["REPO"]

with open(manifest_path, encoding="utf-8-sig") as f:
    m = json.load(f)

download = f"https://github.com/{repo}/releases/download/{tag}/latest.zip"
entry = {
    "Author": m.get("Author", ""),
    "Name": m.get("Name", "FLStats Plus"),
    "InternalName": m.get("InternalName", "FLStatsPlus"),
    "AssemblyVersion": version,
    "TestingAssemblyVersion": version,
    "Punchline": m.get("Punchline", ""),
    "Description": m.get("Description", ""),
    "Changelog": m.get("Changelog", ""),
    "Tags": m.get("Tags", []),
    "RepoUrl": f"https://github.com/{repo}",
    "ApplicableVersion": "any",
    "DalamudApiLevel": 15,
    "TestingDalamudApiLevel": 15,
    "IsHide": False,
    "IsTestingExclusive": False,
    "DownloadLinkInstall": download,
    "DownloadLinkTesting": download,
    "DownloadLinkUpdate": download,
    "LastUpdate": int(time.time()),
}
if m.get("IconUrl"):
    entry["IconUrl"] = m["IconUrl"]

with open(out_path, "w", encoding="utf-8") as f:
    json.dump([entry], f, indent=2)
print(f"wrote {out_path} for {entry['InternalName']} {version} -> {download}")
