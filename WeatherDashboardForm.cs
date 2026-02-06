using System.Drawing;
using System.Windows.Forms;
using System.Net.Http;

namespace WeatherTrayApp;

/// <summary>
/// Weather dashboard window showing extended forecast information.
/// Borderless window with custom dragging.
/// </summary>
public class WeatherDashboardForm : Form
{
    private readonly WeatherService _weatherService;
    private readonly Settings _settings;
    private readonly Action<Settings> _onSettingsChanged;
    private readonly HttpClient _httpClient = new();

    // For window dragging
    private bool _dragging = false;
    private Point _dragStart;

    // UI Controls
    private TextBox _locationTextBox = null!;
    // private Button _searchButton = null!; // Removed
    private Label _locationLabel = null!;
    private Panel _currentPanel = null!;
    private Panel _radarPanel = null!;
    private PictureBox _radarImage = null!;
    private TableLayoutPanel _hourlyContainer = null!;
    private FlowLayoutPanel _dailyContainer = null!;
    // Current weather labels
    private Label _tempLabel = null!;
    private Label _weatherDescLabel = null!;
    private Label _feelsLikeLabel = null!;
    private Label _windLabel = null!;
    private Label _humidityLabel = null!;
    private Label _uvLabel = null!;

    private WeatherData? _currentData;

    // Radar map zoom/pan state
    private double _radarZoom = 2.5; // degrees radius (smaller = more zoomed in)
    private double _radarCenterLat;
    private double _radarCenterLon;
    private bool _radarDragging = false;
    private Point _radarDragStart;
    private double _radarDragStartLat;
    private double _radarDragStartLon;
    private Image? _radarOriginalImage; // Store original for pan preview

    public WeatherDashboardForm(WeatherService weatherService, Settings settings, Action<Settings> onSettingsChanged)
    {
        _weatherService = weatherService;
        _settings = settings;
        _onSettingsChanged = onSettingsChanged;

        InitializeComponents();
        
        // Refresh data whenever window is shown
        VisibleChanged += async (s, e) => {
            if (Visible) await LoadWeatherDataAsync();
        };

        _ = LoadWeatherDataAsync(); // Initial load
    }

    public void UpdateAppIcon(Icon icon)
    {
        // Clone the icon to ensure we have our own copy that won't be disposed externally
        var oldIcon = Icon;
        Icon = (Icon)icon.Clone();
        // Don't dispose oldIcon if it's the default null/system one, but if we set it, we should.
        // Actually, Form.Icon handles disposal usually, but let's be safe.
    }

    private void InitializeComponents()
    {
        Text = "Weather Dashboard";
        Size = new Size(550, 600); // 550x600 makes right panels roughly 300x290 (square)
        MinimumSize = new Size(500, 500);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(25, 28, 35);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9);
        FormBorderStyle = FormBorderStyle.None;

        MouseDown += OnFormMouseDown;
        MouseMove += OnFormMouseMove;
        MouseUp += OnFormMouseUp;

        // Main layout - 2 columns
        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(8, 6, 8, 6),
            BackColor = Color.Transparent,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45)); // Left (Current + Hourly)
        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); // Right (Radar + Daily) - ~300px width
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));   // Location bar
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 52));    // Current + Radar
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 48));    // Hourly + Daily

        mainPanel.MouseDown += OnFormMouseDown;
        mainPanel.MouseMove += OnFormMouseMove;
        mainPanel.MouseUp += OnFormMouseUp;

        // Location bar (spans 2 columns)
        var locationBar = CreateLocationBar();
        mainPanel.Controls.Add(locationBar, 0, 0);
        mainPanel.SetColumnSpan(locationBar, 2);

        // Current conditions panel (left)
        _currentPanel = CreateCurrentConditionsPanel();
        mainPanel.Controls.Add(_currentPanel, 0, 1);

        // Radar panel (right)
        _radarPanel = CreateRadarPanel();
        mainPanel.Controls.Add(_radarPanel, 1, 1);

        // Hourly forecast panel (left)
        var hourlyPanel = CreateHourlyPanel();
        mainPanel.Controls.Add(hourlyPanel, 0, 2);

        // Daily forecast panel (right)
        var dailyPanel = CreateDailyPanel();
        mainPanel.Controls.Add(dailyPanel, 1, 2);

        Controls.Add(mainPanel);

        FormClosing += (s, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    #region Window Dragging
    private void OnFormMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _dragStart = e.Location;
        }
    }

    private void OnFormMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            var screenPos = PointToScreen(e.Location);
            Location = new Point(screenPos.X - _dragStart.X, screenPos.Y - _dragStart.Y);
        }
    }

    private void OnFormMouseUp(object? sender, MouseEventArgs e)
    {
        _dragging = false;
    }

    private void EnableDragging(Control control)
    {
        // Recursively apply to all children
        foreach (Control child in control.Controls)
        {
            EnableDragging(child);
        }

        // Apply drag handlers only to non-interactive controls
        // Skip TextBox (input), Button (interact), and the Radar Map (pan/zoom)
        if (control is TextBox || control is Button || control == _radarImage)
        {
            return;
        }

        control.MouseDown += OnFormMouseDown;
        control.MouseMove += OnFormMouseMove;
        control.MouseUp += OnFormMouseUp;
    }
    #endregion

    private Panel CreateLocationBar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        // Adjusted columns: Location Label, TextBox (Fill), Startup Toggle, Close
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // Location label
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // TextBox (fill)
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 45)); // Startup Toggle
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 45)); // Close
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _locationLabel = new Label
        {
            Text = $"📍 {_settings.LocationName}",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(5, 0, 10, 0)
        };

        _locationTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 14), // Larger font for taller input
            PlaceholderText = "City...",
            BackColor = Color.FromArgb(45, 50, 60),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.None,
            Margin = new Padding(0, 7, 5, 5) // Adjusted for 45px row height
        };
        _locationTextBox.KeyPress += (s, e) =>
        {
            if (e.KeyChar == (char)Keys.Enter)
            {
                e.Handled = true;
                _ = SearchAndUpdateLocationAsync();
            }
        };

        // Sleek startup toggle button
        var startupButton = CreateHeaderButton("⊞", null!); 
        startupButton.Click += (s, e) => {
            bool newState = !Settings.IsStartupEnabled;
            Settings.SetStartupEnabled(newState);
            startupButton.ForeColor = newState ? Color.LightGreen : Color.Gray;
        };
        startupButton.ForeColor = Settings.IsStartupEnabled ? Color.LightGreen : Color.Gray;
        var toolTip = new ToolTip();
        toolTip.SetToolTip(startupButton, "Start with Windows");

        var closeButton = CreateHeaderButton("✕", () => Hide(), true);

        // Add to panel
        panel.Controls.Add(_locationLabel, 0, 0);
        panel.Controls.Add(_locationTextBox, 1, 0);
        panel.Controls.Add(startupButton, 2, 0);
        panel.Controls.Add(closeButton, 3, 0);

        EnableDragging(panel);

        return panel;
    }

    private Button CreateHeaderButton(string text, Action onClick, bool isRed = false)
    {
        var btn = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = isRed ? Color.FromArgb(160, 50, 50) : Color.FromArgb(50, 55, 70),
            ForeColor = isRed ? Color.White : Color.LightGray,
            Margin = new Padding(1),
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 10)
        };
        btn.FlatAppearance.BorderSize = 0;
        if (onClick != null) btn.Click += (s, e) => onClick();
        return btn;
    }

    private Panel CreateCurrentConditionsPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(40, 40, 55),
            Padding = new Padding(10),
            Margin = new Padding(0, 5, 5, 5)
        };
        // Top main info (Temp + Desc)
        _tempLabel = new Label
        {
            Text = "--°", // Shortened for sleeko
            Font = new Font("Segoe UI", 48, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(5, 5)
        };

        _weatherDescLabel = new Label
        {
            Text = "Loading...",
            Font = new Font("Segoe UI", 12),
            ForeColor = Color.FromArgb(200, 200, 220),
            AutoSize = true,
            Location = new Point(12, 100) // Moved down more for safety
        };

        // Details list (1 column, 4 rows as requested)
        var detailsPanel = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 4,
            Dock = DockStyle.Bottom,
            Height = 110, // Increased height for 4 rows
            AutoSize = false,
            BackColor = Color.Transparent,
            Padding = new Padding(5, 0, 0, 5)
        };
        detailsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        detailsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        detailsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        detailsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        detailsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));

        // Initialize labels
        _feelsLikeLabel = CreateDetailLabel("Feels like: --°");
        _windLabel = CreateDetailLabel("💨 Wind: --");
        _humidityLabel = CreateDetailLabel("💧 Humidity: --%");
        _uvLabel = CreateDetailLabel("☀️ UV: --");

        // Stack vertically: UV, Humidity, Wind, Feels Like
        detailsPanel.Controls.Add(_uvLabel, 0, 0);
        detailsPanel.Controls.Add(_humidityLabel, 0, 1);
        detailsPanel.Controls.Add(_windLabel, 0, 2);
        detailsPanel.Controls.Add(_feelsLikeLabel, 0, 3);

        panel.Controls.Add(_tempLabel);
        panel.Controls.Add(_weatherDescLabel);
        panel.Controls.Add(detailsPanel);
        
        EnableDragging(panel);

        return panel;
    }

    private Label CreateDetailLabel(string text)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.Gainsboro,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private Panel CreateRadarPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(40, 40, 55),
            Padding = new Padding(0),
            Margin = new Padding(5, 5, 0, 5)
        };

        _radarImage = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.StretchImage,
            BackColor = Color.FromArgb(25, 35, 50),
            Cursor = Cursors.Hand
        };

        // Mouse wheel for zoom
        _radarImage.MouseWheel += (s, e) =>
        {
            double zoomFactor = e.Delta > 0 ? 0.8 : 1.25; // Zoom in/out
            _radarZoom = Math.Clamp(_radarZoom * zoomFactor, 0.5, 10.0);
            _ = LoadRadarImageAsync();
        };

        // Mouse drag for pan
        _radarImage.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left && _radarImage.Image != null)
            {
                _radarDragging = true;
                _radarDragStart = e.Location;
                _radarDragStartLat = _radarCenterLat;
                _radarDragStartLon = _radarCenterLon;
                _radarImage.Cursor = Cursors.SizeAll;
                
                // Store original image for pan preview
                _radarOriginalImage?.Dispose();
                _radarOriginalImage = (Image)_radarImage.Image.Clone();
            }
        };

        _radarImage.MouseMove += (s, e) =>
        {
            if (_radarDragging && _radarOriginalImage != null && _radarImage.Width > 0 && _radarImage.Height > 0)
            {
                // Calculate offset
                int deltaX = e.X - _radarDragStart.X;
                int deltaY = e.Y - _radarDragStart.Y;
                
                // Update coordinates for final reload
                double degreesPerPixelX = (_radarZoom * 2.6) / _radarImage.Width;
                double degreesPerPixelY = (_radarZoom * 2.0) / _radarImage.Height;
                _radarCenterLon = _radarDragStartLon - (deltaX * degreesPerPixelX);
                _radarCenterLat = _radarDragStartLat + (deltaY * degreesPerPixelY);
                
                // Create visual preview by offsetting the original image
                var preview = new Bitmap(_radarImage.Width, _radarImage.Height);
                using (var g = Graphics.FromImage(preview))
                {
                    g.Clear(Color.FromArgb(35, 55, 80));
                    g.DrawImage(_radarOriginalImage, deltaX, deltaY);
                }
                
                var oldImage = _radarImage.Image;
                _radarImage.Image = preview;
                if (oldImage != _radarOriginalImage)
                    oldImage?.Dispose();
            }
        };

        _radarImage.MouseUp += (s, e) =>
        {
            if (_radarDragging)
            {
                _radarDragging = false;
                _radarImage.Cursor = Cursors.Hand;
                _radarOriginalImage?.Dispose();
                _radarOriginalImage = null;
                _ = LoadRadarImageAsync();
            }
        };

        // Reset on double-click
        _radarImage.DoubleClick += (s, e) =>
        {
            _radarZoom = 2.5;
            _radarCenterLat = _settings.Latitude;
            _radarCenterLon = _settings.Longitude;
            _ = LoadRadarImageAsync();
        };

        panel.Controls.Add(_radarImage);
        
        // Add zoom/pan hint as a small semi-transparent overlay
        var hintLabel = new Label {
            Text = "Scroll to Zoom • Drag to Pan",
            AutoSize = true,
            BackColor = Color.FromArgb(100, 0, 0, 0),
            ForeColor = Color.FromArgb(200, 255, 255, 255),
            Font = new Font("Segoe UI", 7),
            Location = new Point(5, 5)
        };
        _radarImage.Controls.Add(hintLabel);

        return panel;
    }

    private Panel CreateHourlyPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(40, 40, 55),
            Padding = new Padding(10, 5, 10, 5),
            Margin = new Padding(0, 5, 5, 0)
        };

        var titleLabel = new Label
        {
            Text = "Hourly",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 22
        };

        // Use TableLayoutPanel for vertical list (matching daily format)
        _hourlyContainer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            BackColor = Color.Transparent,
            Padding = new Padding(0)
        };
        _hourlyContainer.RowStyles.Clear();
        _hourlyContainer.ColumnStyles.Clear();
        _hourlyContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // Single column
        for (int i = 0; i < 8; i++)
        {
            _hourlyContainer.RowStyles.Add(new RowStyle(SizeType.Percent, 12.5f)); // 8 rows equal height
        }

        panel.Controls.Clear();
        panel.Controls.Add(_hourlyContainer); // Fill
        panel.Controls.Add(titleLabel);       // Top
        
        EnableDragging(panel);

        return panel;
    }

    private Panel CreateDailyPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(40, 40, 55),
            Padding = new Padding(10, 5, 10, 5),
            Margin = new Padding(5, 5, 0, 0)
        };

        var titleLabel = new Label
        {
            Text = "7-Day",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 22
        };

        _dailyContainer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
            Padding = new Padding(0)
        };

        panel.Controls.Add(_dailyContainer);
        panel.Controls.Add(titleLabel);
        
        EnableDragging(panel);

        return panel;
    }

    private async Task SearchAndUpdateLocationAsync()
    {
        var query = _locationTextBox.Text.Trim();
        if (string.IsNullOrEmpty(query)) return;

        // _statusLabel.Text = "Searching..."; // Removed

        try
        {
            var result = await _weatherService.SearchLocationAsync(query);
            if (result == null && query.Contains(','))
                result = await _weatherService.SearchLocationAsync(query.Split(',')[0].Trim());

            if (result != null)
            {
                _settings.Latitude = result.Latitude;
                _settings.Longitude = result.Longitude;
                _settings.LocationName = result.DisplayName;
                _settings.Save();

                _locationLabel.Text = $"📍 {_settings.LocationName}";
                _locationTextBox.Clear();
                _onSettingsChanged?.Invoke(_settings);
                await LoadWeatherDataAsync();
                // _statusLabel.Text = $"Location: {_settings.LocationName}";
            }
            else
            {
                MessageBox.Show($"Location not found: '{query}'", "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                // _statusLabel.Text = "Not found";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            // _statusLabel.Text = "Error";
        }
    }

    public async Task LoadWeatherDataAsync()
    {
        // _statusLabel.Text = "Loading..."; // Removed status

        try
        {
            _currentData = await _weatherService.FetchExtendedWeatherAsync(_settings.Latitude, _settings.Longitude);
            
            // Initialize radar center to settings location if not set
            if (_radarCenterLat == 0 && _radarCenterLon == 0)
            {
                _radarCenterLat = _settings.Latitude;
                _radarCenterLon = _settings.Longitude;
            }
            
            UpdateCurrentConditions();
            UpdateHourlyForecast();
            UpdateDailyForecast();
            await LoadRadarImageAsync();
            // _statusLabel.Text = $"Updated: {DateTime.Now:t}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
        }
    }

    private async Task LoadRadarImageAsync()
    {
        try
        {
            // Get actual panel dimensions for proper filling
            int imgWidth = Math.Max(300, _radarImage.Width);
            int imgHeight = Math.Max(200, _radarImage.Height);
            if (imgWidth <= 0) imgWidth = 400;
            if (imgHeight <= 0) imgHeight = 300;
            
            // Calculate bounding box using zoom/pan state
            double radius = _radarZoom;
            double centerLat = _radarCenterLat != 0 ? _radarCenterLat : _settings.Latitude;
            double centerLon = _radarCenterLon != 0 ? _radarCenterLon : _settings.Longitude;
            
            double minLon = centerLon - radius * 1.3; // wider for aspect ratio
            double maxLon = centerLon + radius * 1.3;
            double minLat = centerLat - radius;
            double maxLat = centerLat + radius;

            // Clamp to CONUS bounds for radar
            double radarMinLon = Math.Max(-130, minLon);
            double radarMaxLon = Math.Min(-60, maxLon);
            double radarMinLat = Math.Max(20, minLat);
            double radarMaxLat = Math.Min(55, maxLat);

            // Create composite image
            using var composite = new Bitmap(imgWidth, imgHeight);
            using var g = Graphics.FromImage(composite);
            
            // Simple 3-color background: ocean blue
            g.Clear(Color.FromArgb(35, 55, 80));

            // Fetch USGS state boundaries with styled background
            try
            {
                // Use Natural Earth styled WMS from NOAA for simple land/ocean
                string baseMapUrl = $"https://basemap.nationalmap.gov/arcgis/services/USGSTopo/MapServer/WMSServer?" +
                    $"service=WMS&version=1.1.1&request=GetMap" +
                    $"&layers=0" +
                    $"&styles=" +
                    $"&srs=EPSG:4326" +
                    $"&bbox={minLon},{minLat},{maxLon},{maxLat}" +
                    $"&width={imgWidth}&height={imgHeight}" +
                    $"&format=image/png";

                var mapBytes = await _httpClient.GetByteArrayAsync(baseMapUrl);
                using var mapMs = new MemoryStream(mapBytes);
                using var mapImg = Image.FromStream(mapMs);
                
                // Apply a dark tint to the base map
                using var tintedBmp = new Bitmap(imgWidth, imgHeight);
                using var tintG = Graphics.FromImage(tintedBmp);
                tintG.DrawImage(mapImg, 0, 0, imgWidth, imgHeight);
                
                // Darken for better radar visibility
                using var darkBrush = new SolidBrush(Color.FromArgb(140, 20, 30, 50));
                tintG.FillRectangle(darkBrush, 0, 0, imgWidth, imgHeight);
                
                g.DrawImage(tintedBmp, 0, 0, imgWidth, imgHeight);
            }
            catch
            {
                // Fallback: simple land color with grid
                g.Clear(Color.FromArgb(45, 55, 65));
                using var gridPen = new Pen(Color.FromArgb(40, 100, 120, 140), 1);
                int gridSpacingX = imgWidth / 8;
                int gridSpacingY = imgHeight / 6;
                for (int i = 1; i < 8; i++)
                    g.DrawLine(gridPen, i * gridSpacingX, 0, i * gridSpacingX, imgHeight);
                for (int i = 1; i < 6; i++)
                    g.DrawLine(gridPen, 0, i * gridSpacingY, imgWidth, i * gridSpacingY);
            }

            // Fetch NOAA radar overlay
            try
            {
                string radarUrl = $"https://opengeo.ncep.noaa.gov/geoserver/conus/conus_bref_qcd/ows?" +
                    $"service=WMS&version=1.3.0&request=GetMap" +
                    $"&layers=conus_bref_qcd" +
                    $"&styles=" +
                    $"&crs=EPSG:4326" +
                    $"&bbox={radarMinLat},{radarMinLon},{radarMaxLat},{radarMaxLon}" +
                    $"&width={imgWidth}&height={imgHeight}" +
                    $"&format=image/png" +
                    $"&transparent=true";

                var radarBytes = await _httpClient.GetByteArrayAsync(radarUrl);
                using var radarMs = new MemoryStream(radarBytes);
                using var radarImg = Image.FromStream(radarMs);
                g.DrawImage(radarImg, 0, 0, imgWidth, imgHeight);
            }
            catch
            {
                // Radar unavailable outside CONUS
                using var font = new Font("Segoe UI", 9);
                using var brush = new SolidBrush(Color.FromArgb(150, 180, 200));
                g.DrawString("Radar: US only", font, brush, 10, imgHeight - 25);
            }

            // Draw a center marker for the location
            using var markerPen = new Pen(Color.FromArgb(255, 80, 80), 2);
            int cx = imgWidth / 2;
            int cy = imgHeight / 2;
            g.DrawLine(markerPen, cx - 12, cy, cx + 12, cy);
            g.DrawLine(markerPen, cx, cy - 12, cx, cy + 12);
            g.DrawEllipse(markerPen, cx - 6, cy - 6, 12, 12);

            // Draw scale indicator
            using var infoFont = new Font("Segoe UI", 8);
            using var infoBrush = new SolidBrush(Color.FromArgb(180, 200, 220));
            g.DrawString($"~{radius * 111:F0} km", infoFont, infoBrush, 5, 5);

            // Dispose old image and set new
            _radarImage.Image?.Dispose();
            _radarImage.Image = (Image)composite.Clone();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Radar error: {ex.Message}");
        }
    }

    private void UpdateCurrentConditions()
    {
        if (_currentData == null) return;
        var c = _currentData.Current;
        _tempLabel.Text = $"{c.Temperature:F0}°F";
        _weatherDescLabel.Text = $"{c.WeatherIcon} {c.WeatherDescription}";
        _feelsLikeLabel.Text = $"Feels like: {c.FeelsLike:F0}°F";
        _windLabel.Text = $"💨 Wind: {c.WindSpeed:F0} mph";
        _humidityLabel.Text = $"💧 Humidity: {c.Humidity}%";
        _uvLabel.Text = $"☀️ UV Index: {c.UVIndex:F1}";
    }

    private void UpdateHourlyForecast()
    {
        if (_currentData == null) return;
        _hourlyContainer.Controls.Clear();
        
        var hours = _currentData.Hourly.Take(8).ToList();
        
        for (int i = 0; i < hours.Count; i++)
        {
            var hour = hours[i];
            
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 1)
            };
            
            // Match Daily format columns: Time (instead of Date), Icon, Temp, Rain
            // Adjusted widths to prevent rain wrapping: 25, 15, 40, 20
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25)); // Time
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15)); // Icon
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40)); // Temp
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20)); // Rain

            // Time
            var timeLabel = new Label { 
                Text = hour.Time.ToString("h tt"), 
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.White,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(5, 0, 0, 0)
            };

            // Icon
            var iconLabel = new Label {
                Text = hour.WeatherIcon,
                Font = new Font("Segoe UI Emoji", 10),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };

            // Temp
            var tempLabel = new Label {
                Text = $"{hour.Temperature:F0}°",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.White,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };

            // Rain
            var rainLabel = new Label {
                Text = hour.PrecipitationProbability > 0 ? $"{hour.PrecipitationProbability}%" : "",
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.LightBlue,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(0, 0, 8, 0)
            };

            row.Controls.Add(timeLabel, 0, 0);
            row.Controls.Add(iconLabel, 1, 0);
            row.Controls.Add(tempLabel, 2, 0);
            row.Controls.Add(rainLabel, 3, 0);

            EnableDragging(row);

            _hourlyContainer.Controls.Add(row, 0, i); // Add to row i
        }
    }



    private void UpdateDailyForecast()
    {
        if (_currentData == null) return;
        _dailyContainer.Controls.Clear();

        int containerHeight = _dailyContainer.ClientSize.Height;
        int rowHeight = (containerHeight / 7) - 1; // Fit 7 days exactly

        foreach (var day in _currentData.Daily)
        {
            var row = new TableLayoutPanel
            {
                Width = _dailyContainer.ClientSize.Width,
                Height = rowHeight,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 1)
            };
            
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25)); // Date (Reduced from 35)
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15)); // Icon
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40)); // Temps (Increased from 35)
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20)); // Rain (Increased from 15 to fix wrap)

            // Date
            var dateLabel = new Label { 
                Text = day.Date.ToString("ddd d"), 
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.White,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(5, 0, 0, 0)
            };

            // Icon
            var iconLabel = new Label {
                Text = day.WeatherIcon,
                Font = new Font("Segoe UI Emoji", 10),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };

            // Temps (High/Low)
            var tempLabel = new Label {
                Text = $"{day.TemperatureMax:F0}° / {day.TemperatureMin:F0}°",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.White,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };

            // Rain
            var rainLabel = new Label {
                Text = day.PrecipitationProbability > 0 ? $"{day.PrecipitationProbability}%" : "",
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.LightBlue,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(0, 0, 8, 0)
            };

            row.Controls.Add(dateLabel, 0, 0);
            row.Controls.Add(iconLabel, 1, 0);
            row.Controls.Add(tempLabel, 2, 0);
            row.Controls.Add(rainLabel, 3, 0);
            
            EnableDragging(row);

            _dailyContainer.Controls.Add(row);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _httpClient.Dispose();
            _radarImage?.Image?.Dispose();
        }
        base.Dispose(disposing);
    }
}
