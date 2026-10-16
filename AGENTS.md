# RoadTraffic — DT Vault context rules

For RoadTraffic, DT Vault in Obsidian is the single source of truth for
long-lived project context, user decisions, architecture constraints, runtime
findings, and completed milestones. Do not create a parallel project memory in
chat summaries, ad-hoc files, or another knowledge store.

## Vault access

- Use the available Obsidian MCP/REST API at `http://127.0.0.1:27123/mcp/` or
  the authorized local access to `G:/Můj disk/Obsidian Vault/DT`.
- Obtain credentials only from existing local configuration. Never display,
  copy, or store API keys and tokens in the repository, chat, or Vault notes.
- At the start of any Vault operation, read the Vault root `AGENTS.md`. It is
  the authoritative routing and safety policy for all subsequent Vault work.
- Follow its required inventory, relevant-index, temporal-layer, audit,
  navigation, logging, and read-back steps. Do not write to the Vault when
  those rules or the Vault itself are unavailable.

## When to use the Vault

Use the Vault before relying on or changing RoadTraffic decisions that may
have been made outside the current working tree, including architecture,
simulator behavior, MSFS/SimConnect integration, runtime findings, user
preferences, accepted trade-offs, and release/deployment state.

When the user asks to remember, store, retrieve, continue, verify, or update
RoadTraffic context, use Obsidian as the SSOT. Read the relevant project note
and navigation first; do not infer current state from old chat text alone.

Do not load unrelated private Vault content for ordinary coding tasks that are
fully answerable from the repository and current conversation.

## Writing project knowledge

Only write when the user has authorized storing or remembering the information.
Classify the record accurately: user report, confirmed runtime observation,
implementation change, planned work, hypothesis, or AI proposal. Preserve
historical evidence, avoid duplicate notes, update relevant navigation and the
derived index, append the required audit log entry, and verify the saved
content and links by reading them back.
