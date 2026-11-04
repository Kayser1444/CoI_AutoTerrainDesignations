"""Capture an official API diff and correlate changed types with local source.

Only the official coigame.com Markdown endpoint is fetched. Local imports work
offline, retain their source URL and are labelled as imports, not fresh fetches.
Matches are review leads, never compatibility verdicts.
"""
import argparse
import datetime as dt
import hashlib
import json
from pathlib import Path
import re
import urllib.request

MAINTAINED = ["AutoTerrainDesignations", "AutoForestryDesignations",
              "DesignerToolkit", "KaysersPreIndustrialEra", "CoI_AutoHelpers"]
TYPE = re.compile(r"\b(?:class|struct|interface|enum|delegate)\s+(\w+)")
NAMESPACE = re.compile(r"\bnamespace\s+([\w.]+)")


def changed_types(markdown):
    """Parse each side separately so replaced braces cannot unbalance scopes."""
    records = {}
    for side in ("-", "+"):
        depth, namespace, stack, in_diff = 0, "", [], False
        for line in markdown.splitlines():
            if line.strip() == "```diff":
                depth, namespace, stack, in_diff = 0, "", [], True
                continue
            if line.strip() == "```":
                in_diff = False
                continue
            if not in_diff or not line or line[0] not in (" ", side):
                continue
            code = line[1:]
            ns = NAMESPACE.search(code)
            if ns:
                namespace = ns[1]
            declaration = TYPE.search(code)
            if declaration:
                name = declaration[1]
                # Delegates need no body and cannot contain further declarations.
                qualified = ".".join([namespace] + [t[0] for t in stack] + [name])
                if line[0] == side:
                    record = records.setdefault(qualified, {"name": name, "removed": [], "added": []})
                    record["removed" if side == "-" else "added"].append(code.strip())
                if "{" in code:
                    stack.append((name, depth + 1))
            elif line[0] == side and stack:
                qualified = ".".join([namespace] + [t[0] for t in stack])
                record = records.setdefault(qualified, {"name": stack[-1][0], "removed": [], "added": []})
                record["removed" if side == "-" else "added"].append(code.strip())
            depth += code.count("{") - code.count("}")
            while stack and depth < stack[-1][1]:
                stack.pop()
    return records


def additions_report(records, hits):
    """Index the whole added surface, including APIs unused by maintained mods."""
    additions = {name: record for name, record in records.items() if record["added"]}
    lines = ["# Official API additions for opportunity review", "",
             f"Type scopes with added diff lines: {len(additions)}.", "",
             "Includes unused APIs and replacement signatures, not just new capabilities.",
             "Source-token matches are navigation leads, not verified usage. Check the raw",
             "`official-api.md`, version range and matching decompiled bodies before recommending adoption.", ""]
    if not additions:
        lines += ["No added declarations were detected in this export. This does not establish",
                  "whether a different hotfix range or implementation-only changes offer opportunities."]
    for name, record in sorted(additions.items()):
        projects = sorted({hit["project"] for hit in hits if name in hit["candidates"]})
        lines += [f"## {name}", "",
                  "Current source-token matches: " + (", ".join(projects) or "none") + ".", "",
                  "Added lines:", "```csharp", *record["added"], "```"]
        if record["removed"]:
            lines += ["Removed lines in the same type (check for replacement-only changes):",
                      "```csharp", *record["removed"], "```"]
        lines.append("")
    return "\n".join(lines) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--release", required=True, help="Explicit API release, e.g. 4.3; never inferred from 0.8.8")
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--input", type=Path, help="Previously downloaded official Markdown")
    parser.add_argument("--target-version", help="Expected game version; report any source mismatch")
    parser.add_argument("--project", action="append", choices=MAINTAINED)
    args = parser.parse_args()
    if not re.fullmatch(r"\d+\.\d+", args.release):
        parser.error("release must look like 4.3")
    args.output.mkdir(parents=True, exist_ok=True)
    url = f"https://coigame.com/Modding/Api-Changes/{args.release}/Download"
    now = dt.datetime.now(dt.timezone.utc).isoformat()
    metadata = {"url": url, "release": args.release, "recordedAtUtc": now,
                "status": "unavailable", "method": "local-import" if args.input else "https",
                "expectedTargetVersion": args.target_version}
    try:
        if args.input:
            raw = args.input.read_bytes()
            metadata["importedFrom"] = str(args.input.resolve())
        else:
            request = urllib.request.Request(url, headers={"User-Agent": "CoI-Compatibility-Audit/1.0"})
            with urllib.request.urlopen(request, timeout=25) as response:
                if not response.geturl().startswith("https://coigame.com/"):
                    raise ValueError("unexpected download destination")
                raw = response.read(4_000_001)
        if len(raw) > 4_000_000:
            raise ValueError("API diff exceeds the 4 MB capture limit")
        markdown = raw.decode("utf-8-sig")
        records = changed_types(markdown)
        if not records:
            raise ValueError("response contains no recognizable C# diff types; it may be an error/login page")
        metadata.update(status="captured", sha256=hashlib.sha256(raw).hexdigest(), changedTypes=len(records))
        # The download has no prose version header. Read version constants when present.
        versions = re.findall(r'^([+-]).*?VERSION_FULL\s*=\s*"([^"]+)"', markdown, re.M)
        metadata["fromVersion"] = next((v for s, v in versions if s == "-"), None)
        metadata["toVersion"] = next((v for s, v in versions if s == "+"), None)
        metadata["targetMatches"] = (metadata["toVersion"] == args.target_version) if args.target_version else None
        (args.output / "official-api.md").write_bytes(raw)
        by_name = {}
        for full_name, record in records.items():
            by_name.setdefault(record["name"], []).append(full_name)
        hits = []
        ignored = {"bin", "obj", "external", ".git", "tools", "Unity", "implementations"}
        for project in args.project or MAINTAINED:
            root = args.workspace / project
            source = root / "src"
            if not source.exists():
                continue
            for file in sorted(source.rglob("*.cs")):
                if ignored.intersection(file.relative_to(root).parts):
                    continue
                for number, line in enumerate(file.read_text(encoding="utf-8-sig").splitlines(), 1):
                    names = set(re.findall(r"\b\w+\b", line)).intersection(by_name)
                    for name in sorted(names):
                        hits.append({"project": project, "file": file.relative_to(root).as_posix(),
                                     "line": number, "token": name, "candidates": by_name[name]})
        (args.output / "official-api-impact.json").write_text(json.dumps(
            {"metadata": metadata, "types": records, "sourceLeads": hits}, indent=2), encoding="utf-8")
        (args.output / "official-api-additions.md").write_text(additions_report(records, hits), encoding="utf-8")
        lines = ["## Official API change cross-check", "", f"Source: [{args.release} Markdown diff]({url})",
                 f"Capture: `{metadata['method']}` at `{now}`; SHA-256 `{metadata['sha256']}`.", "",
                 f"Published version constants: `{metadata['fromVersion']}` → `{metadata['toVersion']}`.",
                 f"Changed type scopes: {len(records)}; source-token leads: {len(hits)}.", ""]
        if metadata["targetMatches"] is False:
            lines += ["**Version mismatch:** this published range does not end at the requested installed version.", ""]
        lines += ["Token matches include comments, ambiguous names and inactive version branches. They require",
                  "qualified-type, overload and behavior review; no match is not proof of compatibility.", "",
                  "| Project | Changed API type candidates used in source |", "|---|---|"]
        for project in args.project or MAINTAINED:
            matched = sorted({full for hit in hits if hit["project"] == project for full in hit["candidates"]})
            lines.append(f"| {project} | " + ", ".join(f"`{name}`" for name in matched) + " |")
        lines += ["", "Exact signature additions/removals and file/line leads are in `official-api-impact.json`.",
                  "[All added API candidates](official-api-additions.md) include types with no current source matches; opportunity analysis is separate.",
                  "Inspect decompiled bodies/private members and run builds, binary checks and runtime tests separately."]
    except (OSError, ValueError) as error:
        metadata["error"] = str(error)
        lines = ["## Official API change cross-check", "", f"Source: [{args.release}]({url})", "",
                 f"**Unavailable:** {error}", "", "Continue local checks and report this evidence gap. A missing page is not a clean result."]
    (args.output / "official-api-source.json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")
    (args.output / "official-api-impact.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"Official API notes: {metadata['status']}; {args.output}")
    return 0 if metadata["status"] == "captured" else 2


if __name__ == "__main__":
    raise SystemExit(main())
