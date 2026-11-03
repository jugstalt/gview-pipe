# Security policy

gView.Pipe runs workflows that can call web addresses, send mail, read and write files, run SQL and, with the scripts plugin, run scripts. Please take security issues seriously and report them privately.

## Reporting a vulnerability

Don't open a public issue. Use [private vulnerability reporting](https://github.com/jugstalt/gview-pipe/security/advisories/new) of this repository and describe:

- what you found and which version is affected,
- how to reproduce it, and
- what an attacker gains.

You get an answer as soon as possible; a fix is released as a new version and noted in the [changelog](CHANGELOG.md).

## Supported versions

Only the latest release gets security fixes.

## What the app protects and what you protect

By default the app has no login, and whoever can edit workflows can use everything the host releases (folders, script sources, credentials). The [Security page](https://docs.gviewonline.com/other/gview-pipe/admin-guide/security.html) of the documentation lists what to switch on before others can reach the app.
