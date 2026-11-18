# AI provider setup (environment variables)

DocTwin never stores AI keys. It reads them from **Windows environment variables** at the moment you
click **Analyse selected pair with AI**. Keys are not written to settings, logs, exports or the
review history, and the app only shows whether a variable is set, never its value.

AI is optional. Discovery, the content engine, comparison and reports work without any key.

## Variables

| Provider (AI tab) | Variable | Default model | Notes |
| --- | --- | --- | --- |
| OpenAICompatible | `OPENAI_API_KEY` | `gpt-4.1` | Any OpenAI-compatible endpoint; change the variable *name* in the AI tab if your key uses another name. |
| Claude | `ANTHROPIC_API_KEY` | `claude-sonnet-4-6` | Anthropic Messages API. |
| GitHubCopilot | `COPILOT_GITHUB_TOKEN` | `gpt-4.1` | Used when set. If not set, DocTwin falls back to `copilot login` sign-in. |

The Copilot token must be a **fine-grained personal access token with the "Copilot Requests"
permission** (classic `ghp_` tokens are not supported by the Copilot runtime). It authenticates
only the Copilot request; it is unrelated to the Confluence token.

## Setting a variable

Use **Windows Settings > System > About > Advanced system settings > Environment Variables**, add the
variable under **User variables**, then restart DocTwin (and Visual Studio if you launch from it).
Or from PowerShell, replacing the placeholder (do not paste real keys into scripts or chat):

```powershell
[Environment]::SetEnvironmentVariable('ANTHROPIC_API_KEY', '<your key>', 'User')
```

DocTwin checks the process environment first, then the Windows user scope, so a variable added
after the app started is usually found. If the AI tab still reports it missing, choose
**Re-check environment** or restart the app.

## Using it

1. Open the **AI** tab and pick a provider. The variable name and model are pre-filled.
2. Check the status line: it should say the variable *is set (value hidden)*.
3. Load a finding (**Prepare AI**), tick the consent box, then **Analyse selected pair with AI**.
4. Optionally **Save AI profile**. This stores only the provider, model, endpoint and the variable
   *name*, never the key. An older saved OpenAI profile that names `DOCTWIN_OPENAI_API_KEY` keeps
   using that name until you change it to `OPENAI_API_KEY` and save again.

## Whole-tree quality review

The **AI quality advice** tab uses the same provider, model, endpoint and key variable as the AI tab.
After a scan it shows what would be sent. One consent covers exactly that plan: one request per page,
at most 50 pages and 600,000 characters, flagged pages first, nothing truncated, stopping after 3
rejected requests in a row. Changing the scan or any AI setting withdraws consent. Each page is sent
as text, title, hierarchy path, update date and version only (no owner IDs or tokens), and every quote
in the answer must appear exactly in that page or the answer is rejected. Results are suggestions.

## Safety notes

- Each request still needs explicit, per-pair consent; opening the tab sends nothing.
- Page text, titles, IDs, hierarchy path and extraction limitations are sent to the chosen provider.
  Confluence tokens never are.
- Billed AI requests are not retried automatically.
- Anyone who can read your user environment variables can read these keys. Use least-privilege keys
  with spend limits, rotate them if exposed, and never commit them to source control.
