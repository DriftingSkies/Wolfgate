#!/usr/bin/env python3
"""
Posts a Discord embed for pull request events: opened, reopened, ready for review, merged, closed.

Runs from .github/workflows/discord-pr-notify.yml. Two modes:

  * pull_request_target event: reads the event payload from GITHUB_EVENT_PATH.
  * workflow_dispatch: fetches the PR given by PR_NUMBER through the GitHub API
    (needs GITHUB_TOKEN and GITHUB_REPOSITORY) so the webhook can be tested by hand.

Environment:
  DISCORD_WEBHOOK_URL  Discord webhook to post to. If unset the script does nothing and exits 0.
  DRY_RUN=1            Print the payload instead of sending it.

Standard library only, so no pip install is needed on the runner.
"""

import json
import os
import re
import sys
import urllib.error
import urllib.request

GITHUB_API_URL = os.environ.get("GITHUB_API_URL", "https://api.github.com")
DISCORD_WEBHOOK_URL = os.environ.get("DISCORD_WEBHOOK_URL")
DRY_RUN = os.environ.get("DRY_RUN") == "1"
USER_AGENT = "Wolfgate-PR-Notify (+https://github.com/Aphelion-Moon/Wolfgate)"

# https://discord.com/developers/docs/resources/message#embed-object-embed-limits
TITLE_LIMIT = 256
FIELD_VALUE_LIMIT = 1024
# Hard limit is 4096; keep the embed a summary rather than the whole PR body.
DESCRIPTION_LIMIT = 600

# GitHub's own state colours.
COLOR_OPENED = 0x2DA44E  # green
COLOR_DRAFT = 0x6E7781  # grey
COLOR_READY = 0x0969DA  # blue
COLOR_MERGED = 0x8250DF  # purple
COLOR_CLOSED = 0xCF222E  # red


def main() -> int:
    event = load_event()
    if event is None:
        print("No pull request in event and no PR_NUMBER given, nothing to do")
        return 0

    pr = event["pull_request"]
    action = event.get("action")
    verb, color = describe(action, pr)
    if verb is None:
        print(f"Ignoring action {action!r}")
        return 0

    actor = event.get("sender") or pr.get("user") or {}
    if action == "closed" and pr.get("merged") and pr.get("merged_by"):
        actor = pr["merged_by"]

    payload = build_payload(verb, color, pr, actor)

    if DRY_RUN:
        # ensure_ascii keeps this printable on consoles that aren't UTF-8.
        print(json.dumps(payload, indent=2))
        return 0

    if not DISCORD_WEBHOOK_URL:
        print("No discord webhook URL found, skipping discord send")
        return 0

    return send(payload)


def load_event():
    """Return a pull_request-shaped event dict, from the event file or the API."""
    event = {}
    event_path = os.environ.get("GITHUB_EVENT_PATH")
    if event_path and os.path.exists(event_path):
        with open(event_path, encoding="utf-8") as f:
            event = json.load(f)

    if event.get("pull_request"):
        return event

    # workflow_dispatch: synthesise an event from the REST API.
    pr_number = os.environ.get("PR_NUMBER") or (event.get("inputs") or {}).get("pr_number")
    if not pr_number:
        return None

    repo = os.environ["GITHUB_REPOSITORY"]
    pr = github_get(f"/repos/{repo}/pulls/{int(pr_number)}")

    if pr["state"] == "closed":
        action = "closed"
    else:
        action = "opened"

    return {"action": action, "pull_request": pr, "sender": pr.get("user")}


def describe(action, pr):
    """Map a PR action to (verb for the author line, embed colour)."""
    if action == "opened":
        if pr.get("draft"):
            return "opened a draft pull request", COLOR_DRAFT
        return "opened a pull request", COLOR_OPENED
    if action == "reopened":
        return "reopened a pull request", COLOR_OPENED
    if action == "ready_for_review":
        return "marked a pull request ready for review", COLOR_READY
    if action == "closed":
        if pr.get("merged"):
            return "merged a pull request", COLOR_MERGED
        return "closed a pull request without merging", COLOR_CLOSED
    return None, None


def build_payload(verb, color, pr, actor):
    author_login = (pr.get("user") or {}).get("login", "unknown")
    actor_login = actor.get("login", author_login)

    fields = []

    head = pr.get("head") or {}
    base = pr.get("base") or {}
    head_repo = (head.get("repo") or {}).get("full_name")
    base_repo = (base.get("repo") or {}).get("full_name")
    head_name = head.get("ref") if head_repo == base_repo else head.get("label")
    if head_name and base.get("ref"):
        fields.append(
            {"name": "Branch", "value": f"`{head_name}` → `{base['ref']}`", "inline": True}
        )

    # GitHub reports 0 files/lines for diffs too large to compute; skip the field then.
    if pr.get("additions") is not None and pr.get("changed_files"):
        files = pr["changed_files"]
        commits = pr.get("commits") or 0
        fields.append(
            {
                "name": "Changes",
                "value": (
                    f"+{pr['additions']:,} −{pr.get('deletions', 0):,} · "
                    f"{files:,} file{'s' if files != 1 else ''} · "
                    f"{commits:,} commit{'s' if commits != 1 else ''}"
                ),
                "inline": True,
            }
        )

    if actor_login != author_login:
        fields.append({"name": "Author", "value": author_login, "inline": True})

    labels = [lbl["name"] for lbl in pr.get("labels") or [] if lbl.get("name")]
    if labels:
        fields.append(
            {"name": "Labels", "value": truncate(", ".join(labels), FIELD_VALUE_LIMIT)}
        )

    embed = {
        "author": {
            "name": truncate(f"{actor_login} {verb}", TITLE_LIMIT),
            "url": actor.get("html_url"),
            "icon_url": actor.get("avatar_url"),
        },
        "title": truncate(f"#{pr.get('number')} {pr.get('title', '')}", TITLE_LIMIT),
        "url": pr.get("html_url"),
        "description": summarize_body(pr.get("body")),
        "color": color,
        "fields": fields,
        "footer": {"text": base_repo or os.environ.get("GITHUB_REPOSITORY", "")},
    }

    timestamp = pr.get("merged_at") or pr.get("closed_at") or pr.get("updated_at")
    if timestamp:
        embed["timestamp"] = timestamp

    return {
        "embeds": [embed],
        # PR titles and bodies are untrusted; never let them ping roles or everyone.
        "allowed_mentions": {"parse": []},
    }


ABOUT_SECTION = re.compile(
    r"^#{1,6}\s*About the PR\s*$(.*?)(?=^#{1,6}\s|\Z)",
    re.MULTILINE | re.DOTALL | re.IGNORECASE,
)


def summarize_body(body):
    if not body:
        return ""

    # Drop the hidden guidance from the PR template.
    text = re.sub(r"<!--.*?-->", "", body, flags=re.DOTALL)

    # Discord doesn't render HTML; unwrap the tags that commonly show up in PR bodies.
    text = re.sub(r"<summary>(.*?)</summary>", r"**\1**", text, flags=re.DOTALL | re.IGNORECASE)
    text = re.sub(r"</?details[^>]*>", "", text, flags=re.IGNORECASE)
    text = re.sub(r"<br\s*/?>", "\n", text, flags=re.IGNORECASE)

    # Prefer the "About the PR" section when the template was followed.
    match = ABOUT_SECTION.search(text)
    if match and match.group(1).strip():
        text = match.group(1)

    # Embed descriptions don't render markdown headers; make them bold instead.
    text = re.sub(r"^#{1,6}\s+(.+?)\s*#*\s*$", r"**\1**", text, flags=re.MULTILINE)

    lines = [line.rstrip() for line in text.strip().splitlines()]
    collapsed = []
    for line in lines:
        if line == "" and collapsed and collapsed[-1] == "":
            continue
        collapsed.append(line)
    text = "\n".join(collapsed).strip()

    text = truncate(text, DESCRIPTION_LIMIT)
    if text.count("```") % 2:
        text += "\n```"
    return text


def truncate(text, limit):
    if len(text) <= limit:
        return text
    cut = text[: limit - 1]
    boundary = max(cut.rfind("\n"), cut.rfind(" "))
    if boundary > limit // 2:
        cut = cut[:boundary]
    return cut.rstrip() + "…"


def github_get(path):
    headers = {
        "Accept": "application/vnd.github+json",
        "User-Agent": USER_AGENT,
    }
    token = os.environ.get("GITHUB_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"
    req = urllib.request.Request(f"{GITHUB_API_URL}{path}", headers=headers)
    with urllib.request.urlopen(req, timeout=30) as resp:
        return json.load(resp)


def send(payload) -> int:
    separator = "&" if "?" in DISCORD_WEBHOOK_URL else "?"
    url = f"{DISCORD_WEBHOOK_URL}{separator}wait=true"
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        url,
        data=data,
        headers={"Content-Type": "application/json", "User-Agent": USER_AGENT},
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            print(f"Discord responded {resp.status}")
            return 0
    except urllib.error.HTTPError as e:
        detail = e.read().decode("utf-8", "replace")
        print(f"Discord returned HTTP {e.code}: {detail}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
