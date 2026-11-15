# Security Policy

## Supported versions

TrakHound Framework is pre-release. Security fixes are made in the latest release only, so upgrade to it to get them.

## Reporting a vulnerability

Please don't report security problems in public issues, discussions or pull requests.

Report them privately through GitHub: on the repository's **Security** tab, choose **Report a vulnerability** ([direct link](https://github.com/TrakHound/TrakHound.Framework/security/advisories/new)). If you can't use GitHub, email **info@trakhound.com** with "Security" in the subject.

Include what you can of:

- The affected component (ex. the Instance, the System API, the MCP Server, the CLI, or a package) and version
- How to reproduce it, or a proof of concept
- What an attacker could do with it
- Any fix or workaround you know of

## What happens next

- We acknowledge the report within 3 business days.
- We tell you whether we can reproduce it, and keep you updated while we work on a fix.
- When the fix is released, we publish a GitHub Security Advisory and credit you, unless you'd rather stay anonymous.

Please give us a reasonable time to release a fix before disclosing the problem publicly.

## Scope

In scope: the code in this repository, including the Instance, its System API, MCP Server and Admin interface, the libraries, the CLI and the first-party packages in `modules/`.

Packages from other publishers are the responsibility of their publishers. If a third-party package can get around a protection the Instance provides (ex. Zones, package verification or Module Tokens), that is in scope.
