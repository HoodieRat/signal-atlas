"""Fail if a tracked repository file resembles a credential or private runtime file.

Prints only file, line, and category; never prints a matched value.
"""

from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DENIED_NAMES = {".env", "auth.json", "credentials.json", "secrets.json"}
DENIED_SUFFIXES = {".db", ".sqlite", ".pem", ".pfx", ".p12", ".key"}
DENIED_PARTS = {"bin", "obj", "release", "validation", "state", "logs", "cache", ".codex"}
PATTERNS = {
    "OpenAI key": re.compile(rb"\bsk-(?:proj-)?[A-Za-z0-9_-]{20,}\b"),
    "ElevenLabs key": re.compile(rb"\bsk_[A-Za-z0-9]{32,}\b"),
    "GitHub token": re.compile(rb"\b(?:ghp|gho|ghs|ghr|github_pat)_[A-Za-z0-9_]{20,}\b"),
    "AWS access key": re.compile(rb"\bAKIA[A-Z0-9]{16}\b"),
    "private key block": re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
    "credential assignment": re.compile(
        rb"(?i)\b(?:password|passwd|client_secret|access_token|refresh_token|api_key)\s*[:=]\s*['\"]?[A-Za-z0-9+/_-]{20,}"
    ),
    "personal Windows profile path": re.compile(rb"(?i)[A-Z]:\\Users\\(?!Public\\|Default\\)[^\\\s\"']+\\"),
}


def files_to_check() -> list[Path]:
    if (ROOT / ".git").exists():
        result = subprocess.run(["git", "ls-files", "--cached", "-z"], cwd=ROOT, check=True, capture_output=True)
        return [ROOT / entry.decode("utf-8", "surrogateescape") for entry in result.stdout.split(b"\0") if entry]
    return [path for path in ROOT.rglob("*") if path.is_file() and not any(part.lower() in DENIED_PARTS for part in path.relative_to(ROOT).parts)]


def main() -> int:
    problems: list[str] = []
    files = files_to_check()
    for path in files:
        relative = path.relative_to(ROOT)
        parts = {part.lower() for part in relative.parts}
        name = path.name.lower()
        if name in DENIED_NAMES or path.suffix.lower() in DENIED_SUFFIXES or parts & DENIED_PARTS:
            problems.append(f"{relative}: private/runtime file path")
            continue
        if not path.is_file():
            problems.append(f"{relative}: missing tracked file")
            continue
        data = path.read_bytes()
        for label, pattern in PATTERNS.items():
            match = pattern.search(data)
            if match:
                line = data.count(b"\n", 0, match.start()) + 1
                problems.append(f"{relative}:{line}: {label}")
    for problem in problems:
        print(problem)
    if problems:
        print(f"Secret scan failed: {len(problems)} finding(s) in {len(files)} file(s).")
        return 1
    print(f"Secret scan passed: {len(files)} files checked; no credential patterns or private runtime files found.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
