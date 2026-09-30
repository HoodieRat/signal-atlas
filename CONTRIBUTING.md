# Contributing

Open an issue describing the change before large feature work. Keep changes focused, document user-facing behavior, and run `dotnet build SignalAtlas.sln`, `dotnet test SignalAtlas.sln`, and `python scripts/check_secrets.py` before proposing a pull request.

Use synthetic fixtures for tests. Public demos may use real public source material when the sources and run method are documented. Never commit credentials, browser profiles, SQLite research data, reports generated from private sources, or logs. When changing AI providers, preserve the LM Studio local option and the user's explicit provider selection.
