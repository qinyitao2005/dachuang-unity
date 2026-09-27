"""MCP client that verifies the exact RobotTraining working-copy path before calls."""
import argparse
import asyncio
import json
import sys
from pathlib import Path
from mcp import ClientSession
from mcp.client.streamable_http import streamable_http_client

PROJECT = Path(__file__).resolve().parent.parent
URL = "http://127.0.0.1:8080/mcp"

def payload(result):
    structured = getattr(result, "structuredContent", None)
    if structured is not None:
        return structured
    content = getattr(result, "contents", getattr(result, "content", []))
    texts = [item.text for item in content if hasattr(item, "text")]
    if len(texts) == 1:
        try:
            return json.loads(texts[0])
        except json.JSONDecodeError:
            return {"text": texts[0]}
    return {"texts": texts}

async def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("kind", choices=["instances", "resource", "call", "tools", "resources"])
    parser.add_argument("name", nargs="?")
    parser.add_argument("--args-file", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    async with streamable_http_client(URL) as (reader, writer, _):
        async with ClientSession(reader, writer) as client:
            await client.initialize()
            instances = payload(await client.read_resource("mcpforunity://instances"))
            if args.kind == "instances":
                result = instances
            else:
                candidates = [i for i in instances.get("instances", []) if i.get("name") == PROJECT.name]
                if len(candidates) != 1:
                    raise RuntimeError("Expected one RobotTraining editor: " + json.dumps(instances))
                selected = await client.call_tool("set_active_instance", {"instance": candidates[0]["id"]})
                if selected.isError or payload(selected).get("success") is False:
                    raise RuntimeError(str(payload(selected)))
                info = payload(await client.read_resource("mcpforunity://project/info"))
                actual = Path(info.get("data", {}).get("projectRoot", "")).resolve()
                if actual != PROJECT:
                    raise RuntimeError(f"Wrong connected project {actual}; expected {PROJECT}")
                if args.kind == "tools":
                    result = [{"name": t.name, "description": t.description, "inputSchema": t.inputSchema} for t in (await client.list_tools()).tools]
                elif args.kind == "resources":
                    result = [r.model_dump(mode="json") for r in (await client.list_resources()).resources]
                elif args.kind == "resource":
                    result = payload(await client.read_resource(args.name))
                else:
                    arguments = json.loads(args.args_file.read_text(encoding="utf-8-sig")) if args.args_file else {}
                    tool_result = await client.call_tool(args.name, arguments)
                    result = payload(tool_result)
                    if tool_result.isError or (isinstance(result, dict) and result.get("success") is False):
                        raise RuntimeError(json.dumps(result, ensure_ascii=False))
            output = json.dumps(result, ensure_ascii=False, indent=2)
            if args.output:
                args.output.write_text(output, encoding="utf-8")
                print(str(args.output))
            else:
                print(output)

if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    asyncio.run(main())
