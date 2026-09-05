# Security

Please do not publish credentials, session cookies, API keys, downloaded mods, or private logs in a public issue.

Bibliognost stores the XMA session and Nexus API key with Windows Data Protection API encryption scoped to the current Windows user. The plugin does not intentionally log plaintext credentials. Clearing a provider connection removes its stored encrypted value.

For a suspected credential exposure, revoke the affected website session or API key first. Then contact the maintainer privately through the repository owner's preferred GitHub contact channel. Security reports should include the Bibliognost version, Dalamud version, reproduction steps, and sanitized logs.

Only download Bibliognost from the repository and releases controlled by its maintainer. Release ZIP checksums are published in GitHub release notes.

