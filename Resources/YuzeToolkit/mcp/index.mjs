export const summary = "**index** - Discovery guide for the compact Unity MCP helper modules.";

export const modules = [
  {
    name: "runtime",
    path: "YuzeToolkit/mcp/Runtime/runtime.mjs",
    summary: "Environment state, Unity logs, and bridge command batching."
  },
  {
    name: "objects",
    path: "YuzeToolkit/mcp/Runtime/objects.mjs",
    summary: "Scene GameObject, hierarchy, and Transform query/edit operations."
  },
  {
    name: "components",
    path: "YuzeToolkit/mcp/Runtime/components.mjs",
    summary: "Component query, edit, batch property, and instance method operations."
  },
  {
    name: "diagnostics",
    path: "YuzeToolkit/mcp/Runtime/diagnostics.mjs",
    summary: "Read-only cameras, physics, graphics, UI, and loaded texture diagnostics."
  },
  {
    name: "reflection",
    path: "YuzeToolkit/mcp/Runtime/reflection.mjs",
    summary: "C# type inspection and static method calls for custom APIs."
  },
  {
    name: "editor",
    path: "YuzeToolkit/mcp/Editor/editor.mjs",
    summary: "Editor state, compilation, selection, menu commands, play mode, and screenshots."
  },
  {
    name: "assets",
    path: "YuzeToolkit/mcp/Editor/assets.mjs",
    summary: "AssetDatabase search, text IO, asset moves, scripts, materials, dependencies, and refresh."
  },
  {
    name: "importers",
    path: "YuzeToolkit/mcp/Editor/importers.mjs",
    summary: "AssetImporter inspection, serialized edits, and reimport."
  },
  {
    name: "scenes",
    path: "YuzeToolkit/mcp/Editor/scenes.mjs",
    summary: "Scene file and open scene hierarchy operations."
  },
  {
    name: "prefabs",
    path: "YuzeToolkit/mcp/Editor/prefabs.mjs",
    summary: "Prefab instance, asset, stage, override, and unpack operations."
  },
  {
    name: "serialized",
    path: "YuzeToolkit/mcp/Editor/serialized.mjs",
    summary: "SerializedObject and Inspector property reads/writes."
  },
  {
    name: "project",
    path: "YuzeToolkit/mcp/Editor/project.mjs",
    summary: "Project settings, profiler, and editor tool diagnostics."
  },
  {
    name: "pipeline",
    path: "YuzeToolkit/mcp/Editor/pipeline.mjs",
    summary: "Package Manager, Test Runner, and BuildPipeline workflows."
  },
  {
    name: "validation",
    path: "YuzeToolkit/mcp/Editor/validation.mjs",
    summary: "Project health checks for missing scripts, references, and conventions."
  }
];

export const description = `
YuzeMcpTool exposes one MCP tool: \`evalJsCode\`.

Inside \`evalJsCode\`, write an async function named \`execute\` and import only the helper module you need:

\`\`\`javascript
async function execute() {
  const index = await import('YuzeToolkit/mcp/index.mjs');
  return index.description;
}
\`\`\`

Helper discovery workflow:
1. Start with this index to choose the right domain module.
2. On first use of a module, return its \`description\` export to learn exact function names and safety rules.
3. After you know the API, call helper functions directly. You can combine multiple helper modules in one script when the workflow does not require a Domain Reload between steps.

Module categories:
- Runtime modules can run in Editor or Runtime/Player when the underlying Unity API is available.
- Editor modules use \`UnityEditor\` APIs and fail in Runtime/Player.
- Use \`Runtime/runtime.mjs#getState()\`, direct \`runtime.getState\`, or \`/health\` when you need to check the current environment.

Available modules:
${modules.map((item) => `- ${item.name}: \`${item.path}\` - ${item.summary}`).join("\n")}

Direct bridge calls:
- Runtime bridge commands: \`runtime.getState\`, \`log.execute\`, \`object.execute\`, \`component.execute\`, \`runtime.diagnostics\`, \`reflection.execute\`, \`batch.execute\`.
- Editor bridge commands: \`editor.execute\`, \`asset.execute\`, \`importer.execute\`, \`scene.execute\`, \`prefab.execute\`, \`serialized.execute\`, \`project.execute\`, \`pipeline.execute\`, \`validation.execute\`.
- Domain bridge commands take an \`action\` argument, for example:

\`\`\`javascript
async function execute() {
  return await Mcp.invokeAsync('asset.execute', {
    action: 'find',
    filter: 't:AnimationClip',
    folders: ['Assets'],
    limit: 20
  });
}
\`\`\`

Custom JavaScript:
- If a helper does not cover a project-specific class, custom editor utility, or special Unity API, write JavaScript inside \`execute()\`.
- Prefer existing helpers first. For custom logic, combine \`Mcp.invokeAsync(...)\`, \`reflection\`, selectors returned by \`objects\`, and any PuerTS-accessible C# API available in this Unity project.
- Direct C# interop uses PuerTS globals. Use \`CS.UnityEngine.*\` and, in the Editor, \`CS.UnityEditor.*\`; this is not a Node.js environment.
- When a Unity API expects a \`System.Type\`, pass \`puer.$typeof(CS.Namespace.Type)\` or \`puerts.$typeof(CS.Namespace.Type)\`. Example: \`go.GetComponent(puer.$typeof(CS.UnityEngine.Camera))\`.
- Static methods and constructors follow PuerTS conventions, for example \`CS.UnityEngine.GameObject.Find('Main Camera')\` or \`new CS.UnityEngine.GameObject('Temp')\`.
- Return plain JSON-friendly objects. Avoid returning raw UnityEngine.Object graphs directly; summarize names, instanceIds, paths, and primitive values.
- Always return a concise serializable value. Objects are converted to text through JSON serialization.

Direct PuerTS example:

\`\`\`javascript
async function execute() {
  const go = CS.UnityEngine.GameObject.Find('Main Camera');
  if (!go) return { found: false };
  const camera = go.GetComponent(puer.$typeof(CS.UnityEngine.Camera));
  return {
    found: true,
    name: go.name,
    instanceId: go.GetInstanceID(),
    orthographic: camera ? camera.orthographic : null,
    orthographicSize: camera ? camera.orthographicSize : null
  };
}
\`\`\`

Safety:
- Destructive operations require explicit confirmation where documented, such as \`confirm: true\`.
- Non-public reflection or static/member writes require \`confirmDangerous: true\` where documented.
- Asset file paths are resolved inside the Unity project root by bridge commands that perform file IO.
- Do not trigger script compilation or AssetDatabase refresh and then wait for completion in the same eval call. Trigger it, reconnect if needed, then inspect state/logs in a later call.
`.trim();

export function listModules() {
  return { success: true, modules };
}
