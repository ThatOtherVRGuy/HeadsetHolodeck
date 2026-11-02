# PICO General Agentic Tools Plugin Guidance

This installed plugin provides cross-platform skills shared by PICO Spatial and Unity development workflows. It is automatically installed alongside the platform-specific plugin (spatial or unity) and does not need to be selected manually during setup.

## Plugin Guidance Overview

The plugin provides platform-agnostic agentic tools that are useful across all PICO development targets. Skills are not code libraries. They are host-loaded prompts plus bundled references.

Agents may read this file from a copied or linked project context. In that case, the current working directory is the user's application project, not the plugin source or marketplace root.

## Available Skills

| Skill             | When to use                                                                                                                                                                                              |
| ----------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `knowledge-query` | Use when a PICO development question requires searching the PICO knowledge graph, querying a specific platform's knowledge, or the user explicitly asks to search the knowledge base or knowledge graph. |
