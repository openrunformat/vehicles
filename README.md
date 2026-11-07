# Open Run Format vehicle registry

An open registry of production vehicle models and of setup templates and modifications for
[Open Run Format](https://github.com/openrunformat/spec). Each model generation has a stable, human-readable id and a
permanent `uid`, a few checkable stock facts (wheel fitment, drive modes) and, when there is one, the template of
setup parameters that suits it. Applications use the registry to propose the parameters of a car's setup sheet and to
link a setup to the model it was made for, whatever application wrote it.

Templates and modifications are ordinary `.runsetup` files (section 11 of the
[specification](https://github.com/openrunformat/spec/blob/main/SPEC.md)): anything here can also be shared, imported
and exported as a file, without depending on this registry.

## Identifiers

| Example | Meaning |
|---|---|
| `example-make` | Make id: 2 to 32 characters (`a-z`, `0-9`, single `-`) |
| `example-make/example-model` | Model id: make id, `/`, model name with the same rules |
| `example-make/example-model/base@gen1` | Generation id: model id, `/`, variant id, `@`, generation id (variant and generation ids follow the same rules) |
| `generic-street`, `mod-coilover-2way` | Template and modification ids: up to 64 characters (`a-z`, `0-9`, single `-`) |

Generation ids are the values a `.runsetup` file uses in `appliesTo`. Model and generation `uid` values (UUID
version 7) never change. Templates and modifications carry a `version` that grows with every change.

## Layout of this repository

| Path | Content |
|---|---|
| `models/<make>/<model>.json` | One file per model with its variants and generations, described by `schema/model.schema.json` |
| `templates/<id>.runsetup` | Templates (`kind: "template"`): generic, or for the generations in `appliesTo` |
| `modifications/<id>.runsetup` | Modifications (`kind: "modification"`): generic packages that add, change or remove parameters |
| `schema/` | JSON schemas of a model file and of the index; `systems.json` lists accepted external id systems |
| `dist/index.json` | Generated index of the whole registry (never edited by hand) |
| `tools/` | `orf-vehicles`, the validator and index generator (.NET) |
| `tests/fixtures/` | A synthetic registry used by the tests |

## Index

Applications download one file, `dist/index.json`:
`https://raw.githubusercontent.com/openrunformat/vehicles/main/dist/index.json` (once the repository is public).
Its shape is described by [`schema/index.schema.json`](schema/index.schema.json). It contains every model with its
variants and generations (each with its full generation id in `fullId`), and every template and modification
**whole**, in the `file` field of its entry, so that one download is enough. Contributor details stay in the model files;
the index keeps only the attribution texts of the sources.

## Commands

Requires the .NET SDK named in `global.json`.

```sh
dotnet run --project tools/OrfVehicles -- validate .                       # check every file of the registry
dotnet run --project tools/OrfVehicles -- index . --generated-at <ISO-8601>  # write dist/index.json
dotnet run --project tools/OrfVehicles -- uid                              # print a new uid
dotnet test --solution tools/OrfVehicles.sln                               # run the tool's tests
```

Besides the schema, `validate` checks that ids and uids are unique, that every `appliesTo` names a generation of the
registry, that every generation's `template` exists, that each file sits in the folder of its kind and that every
template can be composed with every generic modification (and with the modifications for its generations).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## Licences

- **Data** — `models/`, `templates/`, `modifications/`, `schema/`, `dist/` and `tests/fixtures/` — and this
  documentation are dedicated to the public domain under CC0-1.0 (see [`LICENSE`](LICENSE)). By contributing data you
  dedicate it under CC0-1.0 too.
- **Code** — `tools/` — is licensed under the Apache License 2.0 (see [`tools/LICENSE`](tools/LICENSE) and
  [`tools/NOTICE`](tools/NOTICE)).

Make and model names are used only to identify the vehicles; they belong to their owners. The registry is not
affiliated with or endorsed by any vehicle manufacturer.
