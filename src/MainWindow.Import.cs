using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Cursors;

/// <summary>The Add pack menu and importing a cursor set from a web link.</summary>
internal sealed partial class MainWindow
{
    private bool _linkImportBusy;

    private void ShowAddMenu(Card card)
    {
        var point = MousePosition;
        if (_keyboardFocus)
        {
            var r = Rectangle.Round(ScreenRect(card));
            point = PointToScreen(new Point(r.X + r.Width / 2, r.Y + r.Height / 2));
        }

        IntPtr menu = Native.CreatePopupMenu();
        try
        {
            Native.AppendMenu(menu, Native.MF_STRING, (UIntPtr)1, "From files…\tCtrl+O");
            Native.AppendMenu(menu, Native.MF_STRING, (UIntPtr)2, "From a link…\tCtrl+L");
            Native.SetForegroundWindow(Handle);
            int command = Native.TrackPopupMenuEx(menu, Native.TPM_RETURNCMD | Native.TPM_RIGHTBUTTON, point.X, point.Y, Handle, IntPtr.Zero);
            if (command == 1) BrowseForPacks();
            else if (command == 2) PromptImportLink();
        }
        finally
        {
            Native.DestroyMenu(menu);
        }
    }

    private void PromptImportLink(string link = null)
    {
        if (link == null)
        {
            try
            {
                string clip = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : null;
                if (LinkImport.LooksLikeLink(clip)) link = clip;
            }
            catch { }
        }
        using var dialog = new LinkDialog(_fonts, S, link);
        if (dialog.ShowDialog(this) == DialogResult.OK) ImportLinkAsync(dialog.Link);
    }

    /// <summary>A link dragged from a browser (URL formats or plain text).</summary>
    private static string DroppedLink(IDataObject data)
    {
        foreach (string format in new[] { "UniformResourceLocatorW", "UniformResourceLocator", DataFormats.UnicodeText, DataFormats.Text })
        {
            try
            {
                if (!data.GetDataPresent(format)) continue;
                string text = data.GetData(format) switch
                {
                    string s => s,
                    System.IO.MemoryStream ms => format.EndsWith("W") ? System.Text.Encoding.Unicode.GetString(ms.ToArray()) : System.Text.Encoding.Default.GetString(ms.ToArray()),
                    _ => null,
                };
                text = text?.Split(new[] { '\0', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
                if (LinkImport.LooksLikeLink(text)) return text;
            }
            catch { }
        }
        return null;
    }

    private void ImportLinkAsync(string link)
    {
        if (_linkImportBusy)
        {
            ShowToast("Still importing the last link");
            return;
        }
        _linkImportBusy = true;
        ShowToast("Downloading cursors…");
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var result = LinkImport.Import(link);
            try
            {
                BeginInvoke((Action)(() =>
                {
                    _linkImportBusy = false;
                    if (result.Error != null)
                    {
                        ShowToast(result.Error);
                        return;
                    }
                    OnImported(result.Import);
                    if (result.Import.AddedIds.Count == 1 && result.License != null &&
                        _allCards.FirstOrDefault(c => c.Pack?.Id == result.Import.AddedIds[0])?.Pack is CursorPack pack)
                        ShowToast($"Added “{pack.Label}” · {result.License}");
                }));
            }
            catch (InvalidOperationException) { }
        });
    }
}

/// <summary>Small dark dialog that asks for a cursor set link.</summary>
internal sealed class LinkDialog : Form
{
    private readonly TextBox _box;
    private readonly Button _import;

    public string Link => _box.Text.Trim();

    public LinkDialog(UiFonts fonts, float s, string link)
    {
        int Px(float v) => (int)Math.Round(v * s);
        Text = "Add from a link";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = fonts.Label;
        ClientSize = new Size(Px(460), Px(168));

        Controls.Add(new Label
        {
            Text = "Paste a link to a cursor set page (for example on rw-designer.com), or to a .zip, .cur or .ani file.",
            ForeColor = Theme.TextSecondary,
            Bounds = new Rectangle(Px(20), Px(16), Px(420), Px(40)),
        });

        var frame = new Panel { BackColor = Theme.Rgb(0x262626), Bounds = new Rectangle(Px(20), Px(62), Px(420), Px(36)) };
        frame.Paint += (_, e) =>
        {
            using var pen = new Pen(_box.Focused ? Color.FromArgb(0x6A, 0x6A, 0x6A) : Color.FromArgb(0x38, 0x38, 0x38));
            e.Graphics.DrawRectangle(pen, 0, 0, frame.Width - 1, frame.Height - 1);
        };
        _box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = frame.BackColor,
            ForeColor = Theme.Text,
            Text = link ?? "",
        };
        _box.Bounds = new Rectangle(Px(10), (frame.Height - _box.PreferredHeight) / 2, frame.Width - Px(20), _box.PreferredHeight);
        _box.GotFocus += (_, _) => frame.Invalidate();
        _box.LostFocus += (_, _) => frame.Invalidate();
        frame.Controls.Add(_box);
        Controls.Add(frame);

        _import = MakeButton("Import", primary: true, new Rectangle(Px(340), Px(116), Px(100), Px(32)));
        _import.DialogResult = DialogResult.OK;
        var cancel = MakeButton("Cancel", primary: false, new Rectangle(Px(230), Px(116), Px(100), Px(32)));
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(_import);
        Controls.Add(cancel);
        AcceptButton = _import;
        CancelButton = cancel;

        _box.TextChanged += (_, _) => _import.Enabled = LinkImport.LooksLikeLink(_box.Text);
        _import.Enabled = LinkImport.LooksLikeLink(_box.Text);
        Shown += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.ApplyDarkFrame(Handle, Native.ToColorRef(Theme.FrameBorder));
    }

    private static Button MakeButton(string text, bool primary, Rectangle bounds)
    {
        var button = new Button
        {
            Text = text,
            Bounds = bounds,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Theme.ChipSelected : Theme.Rgb(0x2D2D2D),
            ForeColor = primary ? Theme.ChipSelectedText : Theme.Text,
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderColor = primary ? Theme.ChipSelected : Theme.Rgb(0x3A3A3A);
        button.FlatAppearance.MouseOverBackColor = primary ? Theme.Rgb(0xF2F2F2) : Theme.Rgb(0x353535);
        button.FlatAppearance.MouseDownBackColor = primary ? Theme.Rgb(0xD6D6D6) : Theme.Rgb(0x2A2A2A);
        button.EnabledChanged += (_, _) =>
        {
            // Flat buttons keep their colors when disabled, so a disabled Import would still look clickable.
            bool bright = primary && button.Enabled;
            button.BackColor = bright ? Theme.ChipSelected : Theme.Rgb(0x2D2D2D);
            button.FlatAppearance.BorderColor = bright ? Theme.ChipSelected : Theme.Rgb(0x3A3A3A);
            button.ForeColor = button.Enabled ? (primary ? Theme.ChipSelectedText : Theme.Text) : Theme.TextInactive;
        };
        return button;
    }
}
