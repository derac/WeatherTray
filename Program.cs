using System;
using System.Drawing;
using System.Drawing.Text;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace WeatherTray;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new WeatherApplicationContext());
    }
}

public class WeatherApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Timer _timer;
    private readonly WeatherService _weatherService;
    private Settings _settings;
    private WeatherDashboardForm? _dashboardForm;

    public WeatherApplicationContext()
    {
        _settings = Settings.Load();
        _weatherService = new WeatherService();

        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Text = "Fetching weather...",
            Icon = SystemIcons.Application
        };

        // Double-click to open dashboard
        _notifyIcon.DoubleClick += OnOpenDashboard;

        // Initialize context menu with just "Exit"
        // User requested removing everything else. 
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Exit", null, (s, e) => 
        {
            _notifyIcon.Visible = false;
            Application.Exit();
        });
        
        // Fix for "Top Middle" positioning bug on Windows 11:
        // Instead of assigning to _notifyIcon.ContextMenuStrip (which relies on auto-positioning),
        // we manually show it at the cursor position on Right Click.
        _notifyIcon.MouseUp += (s, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                // Reflection hack to ensure the menu behaves like a proper system tray menu (closes when clicking away)
                // However, standard Show(Cursor.Position) is usually "good enough" for simple apps.
                // A known trick to fix "click away" focus issues is to activate the dummy form (ApplicationContext doesn't have one usually).
                // But let's try standard Show first.
                // Actually, standard ContextMenuStrip.Show() often leaves the menu stuck if you click away.
                // The most robust way is to assign it, but if that's buggy...
                // Let's force the position by using the SetForegroundWindow trick if possible, or just Show.
                
                // Let's try simple Show first as requested to fix Position.
                contextMenu.Show(Cursor.Position);
            }
        };

        _timer = new Timer();
        _timer.Interval = 15 * 60 * 1000; // Update every 15 minutes
        _timer.Tick += async (s, e) => await UpdateWeatherAsync();
        _timer.Start();

        // Initial update - check for first launch and get IP location
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        // If first launch, try to get location from IP
        if (!_settings.HasBeenConfigured)
        {
            try
            {
                var ipLocation = await _weatherService.GetLocationFromIPAsync();
                if (ipLocation != null)
                {
                    _settings.Latitude = ipLocation.Latitude;
                    _settings.Longitude = ipLocation.Longitude;
                    _settings.LocationName = ipLocation.DisplayName;
                    _settings.HasBeenConfigured = true;
                    _settings.Save();
                }
            }
            catch { }
        }
        
        await UpdateWeatherAsync();
    }

    // UpdateContextMenu removed as it's no longer needed (static menu)

    private void OnOpenDashboard(object? sender, EventArgs e)
    {
        if (_dashboardForm == null || _dashboardForm.IsDisposed)
        {
            _dashboardForm = new WeatherDashboardForm(_weatherService, _settings, OnSettingsChanged);
            // Set initial icon
            if (_notifyIcon.Icon != null)
            {
                _dashboardForm.UpdateAppIcon(_notifyIcon.Icon);
            }
        }

        if (!_dashboardForm.Visible)
        {
            _dashboardForm.Show();
            _dashboardForm.BringToFront();
        }
        else
        {
            _dashboardForm.BringToFront();
            _dashboardForm.Focus();
        }
    }

    private async void OnSettingsChanged(Settings newSettings)
    {
        _settings = newSettings;
        // No need to update context menu anymore
        await UpdateWeatherAsync();
    }

    private async Task UpdateWeatherAsync()
    {
        try
        {
            var data = await _weatherService.FetchExtendedWeatherAsync(_settings.Latitude, _settings.Longitude);
            UpdateTrayIcon(data.Current.Temperature);
        }
        catch (Exception ex)
        {
            _notifyIcon.Text = $"Error: {ex.Message}";
        }
    }

    private void UpdateTrayIcon(double temperature)
    {
        int tempInt = (int)Math.Round(temperature);
        string text = $"{tempInt}";

        // Create a higher resolution bitmap (e.g. 48x48 or 64x64) to look good on all DPIs
        int sizePx = 256;
        using var bitmap = new Bitmap(sizePx, sizePx);
        using var g = Graphics.FromImage(bitmap);
        
        // Clear with transparent background
        g.Clear(Color.Transparent);
        
        // Clear with transparent background
        g.Clear(Color.Transparent);
        
        // Use high-quality rendering
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        using var fontFamily = new FontFamily("Segoe UI");
        
        // Add text to path at an arbitrary size (e.g. 100)
        // We will scale it strictly based on bounds
        path.AddString(
            text, 
            fontFamily, 
            (int)FontStyle.Bold, 
            100, 
            new Point(0, 0), 
            StringFormat.GenericDefault);

        // Get the tight bounds of the text pixels
        RectangleF bounds = path.GetBounds();

        // Calculate scale factor to fit within sizePx (minus small margin)
        // Increased margin to prevent right-side clipping as per user feedback
        float margin = 32f; 
        float targetSize = sizePx - margin;
        
        // Prevention against empty bounds (e.g. empty string)
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            float scaleX = targetSize / bounds.Width;
            float scaleY = targetSize / bounds.Height;
            
            // Choose the smaller scale to fit both dimensions (uniform scaling)
            // Or if user wants to fill left-to-right primarily, we might prioritize X? 
            // But preserving aspect ratio is usually safer to look "correct".
            // However, filling "as much as possible left to right" suggests we should fit width if possible.
            // Using Min ensures we don't clip height either.
            float scale = Math.Min(scaleX, scaleY);

            // Create transformation matrix
            using var matrix = new System.Drawing.Drawing2D.Matrix();
            
            // 1. Move to origin (0,0)
            matrix.Translate(-bounds.Left, -bounds.Top);
            
            // 2. Scale
            matrix.Scale(scale, scale);
            
            // 3. Center in the bitmap
            float finalWidth = bounds.Width * scale;
            float finalHeight = bounds.Height * scale;
            float offsetX = (sizePx - finalWidth) / 2;
            float offsetY = (sizePx - finalHeight) / 2;
            
            matrix.Translate(offsetX, offsetY, System.Drawing.Drawing2D.MatrixOrder.Append);
            
            // Apply transform
            path.Transform(matrix);
        }

        using var brush = new SolidBrush(Color.White);
        g.FillPath(brush, path);
        
        // Convert to Icon
        IntPtr hIcon = bitmap.GetHicon();
        using var icon = Icon.FromHandle(hIcon);
        
        _notifyIcon.Icon = (Icon)icon.Clone();
        _notifyIcon.Text = $"{_settings.LocationName}: {temperature}°F";
        
        // Update dashboard icon if it exists
        if (_dashboardForm != null && !_dashboardForm.IsDisposed)
        {
            _dashboardForm.UpdateAppIcon(_notifyIcon.Icon);
        }
        
        DestroyIcon(hIcon);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    extern static bool DestroyIcon(IntPtr handle);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Dispose();
            _timer.Dispose();
            _weatherService.Dispose();
            _dashboardForm?.Dispose();
        }
        base.Dispose(disposing);
    }
}
