# JSON Schemas

JSON Schemas for the files in a package project, so editors (VS Code, Visual Studio) complete and check them. The package templates point at them with `$schema`.

| Schema | File |
| --- | --- |
| `trakhound.package.schema.json` | `trakhound.package.json` |
| `trakhound.package.publish.schema.json` | `trakhound.package.publish.json` |
| `trakhound.debug.schema.json` | `trakhound.debug.json` |

They're served at `https://www.trakhound.com/schemas/<file>` (each schema's `$id`). Copy them there when they change.

They're written by hand. `SchemaTests` in `tests/TrakHound.CLI.Tests` checks that each one lists the same properties as the type that reads its file, and that the templates point at them, so add a property to the schema when you add one to the type.
