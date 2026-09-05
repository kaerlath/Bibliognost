# Release checklist

## First private-testing publication

1. Create an empty GitHub repository named `Bibliognost`. Do not initialize it with a README, license, or `.gitignore`.
2. Make the repository **public** before giving the custom-repository URL to testers. Dalamud cannot authenticate to GitHub's private raw-file URLs. It may remain private only while you are preparing it for release.
3. From this folder, run the one-line command documented below.
4. Complete GitHub's browser sign-in if Git Credential Manager asks.
5. Confirm that the `v0.23.0` Actions run creates a GitHub release containing `latest.zip`.
6. Give testers the raw `repo.json` URL printed by the script.

```powershell
.\Publish-GitHub.ps1 -RepositoryUrl "https://github.com/YOUR-GITHUB-NAME/Bibliognost.git"
```

## Before public distribution

- Choose and add a source-code license. With no repository license, the code remains all-rights-reserved by default; the bundled Charito font retains its separate SIL Open Font License.
- Replace personal Nexus API-key authentication with the registered Nexus SSO application flow.
- Review provider terms and live endpoints again.
- Run private in-game testing on the current Dalamud release.
- Decide whether to submit to Dalamud's testing channel. Disclose the project's AI assistance as required by the current Dalamud submission policy.
