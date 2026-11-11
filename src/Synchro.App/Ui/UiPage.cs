namespace Synchro.App.Ui;

internal static class UiPage
{
    public const string Html = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>synchro — status</title>
<style>
  :root { color-scheme: dark; }
  body { font-family: ui-monospace, 'Cascadia Code', Consolas, monospace; background: #0d1117; color: #e6edf3; margin: 0; padding: 24px; }
  h1 { font-size: 18px; letter-spacing: 2px; margin: 0 0 4px; }
  h2 { font-size: 13px; text-transform: uppercase; letter-spacing: 1px; color: #8b949e; margin: 28px 0 8px; border-bottom: 1px solid #21262d; padding-bottom: 4px; }
  .tagline { color: #8b949e; margin: 0 0 20px; }
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
</style>
</head>
<body>
<h1>SYNCHRO</h1>
<p class="tagline">high-level code sync without git transport</p>

<h2>Store status</h2>
<div id="status">loading…</div>

<h2>Conflict scene</h2>
<div id="scene">loading…</div>

<h2>Sync history</h2>
<div id="log">loading…</div>

<script>
async function load() {
  const status = await fetch('/api/status').then(r => r.json());
  const st = document.getElementById('status');
  if (!status.ok) { st.innerHTML = '<span class="err">' + esc(status.error) + '</span>'; }
  else {
    const pill = status.pendingScene
      ? '<span class="pill pending">scene pending</span>'
      : '<span class="pill clean">idle</span>';
    st.innerHTML = '<table>' +
      row('channel', esc(status.channel) + ' → ' + esc(status.peer) + ' ' + pill) +
      row('mode', esc(status.mode)) +
      row('peer head', esc(status.peerHeadPosition ?? '—')) +
      row('peer knows us at', esc(status.peerKnowsUsAt ?? '—')) +
      row('chain seq', esc(status.appliedChainSeq)) +
      row('ledger events', esc(status.ledgerEvents)) +
      row('drifted files', status.driftedFiles.length === 0
        ? '<span class="ok">none</span>'
        : '<span class="warn">' + status.driftedFiles.length + '</span> ' + esc(status.driftedFiles.join(', '))) +
      '</table>';
  }

  const scene = await fetch('/api/scene').then(r => r.json());
  const sc = document.getElementById('scene');
  if (!scene.ok) { sc.textContent = scene.error; }
  else {
    sc.innerHTML = '<table><tr><th>path</th><th>resolution</th></tr>' +
      scene.conflicts.map(c => '<tr><td>' + esc(c.path) + '</td><td>' + esc(c.resolution) + '</td></tr>').join('') +
      '</table>' +
      scene.conflicts.map(c => readBox(c.path)).join('');
  }

  const log = await fetch('/api/log').then(r => r.json());
  const lg = document.getElementById('log');
  lg.innerHTML = log.ok && log.events.length > 0
    ? '<table><tr><th>utc</th><th>direction</th><th>local</th><th>peer</th><th>package</th></tr>' +
      log.events.map(e => '<tr><td>' + esc(e.AtUtc ?? '') + '</td><td>' + esc(e.direction ?? '') +
        '</td><td>' + esc(e.localPosition ?? '—') + '</td><td>' + esc(e.peerPosition ?? '—') +
        '</td><td>' + esc(e.packageDigest ?? '—') + '</td></tr>').join('') + '</table>'
    : '<span class="warn">no sync events yet</span>';
}

function readBox(path) {
  fetch('/api/scene/read?path=' + encodeURIComponent(path))
    .then(r => r.json())
    .then(d => {
      const box = document.getElementById('read-' + cssSafe(path));
      if (!box) return;
      box.innerHTML =
        '<tr><th>theirs</th></tr><tr><td><pre>' + esc(d.theirs ?? '— (deletion)') + '</pre></td></tr>' +
        '<tr><th>base</th></tr><tr><td><pre>' + esc(d.baseVersion ?? '—') + '</pre></td></tr>' +
        '<tr><th>ours</th></tr><tr><td><pre>' + esc(d.ours ?? '—') + '</pre></td></tr>';
    });
  return '<table id="read-' + cssSafe(path) + '"><tr><th>' + esc(path) + ' — three versions</th></tr></table>';
}

function row(k, v) { return '<tr><th>' + k + '</th><td>' + v + '</td></tr>'; }
function cssSafe(s) { return s.replace(/[^a-zA-Z0-9]/g, '_'); }
function esc(s) { const d = document.createElement('div'); d.textContent = String(s ?? ''); return d.innerHTML; }

load().catch(() => {
  document.getElementById('status').innerHTML = '<span class="err">status unreachable</span>';
});
setInterval(() => load().catch(() => { }), 15000);
</script>
</body>
</html>
""";
}
