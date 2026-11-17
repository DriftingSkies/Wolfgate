#!/usr/bin/env python3
"""Publish this workflow run's build artifact to Robust.Cdn.

The CDN's one-shot publish takes a URL and pulls the archive itself, so nothing large is uploaded from here:
the request is a few hundred bytes and goes through Cloudflare fine, and the CDN fetches the artifact from
GitHub directly. The archive is the `release/` artifact of this run: SS14.Client.zip and SS14.Server_*.zip at
the root, which is how actions/upload-artifact lays out a single directory.

Environment (all set by .github/workflows/publish.yml):
  GITHUB_TOKEN        to resolve the artifact's short-lived download URL
  PUBLISH_TOKEN       the fork's UpdateToken on the CDN (repository secret)
  ARTIFACT_ID         output of actions/upload-artifact
  GITHUB_REPOSITORY   owner/repo
  GITHUB_SHA          commit being published (part of the version name)
  VERSION_SUFFIX      appended to the version: the test merge numbers and the panel's request id, if any
  FORK_ID             the CDN fork id to publish under; empty or unset is the fork itself
Optional overrides: ROBUST_CDN_URL, FORK_ID, VERSION.

Standard library only, so it runs on a bare runner.
"""
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone

#
# CONFIGURATION PARAMETERS
# Forks should change these to publish to their own infrastructure.
#
ROBUST_CDN_URL = os.environ.get("ROBUST_CDN_URL") or "https://wolfgatecdn.a13.info/"
# Empty means the fork itself: the workflow passes its fork_id input through as is.
FORK_ID = os.environ.get("FORK_ID") or "wolfgate"
# Upstream default before the fork-configurable overrides above:
# ROBUST_CDN_URL = "https://wizards.cdn.spacestation14.com/"
# FORK_ID = "wizards"

GITHUB_TOKEN = os.environ["GITHUB_TOKEN"]
PUBLISH_TOKEN = os.environ["PUBLISH_TOKEN"]
ARTIFACT_ID = os.environ["ARTIFACT_ID"]
GITHUB_REPOSITORY = os.environ["GITHUB_REPOSITORY"]
GITHUB_SHA = os.environ.get("GITHUB_SHA", "unknown")

PUBLISH_ATTEMPTS = 3
INGEST_WAIT_SECONDS = 600


def log(msg):
    print(msg, flush=True)


def request(method, url, headers=None, body=None, timeout=180, follow_redirects=True):
    """Returns (status, headers, text). Never raises for HTTP errors; raises for network errors."""
    req = urllib.request.Request(url, method=method, data=body, headers=headers or {})
    opener = urllib.request.build_opener() if follow_redirects else urllib.request.build_opener(_NoRedirect())
    try:
        with opener.open(req, timeout=timeout) as resp:
            return resp.status, dict(resp.headers), resp.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as e:
        return e.code, dict(e.headers), e.read().decode("utf-8", "replace")


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def get_engine_version():
    """The engine version the build reports; from the submodule's version props (git tags may not be fetched)."""
    props = os.path.join("RobustToolbox", "MSBuild", "Robust.Engine.Version.props")
    with open(props, encoding="utf-8") as f:
        m = re.search(r"<Version>([^<]+)</Version>", f.read())
    if not m:
        sys.exit(f"could not read the engine version from {props}")
    return m.group(1).strip()


def get_artifact_url():
    """A short-lived, unauthenticated download URL for the artifact zip (the API answers with a redirect)."""
    status, headers, text = request(
        "GET",
        f"https://api.github.com/repos/{GITHUB_REPOSITORY}/actions/artifacts/{ARTIFACT_ID}/zip",
        headers={"Authorization": f"Bearer {GITHUB_TOKEN}", "X-GitHub-Api-Version": "2022-11-28",
                 "User-Agent": "wolfgate-publish"},
        follow_redirects=False,
    )
    if status not in (302, 301, 307) or "Location" not in headers:
        sys.exit(f"could not resolve the artifact download URL ({status}): {text[:300]}")
    return headers["Location"]


def version_on_cdn(version):
    """True once the CDN lists the version in its server manifest (i.e. ingested and available)."""
    try:
        status, _, text = request("GET", f"{ROBUST_CDN_URL}fork/{FORK_ID}/manifest",
                                  headers={"User-Agent": "wolfgate-publish"}, timeout=30)
    except Exception:
        return False
    return status == 200 and version in json.loads(text).get("builds", {})


def main():
    version = os.environ.get("VERSION") or f"{datetime.now(timezone.utc):%Y%m%d-%H%M%S}-{GITHUB_SHA[:7]}{os.environ.get('VERSION_SUFFIX', '')}"
    engine = get_engine_version()
    log(f"Publishing {version} (engine {engine}) to {ROBUST_CDN_URL}fork/{FORK_ID}")

    headers = {"Authorization": f"Bearer {PUBLISH_TOKEN}", "Content-Type": "application/json",
               "User-Agent": "wolfgate-publish"}

    for attempt in range(1, PUBLISH_ATTEMPTS + 1):
        # A fresh URL each time: the signed artifact link only lives for about a minute.
        archive = get_artifact_url()
        body = json.dumps({"version": version, "engineVersion": engine, "archive": archive}).encode()
        try:
            status, _, text = request("POST", f"{ROBUST_CDN_URL}fork/{FORK_ID}/publish", headers, body)
        except Exception as e:  # network-level failure (timeout, reset)
            status, text = 0, str(e)

        if status == 204:
            log("CDN accepted and stored the build.")
            break
        if status == 409:
            log("CDN already has this version; treating as published.")
            break
        if status in (400, 401, 404):
            sys.exit(f"CDN refused the publish ({status}): {text[:300]}")

        # 5xx, 0, or a Cloudflare 52x: the CDN may still have finished behind the proxy. Check before retrying.
        log(f"attempt {attempt}: CDN answered {status or 'no response'}: {text[:200]}")
        time.sleep(15)
        if version_on_cdn(version):
            log("...but the version is on the CDN, so the publish went through.")
            break
        if attempt == PUBLISH_ATTEMPTS:
            sys.exit("publish failed after retries")
        log("retrying...")

    log("Waiting for the CDN to ingest the client and list the version...")
    deadline = time.time() + INGEST_WAIT_SECONDS
    while time.time() < deadline:
        if version_on_cdn(version):
            log(f"SUCCESS: {version} is live on the CDN; the watchdog has been notified and the server restarts at round end.")
            return
        time.sleep(10)
    sys.exit(f"the CDN accepted {version} but has not listed it after {INGEST_WAIT_SECONDS}s; check the CDN log")


if __name__ == "__main__":
    main()
