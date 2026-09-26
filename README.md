# PapaPersonas

PapaPersonas is a local Windows desktop application maintained in two independent versions inside this repository.

## Choose a version

| Version | Status | Purpose |
| --- | --- | --- |
| [V1](V1/README.md) | Stable | Existing application with the cumulative DuckDB master database and the implemented Hernán, Sergio, query, stock, and maintenance workflows. |
| V2 | In development | Lighter application built incrementally around flexible file cleaning and non-cumulative processing. |

## Current development path

V2 is being developed in controlled work units:

1. Preserve V1 as an independently buildable application.
2. Create the V2 tabbed shell and Paso 1 interface.
3. Detect and display arbitrary workbook columns.
4. Add one cleaning behavior at a time with explicit logs and rejected-row evidence.

Progress and verification evidence are recorded in [`odd/tasks/v2-lightweight.md`](odd/tasks/v2-lightweight.md).

## Repository structure

```text
PapaPersonas/
├── V1/                     # Existing stable solution
│   ├── PapaPersonas.sln
│   ├── src/
│   ├── tests/
│   ├── config/
│   └── docs/
├── V2/                     # New lightweight solution (created incrementally)
├── odd/tasks/              # Recoverable implementation progress
└── README.md               # Repository landing page
```

## V1 quick start

Prerequisites:

- Windows 11
- .NET SDK 10.0.x

From the repository root:

```powershell
dotnet restore V1/PapaPersonas.sln
dotnet build V1/PapaPersonas.sln -c Release
dotnet test V1/PapaPersonas.sln -c Release
dotnet run --project V1/src/PapaPersonas.App/PapaPersonas.App.csproj
```

Operational details, privacy guidance, and database safety rules remain in [`V1/README.md`](V1/README.md).

## Safety boundary

- V1 and V2 are independent solutions.
- Generated outputs, local databases, and release artifacts remain excluded from Git.
- V2 changes must not alter V1 behavior or data.
- Pushes, pull requests, and merges remain separate user decisions.
