// @module: reCart.Player.PlayerAutomationBridge
// @purpose: Debug/Test-only STA WinForms proxy form + Foundation.Automation loopback bridge forwarding UI tree, input, and snapshots to SDL frame boundaries.
// @depends-on: System, System.Collections.Concurrent, System.Collections.Generic, System.Diagnostics, System.Drawing, System.Linq, System.Security.Cryptography, System.Threading, System.Windows.Forms, Foundation.Automation, reCart.Contracts, reEngine.Host.Contracts, reEngine.Host.Core
// @exports: PlayerAutomationBridge

#if RECART_ENABLE_AUTOMATION
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using System.Windows.Forms;
using Foundation.Automation;
using reCart.Contracts;
using reEngine.Host.Contracts;
using reEngine.Host.Core;

namespace reCart.Player;

internal sealed class PlayerAutomationBridge : IHostAutomationBridge, IRenderSurfaceSnapshotProvider
{
    private const int MaxQueueSize = 256;
    private sealed record QueuedAction(bool IsKey, string? KeyOrTarget, int X, int Y, bool IsRightClick, IReadOnlyList<string>? Modifiers);

    private readonly ConcurrentQueue<QueuedAction> _queue = new();
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly int _width;
    private readonly int _height;
    private IHostAutomationTarget? _target;
    private AutomationServerOptions? _serverOptions;
    private Thread? _staThread;
    private ProxyHostForm? _proxyForm;
    private AutomationLoopbackServer? _server;
    private string _lastWidgetSignature = string.Empty;
    private bool _started;
    private volatile bool _disposing;

    public int ListeningPort => _server?.AutomationPort ?? _serverOptions?.AutomationPort ?? 0;

    public PlayerAutomationBridge(int width = 1280, int height = 720)
    {
        _width = Math.Max(320, width);
        _height = Math.Max(240, height);
    }

    public void Start(IHostAutomationTarget target, int port, string? token, string? allowedSnapshotRoot)
    {
        if (_disposing || _started) return;
        _target = target ?? throw new ArgumentNullException(nameof(target));
        string resolvedToken = !string.IsNullOrWhiteSpace(token)
            ? token.Trim()
            : (Environment.GetEnvironmentVariable("FOUNDATION_AUTOMATION_TOKEN")?.Trim()
               ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());

        _serverOptions = new AutomationServerOptions(port, resolvedToken, CartEngineConstants.AppId, allowedSnapshotRoot);
        _started = true;
        _staThread = new Thread(RunStaLoop) { IsBackground = true, Name = "reCart.AutomationProxy" };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("Timed out waiting for reCart PlayerAutomationBridge STA loopback server.");
    }

    public void DrainPendingCommands()
    {
        if (_target is null) return;
        int budget = 32;
        while (budget-- > 0 && _queue.TryDequeue(out QueuedAction? cmd))
        {
            if (cmd.IsKey && !string.IsNullOrWhiteSpace(cmd.KeyOrTarget))
            {
                _target.EnqueueKeyPress(cmd.KeyOrTarget, cmd.Modifiers);
            }
            else if (!cmd.IsKey)
            {
                if (cmd.IsRightClick && _target is HostSessionRunner runner)
                    runner.EnqueuePointerClick(cmd.X, cmd.Y, cmd.KeyOrTarget, isRightClick: true);
                else
                    _target.EnqueuePointerClick(cmd.X, cmd.Y, cmd.KeyOrTarget);
            }
        }
    }

    public void SyncWidgets(IReadOnlyList<HostWidgetBounds> widgets)
    {
        if (_disposing || _proxyForm is null || _proxyForm.IsDisposed || !_proxyForm.IsHandleCreated) return;
        string sig = BuildWidgetSignature(widgets);
        if (string.Equals(sig, _lastWidgetSignature, StringComparison.Ordinal)) return;
        _lastWidgetSignature = sig;
        try { _ = _proxyForm.BeginInvoke(() => _proxyForm.ApplyWidgets(widgets)); }
        catch (InvalidOperationException ex) { Trace.WriteLine($"Automation SyncWidgets skipped: {ex.Message}"); }
    }

    public bool TryCaptureRgba(out int width, out int height, out byte[] rgbaBytes)
    {
        if (_target is null)
        {
            width = 0; height = 0; rgbaBytes = Array.Empty<byte>();
            return false;
        }
        return _target.TryCaptureFramebufferPng(out width, out height, out rgbaBytes);
    }

    private void RunStaLoop()
    {
        try { Application.EnableVisualStyles(); }
        catch (InvalidOperationException ex) { Trace.WriteLine($"EnableVisualStyles skipped: {ex.Message}"); }

        using var form = new ProxyHostForm(this, _width, _height);
        _proxyForm = form;
        form.Load += (_, _) =>
        {
            IReadOnlyList<HostWidgetBounds> initial = _target!.GetOverlayWidgets();
            if (initial.Count > 0)
            {
                _lastWidgetSignature = BuildWidgetSignature(initial);
                form.ApplyWidgets(initial);
            }
            _server = new AutomationLoopbackServer(_serverOptions!, form, this);
            _server.Start();
            _ready.Set();
        };
        Application.Run(form);
    }

    private void Enqueue(QueuedAction action)
    {
        if (_queue.Count < MaxQueueSize) _queue.Enqueue(action);
    }

    private static string BuildWidgetSignature(IReadOnlyList<HostWidgetBounds> widgets) =>
        string.Join('|', widgets.Select(w => $"{w.ControlId}:{w.Bounds.X},{w.Bounds.Y},{w.Bounds.Width},{w.Bounds.Height}:{w.IsVisible}:{w.Label}"));

    public void Dispose()
    {
        if (_disposing) return;
        _disposing = true;
        _server?.Dispose();
        _server = null;
        if (_proxyForm is { IsDisposed: false, IsHandleCreated: true } pf)
        {
            try { _ = pf.BeginInvoke(() => pf.Close()); }
            catch (InvalidOperationException ex) { Trace.WriteLine($"Proxy form close skipped: {ex.Message}"); }
        }
        _staThread?.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    private sealed class ProxyHostForm : Form
    {
        private const int WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
        private readonly PlayerAutomationBridge _owner;
        private bool _ctrlHeld, _altHeld, _shiftHeld;

        public ProxyHostForm(PlayerAutomationBridge owner, int width, int height)
        {
            _owner = owner;
            Name = "SdlHostSurface";
            Text = $"{CartEngineConstants.DisplayName} SDL3 Host Surface";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            ClientSize = new Size(width, height);
            Opacity = 0.0;
        }

        public void ApplyWidgets(IReadOnlyList<HostWidgetBounds> widgets)
        {
            foreach (Control existing in Controls.Cast<Control>().ToArray())
            {
                Controls.Remove(existing);
                existing.Dispose();
            }

            foreach (HostWidgetBounds w in widgets.Where(w => w.ControlType is not ("Panel" or "Label")))
            {
                var btn = new KeyForwardButton(this) { Name = w.ControlId, Text = w.Label, Bounds = w.Bounds, Visible = w.IsVisible, Enabled = true };
                string id = w.ControlId;
                int cx = w.Bounds.X + (w.Bounds.Width / 2);
                int cy = w.Bounds.Y + (w.Bounds.Height / 2);
                btn.Click += (_, _) => _owner.Enqueue(new QueuedAction(false, id, cx, cy, false, null));
                btn.MouseUp += (_, me) =>
                {
                    if (me.Button == MouseButtons.Right) _owner.Enqueue(new QueuedAction(false, id, cx, cy, true, null));
                };
                Controls.Add(btn);
            }

            foreach (HostWidgetBounds w in widgets.Where(w => w.ControlType is "Panel" or "Label"))
            {
                var panel = new Panel { Name = w.ControlId, Text = w.Label, Bounds = w.Bounds, Visible = w.IsVisible, Enabled = true };
                if (string.Equals(w.ControlId, "HostViewport", StringComparison.Ordinal))
                    panel.MouseUp += (_, me) => _owner.Enqueue(new QueuedAction(false, null, me.X, me.Y, me.Button == MouseButtons.Right, null));
                Controls.Add(panel);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _owner.Enqueue(new QueuedAction(false, null, e.X, e.Y, e.Button == MouseButtons.Right, null));
        }

        internal void HandleKeyMessage(int msg, int vk)
        {
            bool isDown = msg is WmKeyDown or WmSysKeyDown;
            if (vk == 0x11) { _ctrlHeld = isDown; return; }
            if (vk == 0x12) { _altHeld = isDown; return; }
            if (vk == 0x10) { _shiftHeld = isDown; return; }
            if (!isDown) return;

            if (MapVkToName(vk) is { } keyName)
                _owner.Enqueue(new QueuedAction(true, keyName, 0, 0, false, GetActiveModifiers()));
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg is WmKeyDown or WmKeyUp or WmSysKeyDown or WmSysKeyUp) HandleKeyMessage(m.Msg, (int)m.WParam);
            base.WndProc(ref m);
        }

        private sealed class KeyForwardButton(ProxyHostForm parent) : Button
        {
            protected override void WndProc(ref Message m)
            {
                if (m.Msg is WmKeyDown or WmKeyUp or WmSysKeyDown or WmSysKeyUp) parent.HandleKeyMessage(m.Msg, (int)m.WParam);
                base.WndProc(ref m);
            }
        }

        private IReadOnlyList<string>? GetActiveModifiers()
        {
            Keys mods = Control.ModifierKeys;
            bool ctrl = _ctrlHeld || (mods & Keys.Control) != 0;
            bool alt = _altHeld || (mods & Keys.Alt) != 0;
            bool shift = _shiftHeld || (mods & Keys.Shift) != 0;
            if (!ctrl && !alt && !shift) return null;
            var list = new List<string>(3);
            if (ctrl) list.Add("CTRL");
            if (alt) list.Add("ALT");
            if (shift) list.Add("SHIFT");
            return list;
        }

        private static string? MapVkToName(int vk) => vk switch
        {
            >= 0x70 and <= 0x7B => $"F{vk - 0x6F}",
            0xC0 => "Grave", 0x1B => "ESC", 0x08 => "BACKSPACE", 0x09 => "TAB", 0x0D => "ENTER", 0x20 => "SPACE",
            0x21 => "PGUP", 0x22 => "PGDN", 0x23 => "END", 0x24 => "HOME",
            0x25 => "LEFT", 0x26 => "UP", 0x27 => "RIGHT", 0x28 => "DOWN", 0x2E => "DELETE",
            >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            >= 0x30 and <= 0x39 => ((char)vk).ToString(),
            _ => null
        };
    }
}
#endif
