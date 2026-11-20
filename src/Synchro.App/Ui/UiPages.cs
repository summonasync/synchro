using System.Net;
using System.Text;

namespace Synchro.App.Ui;

internal static class UiPages
{
    private const string Style = """
  :root { color-scheme: dark; }
  body { font-family: ui-monospace, 'Cascadia Code', Consolas, monospace; background: #0d1117; color: #e6edf3; margin: 0; padding: 24px; }
  h1 { font-size: 18px; letter-spacing: 2px; margin: 0 0 4px; }
  h2 { font-size: 13px; text-transform: uppercase; letter-spacing: 1px; color: #8b949e; margin: 28px 0 8px; border-bottom: 1px solid #21262d; padding-bottom: 4px; }
  h3 { font-size: 14px; margin: 0 0 8px; }
  .tagline { color: #8b949e; margin: 0 0 12px; }
  nav a { color: #58a6ff; text-decoration: none; margin-right: 16px; font-size: 13px; }
  nav a:hover { text-decoration: underline; }
  table { border-collapse: collapse; width: 100%; font-size: 13px; }
  td, th { border: 1px solid #21262d; padding: 6px 10px; text-align: left; vertical-align: top; }
  th { color: #8b949e; background: #161b22; }
  .ok { color: #3fb950; }
  .warn { color: #d29922; }
  .err { color: #f85149; }
  pre { background: #161b22; border: 1px solid #21262d; padding: 8px; margin: 4px 0; white-space: pre-wrap; word-break: break-word; font-size: 12px; }
  .pill { display: inline-block; padding: 1px 8px; border-radius: 10px; font-size: 11px; margin-left: 8px; }
  .pill.pending { background: #3d2e00; color: #d29922; }
  .pill.clean { background: #0f2c1a; color: #3fb950; }
  .pill.resolved { background: #12233f; color: #58a6ff; }
  .pill.flight { background: #2a1d3f; color: #bc8cff; }
  .card { border: 1px solid #21262d; border-radius: 6px; padding: 14px 16px; margin: 12px 0; background: #10161d; }
  .card .links { margin-top: 8px; font-size: 12px; }
  .card .links a { color: #58a6ff; text-decoration: none; margin-right: 14px; }
  .card .links a:hover { text-decoration: underline; }
  .sides { display: grid; grid-template-columns: repeat(3, 1fr); gap: 8px; margin: 8px 0; }
  .sides .side h4 { font-size: 11px; color: #8b949e; text-transform: uppercase; letter-spacing: 1px; margin: 0 0 4px; }
  button { font-family: inherit; font-size: 12px; background: #21262d; color: #e6edf3; border: 1px solid #30363d; border-radius: 4px; padding: 4px 12px; cursor: pointer; margin-right: 6px; }
  button:hover { background: #30363d; }
""";

    public static string Dashboard(IUiStore store)
    {
        var sb = new StringBuilder();
        foreach (var slave in store.GetSlaves())
        {
            var flight = slave.SyncInFlight ? "<span class=\"pill flight\">sync in flight</span>" : "";
            sb.Append($"""
<section class="card">
  <h3>{Esc(slave.Name)}{InboxPill(slave.SlaveInbox)}{flight}</h3>
  <table>
    <tr><th>path</th><td>{Esc(slave.Path)}</td></tr>
    <tr><th>sync branch</th><td>{Esc(slave.SyncBranch)}</td></tr>
    <tr><th>base branch</th><td>{Esc(slave.BaseBranch)}</td></tr>
    <tr><th>ledger</th><td>{Esc(slave.LedgerHint)}</td></tr>
    <tr><th>inbox (slave)</th><td>{InboxCell(slave.SlaveInbox)}</td></tr>
    <tr><th>inbox (master)</th><td>{InboxCell(slave.MasterInbox)}</td></tr>
    <tr><th>last op</th><td>{Esc(slave.LastOperation)}</td></tr>
  </table>
  <p class="links"><a href="/slave/{Url(slave.Name)}/history">history</a><a href="/slave/{Url(slave.Name)}/inbox">inbox</a><a href="/slave/{Url(slave.Name)}/conflicts">conflicts</a></p>
</section>
""");
        }

        return Layout("synchro — dashboard", "<h2>Slaves</h2>" + sb.ToString());
    }

    public static string History(string slaveName, IReadOnlyList<HistoryEntryView> events)
    {
        var rows = events.Count == 0
            ? "<span class=\"warn\">no sync events yet</span>"
            : "<table><tr><th>utc</th><th>direction</th><th>files</th><th>conflict resolution</th></tr>" +
              string.Join("", events.Select(e =>
                  $"<tr><td>{Esc(e.AtUtc.ToString("yyyy-MM-dd HH:mm:ss"))}</td><td>{Esc(e.Direction)}</td><td>{e.FileCount}</td><td>{Esc(e.ConflictResolution ?? "—")}</td></tr>")) +
              "</table>";

        return Layout($"synchro — {slaveName} history", $"<h2>Sync history · {Esc(slaveName)}</h2>{rows}");
    }

    public static string Inbox(SlaveView slave)
    {
        var body = $"""
<table>
  <tr><th>slave inbox</th><td>{InboxCell(slave.SlaveInbox)}</td></tr>
  <tr><th>master inbox</th><td>{InboxCell(slave.MasterInbox)}</td></tr>
  <tr><th>last sync op</th><td>{Esc(slave.LastOperation)}</td></tr>
</table>
""";
        return Layout($"synchro — {slave.Name} inbox", $"<h2>Pending inbox · {Esc(slave.Name)}</h2>{body}");
    }

    public static string Conflicts(SlaveConflictView scene)
    {
        var sb = new StringBuilder();
        foreach (var conflict in scene.Paths)
        {
            var pill = conflict.Resolution == "unresolved"
                ? "<span class=\"pill pending\">unresolved</span>"
                : $"<span class=\"pill resolved\">{Esc(conflict.Resolution)}</span>";
            var form = conflict.Resolution == "unresolved"
                ? $"""
<form method="post" action="/slave/{Url(scene.SlaveName)}/conflicts/resolve">
  <input type="hidden" name="path" value="{Esc(conflict.Path)}" />
  <button name="op" value="drop">drop</button>
  <button name="op" value="replace">replace</button>
  <button name="op" value="merge">merge</button>
</form>
"""
                : "";
            sb.Append($"""
<section class="card">
  <h3>{Esc(conflict.Path)}{pill}</h3>
  <div class="sides">
    <div class="side"><h4>theirs</h4><pre>{Esc(conflict.Theirs)}</pre></div>
    <div class="side"><h4>local</h4><pre>{Esc(conflict.Local)}</pre></div>
    <div class="side"><h4>base</h4><pre>{Esc(conflict.Base)}</pre></div>
  </div>
  {form}
</section>
""");
        }

        return Layout($"synchro — {scene.SlaveName} conflicts", $"<h2>Conflict scene · {Esc(scene.SlaveName)}</h2>{sb}");
    }

    private static string Layout(string title, string body) => $"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{Esc(title)}</title>
<style>{Style}</style>
</head>
<body>
<h1>SYNCHRO</h1>
<p class="tagline">master-slave direct sync · localhost console</p>
<nav><a href="/">dashboard</a><a href="/pack">pack console</a></nav>
{body}
</body>
</html>
""";

    private static string InboxPill(InboxView inbox) => inbox.State switch
    {
        "pending" => "<span class=\"pill pending\">inbox pending</span>",
        "missing" => "<span class=\"err\">repo missing</span>",
        _ => "<span class=\"pill clean\">inbox clean</span>",
    };

    private static string InboxCell(InboxView inbox) => inbox.State == "missing"
        ? "<span class=\"err\">repo missing — check the path</span>"
        : $"{Esc(inbox.Branch ?? "—")} · {inbox.DirtyFileCount} uncommitted · {InboxPill(inbox)}";

    private static string Esc(string value) => WebUtility.HtmlEncode(value);

    private static string Url(string value) => Uri.EscapeDataString(value);
}
