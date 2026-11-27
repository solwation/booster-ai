using System.Globalization;
using System.Text.Json;

namespace BoosterAI;

/// <summary>
/// The agent's side of the tab status channel (#248): one python3 script in the session folder that is both the stdio MCP
/// server (tools boosterai_status and boosterai_ask) and the fallback command <c>boosterai-tab status|ask</c>, plus how a tab
/// started with <see cref="TabTrack.StatusChannel"/> on exports the channel's variables and registers the server with its CLI.
/// python3 because the server must parse JSON-RPC reliably (bash cannot, and a jq or node would be a new dependency), the tab's
/// Linux already needs it for bs_pty and the status lines, and the BoosterAI binary itself is a Windows program on Windows,
/// out of reach of a WSL tab. Without python3 the tools and the command report nothing; the CLI starts as usual.
/// </summary>
public static class TabChannelAgent
{
    /// <summary>The MCP server's name; Claude Code calls its tools mcp__boosterai__boosterai_status / _ask.</summary>
    public const string ServerName = "boosterai";
    public const string CommandName = "boosterai-tab";
    /// <summary>The session's folder that a tab with the channel on puts first on PATH; it holds only <see cref="CommandName"/>.</summary>
    public const string BinDirectory = "bin";
    /// <summary>The session's Claude Code MCP configuration (<c>--mcp-config</c>); the paths of each tab come from its variables.</summary>
    public const string McpConfigFile = "boosterai-mcp.json";

    /// <summary>How long boosterai_ask and <c>boosterai-tab ask</c> wait by default, and at most, for the user's answer.</summary>
    public const int AskTimeoutSeconds = 900;
    public const int MaxAskTimeoutSeconds = 3600;
    /// <summary>Codex stops an MCP call after 60 s by default; the ask must be able to wait its longest time.</summary>
    public const int CodexToolTimeoutSeconds = MaxAskTimeoutSeconds + 60;

    /// <summary>
    /// The agent's instruction (at most about 60 words, it is sent with every request): Claude gets it with
    /// --append-system-prompt, Codex as developer_instructions. No ';' (Windows Terminal), no quote (TOML literal string).
    /// </summary>
    public const string Instruction =
        "First load boosterai_status with ToolSearch if deferred. Keep the user informed: before each new step (reading, planning, building, testing, waiting) "
        + "call it with state working and one short line in the language of the user, not after every tool call. "
        + "Finish with state done, or blocked with the reason. Use boosterai_ask only for questions the user alone can answer.";

    public static string CommandPath(string dir) => $"{dir}/{BinDirectory}/{CommandName}";
    public static string McpConfigPath(string dir) => $"{dir}/{McpConfigFile}";

    /// <summary>The CLIs that get the MCP server and the instruction; every other one has only the command on PATH.</summary>
    public static bool Registers(string llm) => llm is "claude" or "codex";

    /// <summary>The server for Claude Code: python3 -I (no PYTHON* variables, no user site) runs the session's script.</summary>
    public static string McpConfigJson(string dir) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["mcpServers"] = new Dictionary<string, object>
        {
            [ServerName] = new Dictionary<string, object>
            {
                ["type"] = "stdio",
                ["command"] = "python3",
                ["args"] = new[] { "-I", CommandPath(dir), "mcp" },
            },
        },
    }, new JsonSerializerOptions { WriteIndented = true }) + "\n";

    /// <summary>
    /// The step that exports the channel's variables and puts <see cref="BinDirectory"/> first on PATH (once, also after
    /// in-place restarts), ending in " &amp;&amp; "; "" when the channel is off, so the tab starts exactly as without it.
    /// Plain assignments, then export: no ';' and no '"' (see Launcher.TabScript).
    /// </summary>
    public static string ExportCommand(TabTrack track)
    {
        var env = TabChannel.Environment(track);
        if (env.Count == 0) return "";
        var bin = Launcher.Quote($"{track.Dir}/{BinDirectory}");
        var assignments = env.Select(p => p.Key + "=" + Launcher.Quote(p.Value)).Append($"PATH={bin}:${{PATH#{bin}:}}");
        return string.Join(' ', assignments) + " && export " + string.Join(' ', env.Keys.Append("PATH")) + " && ";
    }

    /// <summary>
    /// The CLI arguments that register the MCP server and give the instruction (with a leading space), or "" when the channel
    /// is off or the CLI has no such option. Claude Code: the session's --mcp-config, which adds to the user's own servers
    /// (no --strict-mcp-config), the server's tools allowed without asking, and --append-system-prompt. Codex: the server as
    /// -c mcp_servers.boosterai.* overrides that pass it the channel's variables, its calls
    /// approved and allowed to wait for an answer, and developer_instructions. Nothing in the user's own config files changes.
    /// </summary>
    public static string Arguments(string llm, TabTrack? track)
    {
        if (track is not { StatusChannel: true } || !Registers(llm)) return "";
        if (llm == "claude")
            return " --mcp-config " + Launcher.Quote(McpConfigPath(track.Dir)) + " --allowedTools mcp__" + ServerName
                + " --append-system-prompt " + Launcher.Quote(Instruction);
        // TOML literal strings ('...') need no double quotes; a path that cannot be one gets no server. Bare values that are no
        // TOML (python3, the instruction) are taken as strings by Codex. Its MCP servers get only some variables, so env_vars
        // passes the channel's on: they are exported in the tab.
        if (track.Dir.IndexOfAny(['\'', '\n', '\r']) >= 0) return "";
        static string List(IEnumerable<string> items) => "[" + string.Join(',', items.Select(s => "'" + s + "'")) + "]";
        var key = "mcp_servers." + ServerName + ".";
        string[] settings =
        [
            key + "command=python3",
            key + "args=" + List(["-I", CommandPath(track.Dir), "mcp"]),
            key + "env_vars=" + List(TabChannel.Environment(track).Keys),
            key + "tool_timeout_sec=" + CodexToolTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
            key + "default_tools_approval_mode=approve",
            "developer_instructions=" + Instruction,
        ];
        return string.Concat(settings.Select(s => " -c " + Launcher.Quote(s)));
    }

    /// <summary>
    /// Writes the script (bin/boosterai-tab, executable) and the Claude Code MCP configuration into a session folder.
    /// They are inert: only a tab started with the channel on uses them. <paramref name="dir"/> is the folder as the tab sees it.
    /// </summary>
    public static void Write(string sessionDirectory, string dir)
    {
        var bin = Path.Combine(sessionDirectory, BinDirectory);
        Directory.CreateDirectory(bin);
        var command = Path.Combine(bin, CommandName);
        File.WriteAllText(command, Script.Replace("\r\n", "\n") + "\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(command, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        File.WriteAllText(Path.Combine(sessionDirectory, McpConfigFile), McpConfigJson(dir));
    }

    /// <summary>bin/boosterai-tab. The format is TabChannel's (docs/DETAILS.md, "Tab status channel").</summary>
    public static readonly string Script = """"
#!/usr/bin/env python3
# BoosterAI tab status channel, the agent's side (#248). Written by BoosterAI into the session folder.
#   boosterai-tab status idle|working|blocked|done [text]
#   boosterai-tab ask [--option TEXT]... [--free | --no-free] [--timeout SECONDS] QUESTION
#   boosterai-tab mcp    (a stdio MCP server with the tools boosterai_status and boosterai_ask)
# It appends JSON lines to $BOOSTERAI_TAB_EVENTS and waits for $BOOSTERAI_TAB_ANSWERS/<id> (docs/DETAILS.md,
# "Tab status channel", format version 1). Plain files only: no network, nothing leaves the computer.
import json, os, random, string, sys, threading, time

VERSION = 1
STATES = ("idle", "working", "blocked", "done")
MAX_TEXT, MAX_OPTIONS, MAX_OPTION = 2000, 20, 200
ASK_DEFAULT, ASK_MAX = 900, 3600
POLL = 0.5


class ChannelError(Exception):
    pass


def now_ms():
    return int(time.time() * 1000)


def channel():
    events = os.environ.get("BOOSTERAI_TAB_EVENTS")
    answers = os.environ.get("BOOSTERAI_TAB_ANSWERS")
    if not events or not answers:
        raise ChannelError("Not in a BoosterAI tab with the status channel on (BOOSTERAI_TAB_EVENTS is not set).")
    try:
        protocol = int(os.environ.get("BOOSTERAI_TAB_PROTOCOL") or VERSION)
    except ValueError:
        protocol = 0
    if protocol < VERSION:
        raise ChannelError("BoosterAI's tab status channel uses another format version.")
    return events, answers


def append(event, at=None):
    events, _ = channel()
    line = dict([("v", VERSION), ("at", at or now_ms())] + list(event.items()))
    data = (json.dumps(line, ensure_ascii=False, separators=(",", ":")) + "\n").encode("utf-8")
    try:
        fd = os.open(events, os.O_WRONLY | os.O_APPEND | os.O_CREAT, 0o644)
        try:
            os.write(fd, data)  # one write of one whole line, so a reader never mixes two events
        finally:
            os.close(fd)
    except OSError as e:
        raise ChannelError("Could not write to BoosterAI's events file: %s" % e.strerror)
    return line["at"]


def clean(text, limit, lines=False):
    text = str(text if text is not None else "")
    if lines:  # keep line breaks, drop other control characters
        text = "\n".join("".join(c for c in l if c >= " " or c == "\t").rstrip() for l in text.replace("\r\n", "\n").split("\n")).strip()
    else:
        text = " ".join(text.split())
    return text[:limit]


def status(state, text=""):
    state = str(state or "").strip().lower()
    if state not in STATES:
        raise ChannelError("state must be one of: " + ", ".join(STATES))
    text = clean(text, MAX_TEXT)
    append({"kind": "status", "state": state, "text": text})
    return "BoosterAI shows the status " + state + (": " + text if text else "")


def read_answer(path, qid, at):
    try:
        with open(path, encoding="utf-8") as f:
            a = json.load(f)
    except (OSError, ValueError):
        return None
    if not isinstance(a, dict) or not isinstance(a.get("v"), int) or not 1 <= a["v"] <= VERSION:
        return None
    if a.get("id") != qid or a.get("question_at") != at or not isinstance(a.get("text"), str):
        return None
    return a["text"]


def ask(question, options=None, allow_free_text=None, timeout=None, cancelled=None):
    _, answers = channel()
    question = clean(question, MAX_TEXT, lines=True)
    if not question:
        raise ChannelError("question must not be empty")
    opts = []
    for o in options or []:
        o = clean(o, MAX_OPTION)
        if o and o not in opts and len(opts) < MAX_OPTIONS:
            opts.append(o)
    free = (not opts) if allow_free_text is None else bool(allow_free_text)
    if not opts and not free:
        raise ChannelError("give options or allow a free-text answer")
    try:
        timeout = ASK_DEFAULT if timeout is None else max(1, min(ASK_MAX, int(timeout)))
    except (TypeError, ValueError):
        timeout = ASK_DEFAULT
    qid = "q%x%s" % (now_ms(), "".join(random.choice(string.ascii_lowercase + string.digits) for _ in range(4)))
    # expires_at: when this side stops waiting, so BoosterAI can show the question as stale afterwards (#249).
    at = now_ms()
    append({"kind": "question", "id": qid, "text": question, "options": opts, "free": free, "expires_at": at + timeout * 1000}, at)
    path = os.path.join(answers, qid)
    until = at / 1000.0 + timeout
    while True:
        text = read_answer(path, qid, at)
        if text is not None:
            return True, text
        if cancelled is not None and cancelled.is_set():
            return False, "The question was cancelled."
        if time.time() >= until:
            minutes = timeout // 60
            return False, ("No answer from the user within %s. BoosterAI marks the question as expired and a later answer is not read: "
                           "continue with other work, or ask again later." % (("%d min" % minutes) if minutes else ("%d s" % timeout)))
        time.sleep(POLL)


TOOLS = [
    {
        "name": "boosterai_status",
        "description": "Tell BoosterAI, which runs this terminal tab, what you are doing. Call it with working and a short line in the language of the user each time you start a new step "
                       "(reading, planning, building, testing, waiting), not after every tool call. Also: done (finished, nothing left to do), "
                       "blocked (cannot continue without help) or idle. Returns at once.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "state": {"type": "string", "enum": list(STATES)},
                "text": {"type": "string", "description": "One short line for the user: what you are doing now, what is done or why you are blocked."},
            },
            "required": ["state"],
        },
        "annotations": {"title": "Report status to BoosterAI", "readOnlyHint": False, "destructiveHint": False,
                        "idempotentHint": True, "openWorldHint": False},
    },
    {
        "name": "boosterai_ask",
        "description": "Ask the user a question in BoosterAI's window and wait for the answer (up to 15 minutes by default). "
                       "Only for questions that only the user can answer. Returns the answer text, or says that no answer came.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "question": {"type": "string"},
                "options": {"type": "array", "items": {"type": "string"}, "maxItems": MAX_OPTIONS,
                            "description": "Answers the user can pick with one click."},
                "allow_free_text": {"type": "boolean", "description": "Allow a typed answer. Default: true without options, false with them."},
                "timeout_seconds": {"type": "integer", "minimum": 1, "maximum": ASK_MAX, "description": "How long to wait (default %d)." % ASK_DEFAULT},
            },
            "required": ["question"],
        },
        "annotations": {"title": "Ask the user through BoosterAI", "readOnlyHint": False, "destructiveHint": False,
                        "idempotentHint": False, "openWorldHint": False},
    },
]
PROTOCOLS = ("2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05")


class Server:
    def __init__(self):
        self.lock = threading.Lock()
        self.pending = {}

    def send(self, message):
        data = json.dumps(message, ensure_ascii=False, separators=(",", ":")) + "\n"
        with self.lock:
            sys.stdout.write(data)
            sys.stdout.flush()

    def reply(self, mid, result=None, error=None):
        message = {"jsonrpc": "2.0", "id": mid}
        if error is not None:
            message["error"] = {"code": error[0], "message": error[1]}
        else:
            message["result"] = result
        self.send(message)

    def call(self, mid, params, cancelled):
        name = params.get("name")
        args = params.get("arguments") or {}
        try:
            if not isinstance(args, dict):
                raise ChannelError("arguments must be an object")
            if name == "boosterai_status":
                ok, text = True, status(args.get("state"), args.get("text") or "")
            elif name == "boosterai_ask":
                options = args.get("options")
                if options is not None and not isinstance(options, list):
                    raise ChannelError("options must be a list of strings")
                ok, text = ask(args.get("question"), options, args.get("allow_free_text"), args.get("timeout_seconds"), cancelled)
                if ok:
                    text = "The user answered: " + text
                if cancelled.is_set():
                    return
            else:
                self.reply(mid, error=(-32602, "Unknown tool: %s" % name))
                return
            self.reply(mid, {"content": [{"type": "text", "text": text}], "isError": False})
        except ChannelError as e:
            self.reply(mid, {"content": [{"type": "text", "text": str(e)}], "isError": True})
        finally:
            with self.lock:
                self.pending.pop(mid, None)

    def handle(self, m):
        if not isinstance(m, dict):
            return
        method, mid, params = m.get("method"), m.get("id"), m.get("params") or {}
        if not isinstance(params, dict):
            params = {}
        if method is None:
            return  # a response to nothing we sent
        if mid is None:
            if method == "notifications/cancelled":
                with self.lock:
                    event = self.pending.get(params.get("requestId"))
                if event is not None:
                    event.set()
            return
        if method == "initialize":
            asked = params.get("protocolVersion")
            self.reply(mid, {"protocolVersion": asked if asked in PROTOCOLS else PROTOCOLS[0],
                             "capabilities": {"tools": {"listChanged": False}},
                             "serverInfo": {"name": "boosterai", "title": "BoosterAI tab status", "version": str(VERSION)}})
        elif method == "ping":
            self.reply(mid, {})
        elif method == "tools/list":
            self.reply(mid, {"tools": TOOLS})
        elif method == "tools/call":
            cancelled = threading.Event()
            with self.lock:
                self.pending[mid] = cancelled
            if params.get("name") == "boosterai_ask":
                threading.Thread(target=self.call, args=(mid, params, cancelled), daemon=True).start()
            else:
                self.call(mid, params, cancelled)
        else:
            self.reply(mid, error=(-32601, "Method not found: %s" % method))

    def run(self):
        for line in sys.stdin:
            line = line.strip()
            if not line:
                continue
            try:
                message = json.loads(line)
            except ValueError:
                self.reply(None, error=(-32700, "Parse error"))
                continue
            for m in message if isinstance(message, list) else [message]:
                self.handle(m)


def main(argv):
    import argparse
    parser = argparse.ArgumentParser(prog="boosterai-tab", description="Report status to BoosterAI or ask the user a question in BoosterAI.")
    sub = parser.add_subparsers(dest="command")
    s = sub.add_parser("status", help="report idle, working, blocked or done")
    s.add_argument("state", choices=STATES)
    s.add_argument("text", nargs="*")
    a = sub.add_parser("ask", help="ask the user and print the answer (exit 2 when none came)")
    a.add_argument("--option", action="append", default=[], help="an answer to pick (repeatable)")
    a.add_argument("--free", dest="free", action="store_true", default=None, help="allow a typed answer")
    a.add_argument("--no-free", dest="free", action="store_false", help="only the options")
    a.add_argument("--timeout", type=int, default=None, help="seconds to wait (default %d)" % ASK_DEFAULT)
    a.add_argument("question", nargs="+")
    sub.add_parser("mcp", help="run the stdio MCP server")
    args = parser.parse_args(argv)
    try:
        if args.command == "status":
            print(status(args.state, " ".join(args.text)))
            return 0
        if args.command == "ask":
            ok, text = ask(" ".join(args.question), args.option, args.free, args.timeout)
            if ok:
                print(text)
                return 0
            print(text, file=sys.stderr)
            return 2
        if args.command == "mcp":
            Server().run()
            return 0
    except ChannelError as e:
        print("boosterai-tab: " + str(e), file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        return 130
    parser.print_help(sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
"""";
}
