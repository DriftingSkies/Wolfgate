#!/usr/bin/env python3
"""Merge pull requests on top of the checked-out branch before a build, and stamp the build with what merged.

Run by .github/workflows/publish.yml when a workflow_dispatch carries test_merges, a comma-separated list of PR
numbers. Each PR's head is fetched from this repository's refs/pull/N/head and merged with a merge commit; one that
does not merge cleanly is skipped and recorded, and the build goes ahead with the rest, the way a TGS test merge does.

What merged and what did not is written to Resources/Symphony/testmerges.json, which the server package carries and
the Symphony hook reads: it tells players at round start and on joining, and reports it in /status for the panel. The
merged numbers also become a version suffix, written to $GITHUB_OUTPUT as `suffix`, so the CDN version names the set.

Environment: GITHUB_TOKEN and GITHUB_REPOSITORY (owner/repo), both set by the workflow. Standard library only.
"""
import json
import os
import subprocess
import sys
import urllib.error
import urllib.request
from datetime import datetime, timezone

STAMP = os.path.join("Resources", "Symphony", "testmerges.json")

GITHUB_TOKEN = os.environ["GITHUB_TOKEN"]
GITHUB_REPOSITORY = os.environ["GITHUB_REPOSITORY"]


def log(msg):
    print(msg, flush=True)


def git(*args, check=True):
    return subprocess.run(["git", *args], check=check, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                          encoding="utf-8", errors="replace")


def pull_request(number):
    """Title, author and head SHA from the API; None when the PR does not exist or is not open."""
    req = urllib.request.Request(
        f"https://api.github.com/repos/{GITHUB_REPOSITORY}/pulls/{number}",
        headers={"Authorization": f"Bearer {GITHUB_TOKEN}", "X-GitHub-Api-Version": "2022-11-28",
                 "Accept": "application/vnd.github+json", "User-Agent": "wolfgate-test-merge"},
    )
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            data = json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        log(f"PR #{number}: GitHub answered {e.code}")
        return None
    if data.get("state") != "open":
        log(f"PR #{number}: not open ({data.get('state')})")
        return None
    return {
        "number": number,
        "title": data.get("title") or "",
        "author": (data.get("user") or {}).get("login") or "",
        "sha": (data.get("head") or {}).get("sha") or "",
        "url": data.get("html_url") or "",
    }


def parse_numbers(raw):
    out = []
    for part in raw.replace(";", ",").split(","):
        part = part.strip().lstrip("#")
        if not part:
            continue
        if not part.isdigit():
            sys.exit(f"'{part}' is not a pull request number")
        n = int(part)
        if n not in out:
            out.append(n)
    return out


def main():
    numbers = parse_numbers(sys.argv[1] if len(sys.argv) > 1 else os.environ.get("TEST_MERGES", ""))
    base = git("rev-parse", "HEAD").stdout.strip()
    merged, failed = [], []

    # The merge commits need an author; the runner has none configured.
    git("config", "user.name", "Symphony test merge")
    git("config", "user.email", "symphony@users.noreply.github.com")

    for n in numbers:
        pr = pull_request(n)
        if pr is None:
            failed.append({"number": n, "title": "", "reason": "not an open pull request"})
            continue
        fetch = git("fetch", "--no-tags", "origin", f"pull/{n}/head", check=False)
        if fetch.returncode != 0:
            failed.append({"number": n, "title": pr["title"], "reason": "could not fetch the pull request"})
            log(f"PR #{n}: fetch failed\n{fetch.stdout}")
            continue
        merge = git("merge", "--no-ff", "--no-edit", "-m", f"Test merge #{n}: {pr['title']}", "FETCH_HEAD", check=False)
        if merge.returncode != 0:
            git("merge", "--abort", check=False)
            failed.append({"number": n, "title": pr["title"], "reason": "merge conflict"})
            log(f"PR #{n}: did not merge cleanly, skipped\n{merge.stdout}")
            continue
        merged.append(pr)
        log(f"PR #{n}: merged ({pr['sha'][:7]}) {pr['title']}")

    # A PR that moves the engine submodule needs it checked out at the new commit.
    if merged:
        git("submodule", "update", "--init", "--recursive")

    os.makedirs(os.path.dirname(STAMP), exist_ok=True)
    with open(STAMP, "w", encoding="utf-8") as f:
        json.dump({
            "base": base,
            "built_at": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "merged": merged,
            "failed": failed,
        }, f, indent=2)
    log(f"wrote {STAMP}: {len(merged)} merged, {len(failed)} failed")

    suffix = "".join(f"-tm{pr['number']}" for pr in merged)
    with open(os.environ.get("GITHUB_OUTPUT", os.devnull), "a", encoding="utf-8") as out:
        out.write(f"suffix={suffix}\n")
        out.write(f"merged={','.join(str(pr['number']) for pr in merged)}\n")
        out.write(f"failed={','.join(str(pr['number']) for pr in failed)}\n")

    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as s:
            s.write("## Test merges\n\n")
            for pr in merged:
                s.write(f"- merged #{pr['number']} {pr['title']} by {pr['author']} at {pr['sha'][:7]}\n")
            for pr in failed:
                s.write(f"- skipped #{pr['number']} {pr['title']}: {pr['reason']}\n")
            if not merged and not failed:
                s.write("- none requested\n")


if __name__ == "__main__":
    main()
