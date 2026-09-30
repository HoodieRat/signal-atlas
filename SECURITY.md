# Security

This repository must contain no live credentials or private research data. The application reads `OPENAI_API_KEY` from the user's environment and uses Codex CLI's own ChatGPT login state; it does not save either credential in its SQLite database.

Before every push, run `python scripts/check_secrets.py` and review `git diff --cached --name-only`. The scan is a safeguard, not proof that arbitrary files contain no secrets. Do not add database files, account profiles, runtime logs, report archives, or unreviewed screenshots.

Report a vulnerability privately through the repository's GitHub security advisory flow or directly to the repository owner. Do not publish a credential or private content in an issue. If a credential is exposed, revoke and rotate it immediately; removing it from the latest commit alone does not remove it from Git history.
