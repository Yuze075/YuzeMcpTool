export const summary = "**reflection** - Inspect C# types and call static methods when helpers do not cover a custom API.";

export const description = `
Use this module when you need information about C# types, project-specific classes, or a static method call that is not wrapped by a helper.

Functions:
- \`getNamespaces()\` - List public namespaces visible to the current AppDomain.
- \`getTypes(namespaceName)\` - List public types in one namespace.
- \`getTypeDetails(fullName)\` - Return public methods, fields, and properties for a type.
- \`findMethods(args)\` - Search public methods by query/type. Non-public search requires \`includeNonPublic: true\` and \`confirmDangerous: true\`.
- \`callStaticMethod(args)\` - Call a public static method. Non-public calls require \`includeNonPublic: true\` and \`confirmDangerous: true\`.

For instance methods or more complex custom class workflows, write custom JavaScript in \`execute()\` and combine this module with \`Mcp.invokeAsync\` or PuerTS-accessible C# APIs.
`.trim();

function unwrap(response) {
  if (!response || response.success !== true) throw new Error(response?.error || "MCP bridge command failed.");
  return response.result;
}

export async function getNamespaces() { return unwrap(await Mcp.invokeAsync("reflection.execute", { action: "getNamespaces" })); }
export async function getTypes(namespaceName) { return unwrap(await Mcp.invokeAsync("reflection.execute", { action: "getTypes", namespace: namespaceName })); }
export async function getTypeDetails(fullName) { return unwrap(await Mcp.invokeAsync("reflection.execute", { action: "getTypeDetails", fullName })); }
export async function findMethods(args = {}) { return unwrap(await Mcp.invokeAsync("reflection.execute", { ...args, action: "findMethods" })); }
export async function callStaticMethod(args) { return unwrap(await Mcp.invokeAsync("reflection.execute", { ...args, action: "callStaticMethod" })); }
