# Nuclei GitBook audit evidence

Captured September 12, 2026. This directory contains research artifacts only. It is outside the `docs/user-guide` content directory configured for GitBook.

| Observation | Evidence | Result |
| --- | --- | --- |
| Public index | [llms.txt](C:/Nuclei/docs/research/gitbook-usability-2026-09-12/llms.txt) | HTTP 200; 75 page links, including 40 component leaves and 18 example leaves. |
| Full export | [llms-full.txt](C:/Nuclei/docs/research/gitbook-usability-2026-09-12/llms-full.txt) | HTTP 200; 75 page headings; 85 Markdown images, all with empty labels in this capture. |
| Home Markdown | [Public home Markdown](https://nuclei.gitbook.io/docs/readme.md) | HTTP 200; actual documentation returned. |
| MCP initialization | [Protocol response](C:/Nuclei/docs/research/gitbook-usability-2026-09-12/mcp-initialize.txt) | Negotiated protocol 2025-03-26; tools capability present. |
| MCP tool discovery | [Tool schemas](C:/Nuclei/docs/research/gitbook-usability-2026-09-12/mcp-tools.txt) | searchDocumentation, getPage, askQuestion, sendFeedback. |
| MCP page retrieval | [Page response](C:/Nuclei/docs/research/gitbook-usability-2026-09-12/mcp-get-page.txt) | Returned Define Voxel Values Markdown, including port tables. |
| MCP search | [Search response](C:/Nuclei/docs/research/gitbook-usability-2026-09-12/mcp-search.txt) | Query: Construct Voxels Voxel Size default. Correct component was fifth returned result. Single query only; not a ranking benchmark. |

The protocol response files contain event-stream text returned over HTTP; their data lines contain JSON-RPC payloads. They are not standalone JSON documents. No feedback was sent and no GitBook content or client configuration was changed for these checks.

Representations differed between observations. Early welcome/component page reads included descriptive image text and different screenshot assets, while the later full export and MCP page used wired screenshot assets with empty image labels. Publishing timing, caching, or representation differences may explain this; no specific cause was established. The saved files should be treated as individual observations, not an atomic release snapshot.

The published site configuration exposed Markdown and MCP page actions, prominent search, page feedback, and Assistant mode. These settings confirm availability at inspection time, not long-term plan entitlements.

Local source examination established potential reference additions, including component GUIDs, runtime types, enum choices, index order, tree rules, and existing reflection infrastructure. The local working tree had extensive ongoing changes; no build, live simulation, minimum-hardware performance test, or release certification was performed as part of this research.
