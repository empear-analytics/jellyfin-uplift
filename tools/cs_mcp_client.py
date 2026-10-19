#!/usr/bin/env python3
"""A minimal stdio client for the CodeScene MCP server.

The refactoring campaign measures Code Health through the CodeScene MCP server, so each
measurement is recorded on the CodeScene side. The server is started from the "codescene"
entry of the repository's .mcp.json (the same one the harness uses) and spoken to with
JSON-RPC over stdin and stdout, one message per line.

    client = McpClient.from_repo(repo_root)
    text = client.call("code_health_review", {"file_path": "/abs/path/File.cs"})

Anything that goes wrong (no .mcp.json, no node, no token, a timeout, an error answer) raises
McpUnavailable, so the caller can fall back to the CodeScene CLI.
"""

from __future__ import annotations

import atexit
import json
import os
import queue
import re
import shutil
import subprocess
import threading
import time
from pathlib import Path


class McpUnavailable(Exception):
    pass


class McpToolError(McpUnavailable):
    """The server answered, but the tool reported an error."""


# The server answers "Access token is invalid or expired" when its calls come too fast or in
# parallel, although the same token works again after a pause (seen in round 5: about 85 s).
# Such an answer is retried after a pause before it counts as a failure.
THROTTLE_MESSAGE = "access token is invalid or expired"
RETRY_PAUSES = (20, 45, 90)


class McpClient:
    def __init__(self, command: list[str], env: dict[str, str], timeout: float = 90.0) -> None:
        self.timeout = timeout
        self._next_id = 0
        self._lines: "queue.Queue[str | None]" = queue.Queue()
        try:
            self.proc = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                         stderr=subprocess.DEVNULL, env=env, text=True,
                                         encoding="utf-8", errors="replace", bufsize=1)
        except OSError as exc:
            raise McpUnavailable(f"cannot start the MCP server: {exc}") from exc
        threading.Thread(target=self._pump, daemon=True).start()
        atexit.register(self.close)
        self._request("initialize", {
            "protocolVersion": "2024-11-05",
            "capabilities": {},
            "clientInfo": {"name": "refactoring-campaign", "version": "1"},
        })
        self._send({"jsonrpc": "2.0", "method": "notifications/initialized"})

    @classmethod
    def from_repo(cls, repo: Path, timeout: float = 90.0) -> "McpClient":
        # .mcp.json is not committed, so a git worktree does not have one: look in the repository
        # root first, then in each parent folder (the folder a session is opened in).
        override = os.environ.get("CS_MCP_CONFIG")
        candidates = [Path(override)] if override else [d / ".mcp.json" for d in [repo, *repo.parents]]
        server = None
        for config in candidates:
            try:
                server = json.loads(config.read_text(encoding="utf-8"))["mcpServers"]["codescene"]
                break
            except (OSError, KeyError, ValueError):
                continue
        if server is None:
            raise McpUnavailable("no codescene server in a .mcp.json at or above " + str(repo))
        command = server.get("command", "")
        exe = shutil.which(command) or command
        env = dict(os.environ, CS_DISABLE_VERSION_CHECK="1")
        for key, value in (server.get("env") or {}).items():
            env[key] = re.sub(r"\$\{(\w+)\}", lambda m: os.environ.get(m.group(1), ""), value)
        if not env.get("CS_ACCESS_TOKEN"):
            raise McpUnavailable("CS_ACCESS_TOKEN is not set")
        return cls([exe] + list(server.get("args", [])), env, timeout)

    def _pump(self) -> None:
        assert self.proc.stdout is not None
        for line in self.proc.stdout:
            self._lines.put(line)
        self._lines.put(None)

    def _send(self, message: dict) -> None:
        assert self.proc.stdin is not None
        try:
            self.proc.stdin.write(json.dumps(message) + "\n")
            self.proc.stdin.flush()
        except OSError as exc:
            raise McpUnavailable(f"the MCP server closed its input: {exc}") from exc

    def _request(self, method: str, params: dict) -> dict:
        self._next_id += 1
        wanted = self._next_id
        self._send({"jsonrpc": "2.0", "id": wanted, "method": method, "params": params})
        while True:
            try:
                line = self._lines.get(timeout=self.timeout)
            except queue.Empty as exc:
                raise McpUnavailable(f"{method} timed out after {self.timeout:.0f} s") from exc
            if line is None:
                raise McpUnavailable("the MCP server exited")
            try:
                message = json.loads(line)
            except ValueError:
                continue  # a log line, not a message
            if message.get("id") != wanted:
                continue  # a notification or an answer to something else
            if "error" in message:
                raise McpUnavailable(f"{method} failed: {message['error']}")
            return message.get("result", {})

    def call(self, tool: str, arguments: dict) -> str:
        for attempt in range(len(RETRY_PAUSES) + 1):
            try:
                return self._call_once(tool, arguments)
            except McpToolError as exc:
                if THROTTLE_MESSAGE not in str(exc).lower() or attempt == len(RETRY_PAUSES):
                    raise
                time.sleep(RETRY_PAUSES[attempt])
        raise AssertionError("unreachable")

    def _call_once(self, tool: str, arguments: dict) -> str:
        result = self._request("tools/call", {"name": tool, "arguments": arguments})
        if result.get("isError"):
            detail = " ".join(text.split())[:300]
            raise McpToolError(f"{tool} returned an error" + (f": {detail}" if detail else ""))
        return text

    def close(self) -> None:
        try:
            if self.proc.stdin:
                self.proc.stdin.close()
            self.proc.terminate()
        except OSError:
            pass


def parse_review(text: str) -> dict | None:
    """The review JSON in a tool answer, which can start with a version-update notice."""
    start = text.find("{")
    if start < 0:
        return None
    try:
        return json.JSONDecoder().raw_decode(text[start:])[0]
    except ValueError:
        return None
