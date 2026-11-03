# Repository workflow

- Use the `main` branch and the GitHub repository configured as `origin`.
- After completing each requested code or documentation update, run the relevant verification, create a focused commit, and push it to `origin/main` without asking again. The user explicitly requested this ongoing sync behavior.
- Never force-push. If the remote has commits not present locally or a push is rejected, inspect and integrate safely; do not overwrite remote history.
- Do not commit credentials, local `.pdfdata` files, generated scratch files, or user documents. Keep the application source, build instructions, sample PDF, and intentional release artifacts.
- If GitHub authentication or connectivity is unavailable, complete the local commit and report that the push remains pending.
