using System;
using System.Runtime.InteropServices;
using Rhino;
using Rhino.Geometry;
using Rhino.PlugIns;

[assembly: Guid("16ff780a-a213-4fa1-9039-49eea6bd3aa8")]
[assembly: PlugInDescription(DescriptionType.Organization, "March Studio")]
[assembly: PlugInDescription(DescriptionType.Country, "Australia")]

namespace ResCodeShadows
{
  public class ResCodeShadowsPlugIn : PlugIn
  {
    public ResCodeShadowsPlugIn()
    {
      Instance = this;
      AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbedded;
    }

    /// <summary>Loads Clipper2Lib from this .rhp's resources if it isn't next to the plug-in.</summary>
    static System.Reflection.Assembly ResolveEmbedded(object sender, ResolveEventArgs args)
    {
      if (!args.Name.StartsWith("Clipper2Lib", StringComparison.OrdinalIgnoreCase)) return null;
      var me = typeof(ResCodeShadowsPlugIn).Assembly;
      using (var s = me.GetManifestResourceStream("Embedded.Clipper2Lib.dll"))
      {
        if (s == null) return null;
        var bytes = new byte[s.Length];
        s.Read(bytes, 0, bytes.Length);
        return System.Reflection.Assembly.Load(bytes);
      }
    }
    public static ResCodeShadowsPlugIn Instance { get; private set; }
    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;
  }

  /// <summary>Persistent settings. Defaults follow ResCode Standard A14 / B21 (Victoria).</summary>
  public class ShadowSettings
  {
    // Site
    public string City = "Melbourne, VIC";
    /// <summary>UI preference only: show the north angle clockwise from page-up (true) or counter-clockwise.</summary>
    public bool NorthClockwise = true;
    public double Latitude = -37.8136;     // Melbourne CBD
    public double Longitude = 144.9631;
    public double UtcOffset = 10.0;        // AEST. 22 September is before daylight saving starts.
    /// <summary>Angle of TRUE north, degrees counter-clockwise from world +Y (0 = model +Y is north).</summary>
    public double TrueNorth = 0.0;

    // Date / time (ResCode: 22 September, 9 am - 3 pm)
    public int Year = DateTime.Now.Year;
    public int Month = 9;
    public int Day = 22;
    public double StartHour = 9.0;
    public double EndHour = 15.0;
    /// <summary>Interval between diagrams, minutes (60 = hourly like Revit; 180 = 9/12/3).</summary>
    public int DiagramInterval = 60;

    // Sunlight analysis (Standard B21 / A14)
    public int AnalysisStep = 15;          // minutes
    public double GridSpacing = 0.25;      // metres
    public double RequiredHours = 5.0;
    public double RequiredPercent = 75.0;
    public double RequiredMaxArea = 40.0;  // m2

    // Geometry relevance culling
    public bool AutoCull = true;
    public bool CullEnclosed = true;
    public double MinCasterSize = 0.1;     // metres - largest dimension below this is ignored

    const string P = "ResCode.";

    public static ShadowSettings Load()
    {
      var s = new ShadowSettings();
      var ps = ResCodeShadowsPlugIn.Instance?.Settings;
      if (ps == null) return s;
      s.City = ps.GetString(P + "City", s.City);
      s.NorthClockwise = ps.GetBool(P + "NorthClockwise", s.NorthClockwise);
      s.Latitude = ps.GetDouble(P + "Latitude", s.Latitude);
      s.Longitude = ps.GetDouble(P + "Longitude", s.Longitude);
      s.UtcOffset = ps.GetDouble(P + "UtcOffset", s.UtcOffset);
      s.TrueNorth = ps.GetDouble(P + "TrueNorth", s.TrueNorth);
      s.Year = ps.GetInteger(P + "Year", s.Year);
      s.Month = ps.GetInteger(P + "Month", s.Month);
      s.Day = ps.GetInteger(P + "Day", s.Day);
      s.StartHour = ps.GetDouble(P + "StartHour", s.StartHour);
      s.EndHour = ps.GetDouble(P + "EndHour", s.EndHour);
      s.DiagramInterval = ps.GetInteger(P + "DiagramInterval", s.DiagramInterval);
      s.AnalysisStep = ps.GetInteger(P + "AnalysisStep", s.AnalysisStep);
      s.GridSpacing = ps.GetDouble(P + "GridSpacing", s.GridSpacing);
      s.RequiredHours = ps.GetDouble(P + "RequiredHours", s.RequiredHours);
      s.RequiredPercent = ps.GetDouble(P + "RequiredPercent", s.RequiredPercent);
      s.RequiredMaxArea = ps.GetDouble(P + "RequiredMaxArea", s.RequiredMaxArea);
      s.AutoCull = ps.GetBool(P + "AutoCull", s.AutoCull);
      s.CullEnclosed = ps.GetBool(P + "CullEnclosed", s.CullEnclosed);
      s.MinCasterSize = ps.GetDouble(P + "MinCasterSize", s.MinCasterSize);
      return s;
    }

    public void Save()
    {
      var ps = ResCodeShadowsPlugIn.Instance?.Settings;
      if (ps == null) return;
      ps.SetString(P + "City", City ?? "");
      ps.SetBool(P + "NorthClockwise", NorthClockwise);
      ps.SetDouble(P + "Latitude", Latitude);
      ps.SetDouble(P + "Longitude", Longitude);
      ps.SetDouble(P + "UtcOffset", UtcOffset);
      ps.SetDouble(P + "TrueNorth", TrueNorth);
      ps.SetInteger(P + "Year", Year);
      ps.SetInteger(P + "Month", Month);
      ps.SetInteger(P + "Day", Day);
      ps.SetDouble(P + "StartHour", StartHour);
      ps.SetDouble(P + "EndHour", EndHour);
      ps.SetInteger(P + "DiagramInterval", DiagramInterval);
      ps.SetInteger(P + "AnalysisStep", AnalysisStep);
      ps.SetDouble(P + "GridSpacing", GridSpacing);
      ps.SetDouble(P + "RequiredHours", RequiredHours);
      ps.SetDouble(P + "RequiredPercent", RequiredPercent);
      ps.SetDouble(P + "RequiredMaxArea", RequiredMaxArea);
      ps.SetBool(P + "AutoCull", AutoCull);
      ps.SetBool(P + "CullEnclosed", CullEnclosed);
      ps.SetDouble(P + "MinCasterSize", MinCasterSize);
    }

    public SunPosition SunAt(double localHours) =>
      Solar.Compute(Latitude, Longitude, UtcOffset, Year, Month, Day, localHours);

    /// <summary>Model-space unit vector pointing TOWARDS the sun.</summary>
    public Vector3d SunVector(SunPosition p)
    {
      double a = TrueNorth * Math.PI / 180.0;
      var north = new Vector3d(-Math.Sin(a), Math.Cos(a), 0);
      var east = new Vector3d(Math.Cos(a), Math.Sin(a), 0);
      double alt = p.AltitudeDeg * Math.PI / 180.0;
      double az = p.AzimuthDeg * Math.PI / 180.0;
      var v = east * (Math.Sin(az) * Math.Cos(alt)) + north * (Math.Cos(az) * Math.Cos(alt)) + Vector3d.ZAxis * Math.Sin(alt);
      v.Unitize();
      return v;
    }

    public Vector3d NorthVector()
    {
      double a = TrueNorth * Math.PI / 180.0;
      return new Vector3d(-Math.Sin(a), Math.Cos(a), 0);
    }

    public string DateLabel =>
      new DateTime(Year, Math.Max(1, Math.Min(12, Month)), Math.Max(1, Math.Min(DateTime.DaysInMonth(Year, Month), Day)))
        .ToString("d MMMM").ToUpperInvariant();

    public string ZoneLabel
    {
      get
      {
        var sign = UtcOffset < 0 ? "-" : "+";
        var abs = Math.Abs(UtcOffset);
        int h = (int)Math.Floor(abs);
        int m = (int)Math.Round((abs - h) * 60);
        return $"UTC{sign}{h:00}:{m:00}";
      }
    }

    public static string TimeLabel(double hours)
    {
      int total = (int)Math.Round(hours * 60);
      int h = total / 60, m = total % 60;
      string ampm = h >= 12 ? "PM" : "AM";
      int h12 = h % 12 == 0 ? 12 : h % 12;
      return $"{h12}:{m:00} {ampm}";
    }

    public static string TimeKey(double hours)
    {
      int total = (int)Math.Round(hours * 60);
      return $"{total / 60:00}{total % 60:00}";
    }

    /// <summary>True north as a 0-360 angle clockwise from page-up (+Y).</summary>
    public double NorthClockwiseDeg
    {
      get { var a = -TrueNorth % 360.0; return a < 0 ? a + 360.0 : a; }
      set { TrueNorth = -value; }
    }

    public string NorthText =>
      Math.Abs(TrueNorth % 360.0) < 1e-9 ? "true north straight up the page"
      : NorthClockwise ? $"true north {NorthClockwiseDeg:0.##}° clockwise from page-up"
                       : $"true north {(360.0 - NorthClockwiseDeg) % 360.0:0.##}° counter-clockwise from page-up";

    public void Print()
    {
      RhinoApp.WriteLine($"ResCode settings: {City} - lat {Latitude:0.0000}, lon {Longitude:0.0000}, {ZoneLabel}, {NorthText}, " +
                         $"{DateLabel} {Year}, {TimeLabel(StartHour)} - {TimeLabel(EndHour)}.");
    }
  }
}
