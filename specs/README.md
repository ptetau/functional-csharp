# App specifications

Four small applications, written as plain specifications. They say what each app does, not how to build it, so they can be used with any language, framework or tool.

| Spec | Kind | Exercises |
|------|------|-----------|
| [Ledger](ledger.md) | Command line | Parsing, validation, money arithmetic, reports |
| [Character reviews](character-reviews.md) | Website | Web requests, forms, persistence, HTML rendering, search |
| [Ray tracer](ray-tracer.md) | Command line | Numeric code, recursion, performance, image output |
| [Highlight reel](highlight-reel.md) | Command line | URL and time parsing, interval arithmetic, several output formats |

## How to read a spec

- **Requirements** are numbered (`L-3`, `C-12`...) so tests and issues can refer to them.
- **Must** is required. **Should** is expected unless there's a good reason. **May** is optional.
- **Examples** are acceptance tests. An implementation passes when every example gives exactly the output shown.
- **Out of scope** lists things deliberately left out. Don't build them.

Each app is small on purpose: a few hundred lines in most languages.
