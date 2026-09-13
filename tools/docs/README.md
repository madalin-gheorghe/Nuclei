# Documentation JSON references

The archive exporter reads the exact `.gh` downloads without loading components, running Rhino, solving a definition, or modifying the source files.

From the repository root, with .NET 8, an installed Grasshopper `GH_IO.dll`, and Python:

```powershell
dotnet run --project tools/docs/DefinitionArchiveExporter -- 'C:\Program Files\Rhino 9 WIP\Plug-ins\Grasshopper\GH_IO.dll' docs/user-guide/examples/files .codex-temp/definition-json-export/xml
python tools/docs/build_json_references.py .codex-temp/definition-json-export/xml
```

Always export fresh XML immediately before rebuilding JSON. The builder attaches the current `.gh` hash, resolves every saved data wire, and records missing group members and omitted binary payloads. It preserves the existing example prose and component contracts. Schemas are maintained in `docs/user-guide/reference/`; their semantics are explained in `reading-json.md`.

Validate the generated files with a JSON Schema Draft 2020-12 validator before publishing. Also verify source hashes, node and parameter IDs, wire endpoints and ordering, and parity between embedded JSON and downloads. The schema alone cannot check graph references or simulation behavior.
