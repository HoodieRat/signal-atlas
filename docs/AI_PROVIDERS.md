# AI provider setup

Select the provider on the **AI Model** tab. Signal Atlas applies the selection to manual and scheduled analysis runs. Reports retain the provider's model label with each analysis.

## LM Studio (default)

No OpenAI API key is needed for this option. In **Setup → AI model**, install LM Studio and a model, click **Find installed models**, highlight the model, click **Use selected model**, then click **Test selected model**. Testing also saves the highlighted model automatically. The test runs a short local analysis with that exact model. If LM Studio already loaded it with enough context, Signal Atlas reuses it and leaves it loaded; otherwise the app loads and unloads its own copy.

The app checks RAM, GPU budget, battery state, and CPU pressure before loading a model. If the test is deferred, its message names the selected model and the resource constraint. Free memory or choose a smaller installed model, then test again. Discovery cards can still be published while analysis is deferred.

## OpenAI API key

Create a project API key in the [OpenAI Platform](https://platform.openai.com/api-keys). Set `OPENAI_API_KEY` in the Windows user environment and restart Signal Atlas. For example, in PowerShell:

```powershell
[Environment]::SetEnvironmentVariable('OPENAI_API_KEY', '<your key>', 'User')
```

Select **OpenAI API key**, choose a model ID (default `gpt-6-luna`), save, and use **Test selected provider**. API requests use the Responses API with structured output and `store: false`. API usage is billed separately from ChatGPT subscriptions. The key is read at runtime, kept out of application settings and logs, and never committed to this repository. Do not put a real key in an issue, report, sample file, or Git commit.

## Codex with ChatGPT sign-in

Install the [Codex CLI](https://learn.chatgpt.com/docs/codex/cli) for the Windows user who runs Signal Atlas. Select **Codex with ChatGPT sign-in** and click **Sign in with ChatGPT via Codex**, or run `codex login` yourself. Complete the browser sign-in, click **Check Codex sign-in**, and then **Test selected provider**.

Signal Atlas checks `codex login status` and accepts only a ChatGPT sign-in for this mode. It sends each analysis to a short-lived, read-only `codex exec` session in an empty work directory. It does not inspect Codex credential files or reuse an API key as a ChatGPT login. Codex CLI stores and refreshes its own login outside the repository. Scheduled runs use that signed-in Windows user's Codex installation and are subject to the account's plan access and usage limits.

This is a Codex CLI integration. It does not provide access to ChatGPT conversations. OpenAI documents [ChatGPT account sign-in and plan usage](https://developers.openai.com/cookbook/articles/sign-in-with-chatgpt) as distinct from API-key billing.

## What is transmitted

LM Studio keeps inference local. OpenAI API and Codex transmit the selected source title, URL, topic, and up to 14,000 characters of source text for each analysis, plus up to 30 completed summaries for a run synthesis. Do not select a cloud provider for content that must remain on this computer.
