using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using Rhino;

namespace ResCodeShadows
{
  public enum SettingsOutcome { Cancel, Ok, PickNorth }

  /// <summary>Location / north / date / ResCode settings dialog.</summary>
  public class SettingsDialog : Dialog<SettingsOutcome>
  {
    readonly ShadowSettings _s;
    readonly RhinoDoc _doc;
    bool _updating;

    readonly DropDown _city = new DropDown();
    readonly NumericStepper _lat = new NumericStepper { MinValue = -90, MaxValue = 90, DecimalPlaces = 5, Increment = 0.01, Width = 110 };
    readonly NumericStepper _lon = new NumericStepper { MinValue = -180, MaxValue = 180, DecimalPlaces = 5, Increment = 0.01, Width = 110 };
    readonly NumericStepper _utc = new NumericStepper { MinValue = -14, MaxValue = 14, DecimalPlaces = 1, Increment = 0.5, Width = 110 };

    readonly NumericStepper _north = new NumericStepper { MinValue = 0, MaxValue = 360, DecimalPlaces = 2, Increment = 1, Width = 110 };
    readonly DropDown _northDir = new DropDown { DataStore = new[] { "clockwise from page-up", "counter-clockwise from page-up" } };
    readonly Drawable _compass = new Drawable { Size = new Size(140, 140) };

    readonly DateTimePicker _date = new DateTimePicker { Mode = DateTimePickerMode.Date };
    readonly DropDown _start = new DropDown();
    readonly DropDown _end = new DropDown();
    readonly DropDown _interval = new DropDown();
    static readonly int[] Intervals = { 15, 30, 60, 180 };

    readonly NumericStepper _hours = new NumericStepper { MinValue = 0, MaxValue = 12, DecimalPlaces = 1, Increment = 0.5, Width = 80 };
    readonly NumericStepper _pct = new NumericStepper { MinValue = 0, MaxValue = 100, DecimalPlaces = 0, Increment = 5, Width = 80 };
    readonly NumericStepper _maxArea = new NumericStepper { MinValue = 0, MaxValue = 10000, DecimalPlaces = 1, Increment = 5, Width = 80 };
    readonly NumericStepper _grid = new NumericStepper { MinValue = 0.05, MaxValue = 5, DecimalPlaces = 2, Increment = 0.05, Width = 80 };
    readonly NumericStepper _step = new NumericStepper { MinValue = 1, MaxValue = 60, DecimalPlaces = 0, Increment = 5, Width = 80 };

    readonly CheckBox _autoCull = new CheckBox { Text = "Ignore geometry that can't shade the area being assessed" };
    readonly CheckBox _cullEnclosed = new CheckBox { Text = "Ignore objects sealed inside closed solids (furniture, internal walls)" };
    readonly NumericStepper _minSize = new NumericStepper { MinValue = 0, MaxValue = 5, DecimalPlaces = 2, Increment = 0.05, Width = 80 };

    readonly List<double> _times = new List<double>();

    public SettingsDialog(ShadowSettings settings, RhinoDoc doc)
    {
      _s = settings;
      _doc = doc;
      Title = "ResCode Shadow Settings";
      Resizable = false;
      Padding = new Padding(10);

      // ---- populate lists
      var cityNames = Cities.All.Select(c => c.Name).ToList();
      cityNames.Add(Cities.Custom);
      _city.DataStore = cityNames;

      for (double t = 6; t <= 18 + 1e-9; t += 0.25) _times.Add(t);
      var timeNames = _times.Select(ShadowSettings.TimeLabel).ToList();
      _start.DataStore = timeNames;
      _end.DataStore = timeNames.ToList();
      _interval.DataStore = new[] { "Every 15 minutes", "Every 30 minutes", "Hourly (9, 10, 11 ... 3)", "9 am, 12 pm, 3 pm only" };

      LoadFromSettings();

      // ---- events
      _city.SelectedIndexChanged += (o, e) => OnCityChanged();
      _lat.ValueChanged += (o, e) => OnCoordsEdited();
      _lon.ValueChanged += (o, e) => OnCoordsEdited();
      _north.ValueChanged += (o, e) => _compass.Invalidate();
      _northDir.SelectedIndexChanged += (o, e) => _compass.Invalidate();
      _compass.Paint += PaintCompass;

      var mapBtn = new Button { Text = "Find on map…" };
      mapBtn.Click += (o, e) => OpenMap();
      var anchorBtn = new Button { Text = "Use model's Earth Anchor", ToolTip = "Reads latitude, longitude and north from Rhino's EarthAnchorPoint" };
      anchorBtn.Click += (o, e) => FromEarthAnchor();

      var pickNorthBtn = new Button { Text = "Pick in model…", ToolTip = "Click two points: a base point and a point towards true north" };
      pickNorthBtn.Click += (o, e) => { SaveToSettings(); Close(SettingsOutcome.PickNorth); };
      var upBtn = new Button { Text = "Straight up" };
      upBtn.Click += (o, e) => _north.Value = 0;

      var resetBtn = new Button { Text = "ResCode defaults" };
      resetBtn.Click += (o, e) => ResetResCode();

      DefaultButton = new Button { Text = "OK" };
      DefaultButton.Click += (o, e) => { SaveToSettings(); Close(SettingsOutcome.Ok); };
      AbortButton = new Button { Text = "Cancel" };
      AbortButton.Click += (o, e) => Close(SettingsOutcome.Cancel);

      // ---- layout
      var location = new GroupBox
      {
        Text = "Site location",
        Padding = new Padding(8),
        Content = new TableLayout
        {
          Spacing = new Size(8, 6),
          Rows =
          {
            new TableRow(new Label { Text = "City", VerticalAlignment = VerticalAlignment.Center }, new TableCell(_city, true)),
            new TableRow(Lbl("Latitude (° , south −)"), Stack(_lat, mapBtn)),
            new TableRow(Lbl("Longitude (°)"), Stack(_lon, anchorBtn)),
            new TableRow(Lbl("UTC offset (h)"), Stack(_utc, new Label { Text = "standard time — no daylight saving on 22 Sep", TextColor = Colors.Gray, VerticalAlignment = VerticalAlignment.Center })),
          }
        }
      };

      var north = new GroupBox
      {
        Text = "True north",
        Padding = new Padding(8),
        Content = new TableLayout
        {
          Spacing = new Size(8, 6),
          Rows =
          {
            new TableRow(
              new TableLayout
              {
                Spacing = new Size(6, 6),
                Rows =
                {
                  new TableRow(Lbl("Angle (°)"), _north),
                  new TableRow(Lbl("Measured"), _northDir),
                  new TableRow(null, Stack(pickNorthBtn, upBtn)),
                  new TableRow(null, new Label { Text = "0° = north points straight up the page (+Y).", TextColor = Colors.Gray }),
                  null
                }
              },
              _compass)
          }
        }
      };

      var date = new GroupBox
      {
        Text = "Date and times",
        Padding = new Padding(8),
        Content = new TableLayout
        {
          Spacing = new Size(8, 6),
          Rows =
          {
            new TableRow(Lbl("Date"), _date),
            new TableRow(Lbl("From"), Stack(_start, Lbl("to"), _end)),
            new TableRow(Lbl("Diagrams"), _interval),
          }
        }
      };

      var rescode = new GroupBox
      {
        Text = "ResCode Standard B21 / A14 check",
        Padding = new Padding(8),
        Content = new TableLayout
        {
          Spacing = new Size(8, 6),
          Rows =
          {
            new TableRow(Lbl("Min. hours of sun"), _hours, Lbl("Grid spacing (m)"), _grid),
            new TableRow(Lbl("% of SPOS"), _pct, Lbl("Time step (min)"), _step),
            new TableRow(Lbl("or area (m²), lesser"), _maxArea, null, resetBtn),
          }
        }
      };

      _autoCull.CheckedChanged += (o, e) => { _cullEnclosed.Enabled = _minSize.Enabled = _autoCull.Checked == true; };
      _cullEnclosed.Enabled = _minSize.Enabled = _autoCull.Checked == true;
      var speed = new GroupBox
      {
        Text = "Large models",
        Padding = new Padding(8),
        Content = new TableLayout
        {
          Spacing = new Size(8, 6),
          Rows =
          {
            new TableRow(_autoCull),
            new TableRow(_cullEnclosed),
            new TableRow(Stack(Lbl("Ignore objects smaller than (m)"), _minSize)),
          }
        }
      };

      Content = new TableLayout
      {
        Spacing = new Size(8, 8),
        Rows =
        {
          location, north, date, rescode, speed,
          new TableRow(new TableLayout { Spacing = new Size(6, 0), Rows = { new TableRow(null, DefaultButton, AbortButton) } }),
        }
      };
    }

    static Label Lbl(string t) => new Label { Text = t, VerticalAlignment = VerticalAlignment.Center };

    static StackLayout Stack(params Control[] controls)
    {
      var s = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 6, VerticalContentAlignment = VerticalAlignment.Center };
      foreach (var c in controls) s.Items.Add(c);
      return s;
    }

    // ------------------------------------------------------------------ data

    void LoadFromSettings()
    {
      _updating = true;
      var cityIdx = Cities.All.FindIndex(c => c.Name == _s.City);
      _city.SelectedIndex = cityIdx >= 0 ? cityIdx : Cities.All.Count;
      _lat.Value = _s.Latitude;
      _lon.Value = _s.Longitude;
      _utc.Value = _s.UtcOffset;

      _northDir.SelectedIndex = _s.NorthClockwise ? 0 : 1;
      double cw = _s.NorthClockwiseDeg;
      _north.Value = Math.Round(_s.NorthClockwise ? cw : (360 - cw) % 360, 4);

      int y = Math.Max(1900, Math.Min(2100, _s.Year));
      int m = Math.Max(1, Math.Min(12, _s.Month));
      int d = Math.Max(1, Math.Min(DateTime.DaysInMonth(y, m), _s.Day));
      _date.Value = new DateTime(y, m, d);
      _start.SelectedIndex = NearestTime(_s.StartHour);
      _end.SelectedIndex = NearestTime(_s.EndHour);
      int iv = Array.IndexOf(Intervals, _s.DiagramInterval);
      _interval.SelectedIndex = iv >= 0 ? iv : 2;

      _hours.Value = _s.RequiredHours;
      _pct.Value = _s.RequiredPercent;
      _maxArea.Value = _s.RequiredMaxArea;
      _grid.Value = _s.GridSpacing;
      _step.Value = _s.AnalysisStep;
      _autoCull.Checked = _s.AutoCull;
      _cullEnclosed.Checked = _s.CullEnclosed;
      _minSize.Value = _s.MinCasterSize;
      _updating = false;
    }

    int NearestTime(double h)
    {
      int best = 0;
      for (int i = 1; i < _times.Count; i++)
        if (Math.Abs(_times[i] - h) < Math.Abs(_times[best] - h)) best = i;
      return best;
    }

    void SaveToSettings()
    {
      _s.City = _city.SelectedIndex >= 0 && _city.SelectedIndex < Cities.All.Count ? Cities.All[_city.SelectedIndex].Name : Cities.Custom;
      _s.Latitude = _lat.Value;
      _s.Longitude = _lon.Value;
      _s.UtcOffset = _utc.Value;

      _s.NorthClockwise = _northDir.SelectedIndex != 1;
      _s.NorthClockwiseDeg = _s.NorthClockwise ? _north.Value : -_north.Value;

      var dt = _date.Value ?? new DateTime(DateTime.Now.Year, 9, 22);
      _s.Year = dt.Year; _s.Month = dt.Month; _s.Day = dt.Day;
      double a = _times[Math.Max(0, _start.SelectedIndex)], b = _times[Math.Max(0, _end.SelectedIndex)];
      _s.StartHour = Math.Min(a, b);
      _s.EndHour = Math.Max(a, b);
      _s.DiagramInterval = Intervals[Math.Max(0, _interval.SelectedIndex)];

      _s.RequiredHours = _hours.Value;
      _s.RequiredPercent = _pct.Value;
      _s.RequiredMaxArea = _maxArea.Value;
      _s.GridSpacing = _grid.Value;
      _s.AnalysisStep = (int)Math.Round(_step.Value);
      _s.AutoCull = _autoCull.Checked == true;
      _s.CullEnclosed = _cullEnclosed.Checked == true;
      _s.MinCasterSize = _minSize.Value;
    }

    void OnCityChanged()
    {
      if (_updating) return;
      int i = _city.SelectedIndex;
      if (i < 0 || i >= Cities.All.Count) return;
      var c = Cities.All[i];
      _updating = true;
      _lat.Value = c.Latitude;
      _lon.Value = c.Longitude;
      _utc.Value = c.UtcOffset;
      _updating = false;
    }

    void OnCoordsEdited()
    {
      if (_updating) return;
      _updating = true;
      _city.SelectedIndex = Cities.All.Count; // Custom
      _updating = false;
    }

    void SetCustomLocation(double lat, double lon)
    {
      _updating = true;
      _lat.Value = Math.Round(lat, 6);
      _lon.Value = Math.Round(lon, 6);
      _utc.Value = GuessUtcOffset(lat, lon);
      _city.SelectedIndex = Cities.All.Count;
      _updating = false;
    }

    /// <summary>Australian/NZ standard time zone from position (approximate; check border towns such as Broken Hill).</summary>
    static double GuessUtcOffset(double lat, double lon)
    {
      if (lon >= 165) return 12;
      if (lon < 129) return 8;
      if (lon < 138) return 9.5;                 // SA / NT
      if (lon < 141 && lat < -26) return 9.5;    // SA east of 138
      return 10;
    }

    void OpenMap()
    {
      var dlg = new MapDialog(_lat.Value, _lon.Value);
      if (dlg.ShowModal(this))
        SetCustomLocation(dlg.Latitude, dlg.Longitude);
    }

    void FromEarthAnchor()
    {
      var ea = _doc?.EarthAnchorPoint;
      if (ea == null || !ea.EarthLocationIsSet())
      {
        MessageBox.Show(this, "This model has no Earth Anchor Point set.\nUse the EarthAnchorPoint command, or pick a city / the map instead.", "ResCode Shadows");
        return;
      }
      SetCustomLocation(ea.EarthBasepointLatitude, ea.EarthBasepointLongitude);
      var n = ea.ModelNorth;
      if (n.IsValid && (Math.Abs(n.X) > 1e-9 || Math.Abs(n.Y) > 1e-9))
      {
        double cw = (Math.Atan2(n.X, n.Y) * 180 / Math.PI + 360) % 360; // clockwise from +Y
        _north.Value = Math.Round(_northDir.SelectedIndex == 1 ? (360 - cw) % 360 : cw, 3);
      }
    }

    void ResetResCode()
    {
      var d = new ShadowSettings();
      _date.Value = new DateTime(_date.Value?.Year ?? DateTime.Now.Year, 9, 22);
      _start.SelectedIndex = NearestTime(d.StartHour);
      _end.SelectedIndex = NearestTime(d.EndHour);
      _interval.SelectedIndex = 2;
      _hours.Value = d.RequiredHours;
      _pct.Value = d.RequiredPercent;
      _maxArea.Value = d.RequiredMaxArea;
      _grid.Value = d.GridSpacing;
      _step.Value = d.AnalysisStep;
    }

    // ------------------------------------------------------------------ compass preview

    void PaintCompass(object sender, PaintEventArgs e)
    {
      var g = e.Graphics;
      g.AntiAlias = true;
      var size = _compass.Size;
      float cx = size.Width / 2f, cy = size.Height / 2f, r = Math.Min(cx, cy) - 14;
      var grey = Color.FromArgb(150, 150, 150);
      var font = SystemFonts.Default(8);
      var bold = new Font(SystemFont.Bold, 10);

      g.DrawEllipse(grey, cx - r, cy - r, 2 * r, 2 * r);
      // page-up reference tick
      g.DrawLine(grey, cx, cy - r - 6, cx, cy - r + 6);
      g.DrawText(font, grey, cx + 4, cy - r - 13, "page up");

      double cw = _northDir.SelectedIndex == 1 ? -_north.Value : _north.Value;
      double a = cw * Math.PI / 180.0;
      float dx = (float)Math.Sin(a), dy = (float)-Math.Cos(a);   // screen y is down
      var tip = new PointF(cx + dx * (r - 4), cy + dy * (r - 4));
      var tail = new PointF(cx - dx * (r - 18), cy - dy * (r - 18));
      var side = new PointF(-dy, dx);
      var red = Color.FromArgb(200, 50, 40);
      g.FillPolygon(red, tip, new PointF(cx + side.X * 7, cy + side.Y * 7), new PointF(cx - side.X * 7, cy - side.Y * 7));
      g.FillPolygon(grey, tail, new PointF(cx + side.X * 7, cy + side.Y * 7), new PointF(cx - side.X * 7, cy - side.Y * 7));
      var nPos = new PointF(cx + dx * (r + 7) - 4, cy + dy * (r + 7) - 7);
      g.DrawText(bold, red, nPos.X, nPos.Y, "N");
    }
  }

  // ======================================================================== map picker

  /// <summary>Leaflet map in a WebView. Click (or drag the pin, or search an address) to set the site.</summary>
  public class MapDialog : Dialog<bool>
  {
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    readonly Label _status = new Label { VerticalAlignment = VerticalAlignment.Center };

    public MapDialog(double lat, double lon)
    {
      Latitude = lat; Longitude = lon;
      Title = "Locate site - click the map, drag the pin, or search an address";
      Resizable = true;
      ClientSize = new Size(900, 650);
      Padding = new Padding(8);

      var web = new WebView();
      web.DocumentTitleChanged += (o, e) => OnTitle(e.Title);
      web.LoadHtml(Html(lat, lon), new Uri("https://rescode-shadows.local/"));

      DefaultButton = new Button { Text = "Use this location" };
      DefaultButton.Click += (o, e) => Close(true);
      AbortButton = new Button { Text = "Cancel" };
      AbortButton.Click += (o, e) => Close(false);
      UpdateStatus();

      Content = new TableLayout
      {
        Spacing = new Size(6, 6),
        Rows =
        {
          new TableRow(web) { ScaleHeight = true },
          new TableRow(new TableLayout { Spacing = new Size(6, 0), Rows = { new TableRow(new TableCell(_status, true), DefaultButton, AbortButton) } }),
        }
      };
    }

    void OnTitle(string title)
    {
      if (string.IsNullOrEmpty(title) || !title.StartsWith("pick:")) return;
      var parts = title.Substring(5).Split(',');
      if (parts.Length != 2) return;
      if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var la) &&
          double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lo))
      {
        Latitude = la; Longitude = lo;
        Application.Instance.AsyncInvoke(UpdateStatus);
      }
    }

    void UpdateStatus() => _status.Text = $"Latitude {Latitude:0.000000}   Longitude {Longitude:0.000000}";

    static string Html(double lat, double lon)
    {
      string la = lat.ToString("0.######", CultureInfo.InvariantCulture);
      string lo = lon.ToString("0.######", CultureInfo.InvariantCulture);
      // ES5 only: Rhino 7 on Windows hosts this in an IE11-class engine.
      return @"<!DOCTYPE html>
<html><head>
<meta http-equiv='X-UA-Compatible' content='IE=edge'>
<meta charset='utf-8'>
<title>map</title>
<link rel='stylesheet' href='https://unpkg.com/leaflet@1.9.4/dist/leaflet.css'>
<script src='https://unpkg.com/leaflet@1.9.4/dist/leaflet.js'></script>
<style>
 html,body{margin:0;height:100%;font-family:Segoe UI,Arial,sans-serif;font-size:13px}
 #map{position:absolute;top:40px;bottom:0;left:0;right:0}
 #bar{position:absolute;top:0;left:0;right:0;height:40px;display:flex;align-items:center;padding:0 8px;background:#f3f3f3;border-bottom:1px solid #ccc}
 #q{flex:1;padding:5px 8px;font-size:13px;border:1px solid #bbb;border-radius:3px}
 #go{margin-left:6px;padding:5px 12px}
 #msg{margin-left:10px;color:#666}
</style></head>
<body>
<div id='bar'><input id='q' placeholder='Search address, e.g. 12 Smith St Fitzroy VIC'><button id='go'>Search</button><span id='msg'></span></div>
<div id='map'></div>
<script>
var start=[" + la + "," + lo + @"];
var map=L.map('map').setView(start,18);
var sat=L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}',{maxZoom:20,maxNativeZoom:19,attribution:'Imagery &copy; Esri'});
var street=L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}',{maxZoom:20,maxNativeZoom:19,attribution:'&copy; Esri'});
var osm=L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png',{maxZoom:20,maxNativeZoom:19,attribution:'&copy; OpenStreetMap contributors'});
sat.addTo(map);
L.control.layers({'Aerial':sat,'Streets':street,'OpenStreetMap':osm}).addTo(map);
var pin=L.marker(start,{draggable:true}).addTo(map);
function report(ll){document.title='pick:'+ll.lat.toFixed(6)+','+ll.lng.toFixed(6);document.getElementById('msg').innerHTML=ll.lat.toFixed(5)+', '+ll.lng.toFixed(5);}
map.on('click',function(e){pin.setLatLng(e.latlng);report(e.latlng);});
pin.on('dragend',function(){report(pin.getLatLng());});
function search(){
 var q=document.getElementById('q').value; if(!q) return;
 var x=new XMLHttpRequest();
 x.open('GET','https://nominatim.openstreetmap.org/search?format=json&limit=1&countrycodes=au,nz&q='+encodeURIComponent(q),true);
 x.onreadystatechange=function(){ if(x.readyState!==4) return;
  try{ var r=JSON.parse(x.responseText); if(r.length){ var ll=L.latLng(parseFloat(r[0].lat),parseFloat(r[0].lon)); map.setView(ll,19); pin.setLatLng(ll); report(ll);} else {document.getElementById('msg').innerHTML='Not found';} }
  catch(err){document.getElementById('msg').innerHTML='Search unavailable';} };
 x.send();
}
document.getElementById('go').onclick=search;
document.getElementById('q').onkeydown=function(e){ if((e||window.event).keyCode===13) search(); };
</script></body></html>";
    }
  }
}
