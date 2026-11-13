using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

namespace ResCodeShadows
{
  // ======================================================================== settings

  [System.Runtime.InteropServices.Guid("ef635859-38af-4945-927d-dafaf481cd3a")]
  public class ResCodeShadowSettingsCommand : Command
  {
    public override string EnglishName => "ResCodeShadowSettings";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      // "ResCodeShadowSettings" opens the dialog; "-ResCodeShadowSettings" uses command-line options.
      if (mode == RunMode.Interactive) return RunDialog(doc);
      return RunCommandLine(doc);
    }

    public static Result ShowSettings(RhinoDoc doc) => RunDialog(doc);

    static Result RunDialog(RhinoDoc doc)
    {
      var s = ShadowSettings.Load();
      while (true)
      {
        var dlg = new SettingsDialog(s, doc);
        var outcome = dlg.ShowModal(Rhino.UI.RhinoEtoApp.MainWindow);
        if (outcome == SettingsOutcome.Cancel) return Result.Cancel;
        if (outcome == SettingsOutcome.Ok) break;
        // PickNorth: dialog closed so the user can click in the viewports, then reopen it
        if (PickNorth(out double ccw)) s.TrueNorth = ccw;
      }
      s.Save();
      s.Print();
      return Result.Success;
    }

    /// <summary>Two clicks: base point, then a point towards true north. Returns angle CCW from +Y.</summary>
    static bool PickNorth(out double ccwFromY)
    {
      ccwFromY = 0;
      var gp = new GetPoint();
      gp.SetCommandPrompt("Base point of true-north arrow");
      if (gp.Get() != GetResult.Point) return false;
      var a = gp.Point();
      gp.SetCommandPrompt("Point in the direction of true north");
      gp.DrawLineFromPoint(a, true);
      if (gp.Get() != GetResult.Point) return false;
      var b = gp.Point();
      var d = new Vector3d(b.X - a.X, b.Y - a.Y, 0);
      if (d.IsTiny()) return false;
      ccwFromY = Math.Round(RhinoMath.ToDegrees(Math.Atan2(-d.X, d.Y)), 3);
      return true;
    }

    static Result RunCommandLine(RhinoDoc doc)
    {
      var s = ShadowSettings.Load();
      var go = new GetOption();
      go.AcceptNothing(true);

      while (true)
      {
        go.SetCommandPrompt($"ResCode shadow settings ({s.DateLabel} {s.Year}, {ShadowSettings.TimeLabel(s.StartHour)}-{ShadowSettings.TimeLabel(s.EndHour)}). Press Enter when done");
        go.ClearCommandOptions();
        var lat = new OptionDouble(s.Latitude, -90, 90);
        var lon = new OptionDouble(s.Longitude, -180, 180);
        var utc = new OptionDouble(s.UtcOffset, -14, 14);
        var north = new OptionDouble(s.TrueNorth, -360, 360);
        var year = new OptionInteger(s.Year, 1900, 2100);
        var month = new OptionInteger(s.Month, 1, 12);
        var day = new OptionInteger(s.Day, 1, 31);
        var start = new OptionDouble(s.StartHour, 0, 24);
        var end = new OptionDouble(s.EndHour, 0, 24);
        var interval = new OptionInteger(s.DiagramInterval, 5, 720);
        var step = new OptionInteger(s.AnalysisStep, 1, 60);
        var grid = new OptionDouble(s.GridSpacing, 0.05, 5);
        var hrs = new OptionDouble(s.RequiredHours, 0, 24);
        var pct = new OptionDouble(s.RequiredPercent, 0, 100);
        var maxA = new OptionDouble(s.RequiredMaxArea, 0, 10000);

        go.AddOptionDouble("Latitude", ref lat);
        go.AddOptionDouble("Longitude", ref lon);
        go.AddOptionDouble("UTCOffset", ref utc);
        go.AddOptionDouble("TrueNorth", ref north);
        int optPick = go.AddOption("PickTrueNorth");
        go.AddOptionInteger("Year", ref year);
        go.AddOptionInteger("Month", ref month);
        go.AddOptionInteger("Day", ref day);
        go.AddOptionDouble("StartHour", ref start);
        go.AddOptionDouble("EndHour", ref end);
        go.AddOptionInteger("DiagramIntervalMin", ref interval);
        go.AddOptionInteger("AnalysisStepMin", ref step);
        go.AddOptionDouble("GridSpacingM", ref grid);
        go.AddOptionDouble("RequiredHours", ref hrs);
        go.AddOptionDouble("RequiredPercent", ref pct);
        go.AddOptionDouble("RequiredMaxAreaM2", ref maxA);
        var cull = new OptionToggle(s.AutoCull, "Off", "On");
        var encl = new OptionToggle(s.CullEnclosed, "Off", "On");
        var minSz = new OptionDouble(s.MinCasterSize, 0, 5);
        go.AddOptionToggle("IgnoreIrrelevant", ref cull);
        go.AddOptionToggle("IgnoreEnclosed", ref encl);
        go.AddOptionDouble("IgnoreSmallerThanM", ref minSz);
        int optReset = go.AddOption("ResetToResCode");

        var res = go.Get();
        if (res == GetResult.Cancel) return Result.Cancel;
        if (res == GetResult.Nothing) break;
        if (res != GetResult.Option) continue;

        s.Latitude = lat.CurrentValue; s.Longitude = lon.CurrentValue; s.UtcOffset = utc.CurrentValue;
        s.TrueNorth = north.CurrentValue; s.Year = year.CurrentValue; s.Month = month.CurrentValue;
        s.Day = Math.Min(day.CurrentValue, DateTime.DaysInMonth(s.Year, s.Month));
        s.StartHour = start.CurrentValue; s.EndHour = end.CurrentValue;
        s.DiagramInterval = interval.CurrentValue; s.AnalysisStep = step.CurrentValue;
        s.GridSpacing = grid.CurrentValue; s.RequiredHours = hrs.CurrentValue;
        s.RequiredPercent = pct.CurrentValue; s.RequiredMaxArea = maxA.CurrentValue;
        s.AutoCull = cull.CurrentValue; s.CullEnclosed = encl.CurrentValue; s.MinCasterSize = minSz.CurrentValue;

        if (go.OptionIndex() == optPick)
        {
          if (PickNorth(out double ccw)) s.TrueNorth = ccw;
        }
        else if (go.OptionIndex() == optReset)
        {
          var fresh = new ShadowSettings { City = s.City, Latitude = s.Latitude, Longitude = s.Longitude, UtcOffset = s.UtcOffset, TrueNorth = s.TrueNorth,
                                           AutoCull = s.AutoCull, CullEnclosed = s.CullEnclosed, MinCasterSize = s.MinCasterSize };
          s = fresh;
        }
      }

      if (s.EndHour < s.StartHour) { var t = s.EndHour; s.EndHour = s.StartHour; s.StartHour = t; }
      s.Save();
      s.Print();
      return Result.Success;
    }
  }

  // ======================================================================== shadow diagrams

  [System.Runtime.InteropServices.Guid("f914dc00-1003-4803-ba09-fd45c051ee52")]
  public class ResCodeShadowsCommand : Command
  {
    public override string EnglishName => "ResCodeShadows";

    public const string RootLayer = "ResCode Shadow Diagrams";
    static readonly Color ExistingColor = Color.FromArgb(175, 175, 175);
    static readonly Color AdditionalColor = Color.FromArgb(205, 72, 60);
    static readonly Color OutlineColor = Color.FromArgb(60, 60, 60);
    static readonly Color LabelColor = Color.Black;

    class Frame
    {
      public double Hour;
      public SunPosition Sun;
      public Vector3d ToSun;
      public Shadow2D Existing, Proposed, Additional;
    }

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      var s = ShadowSettings.Load();
      s.Print();
      RhinoApp.WriteLine("Run ResCodeShadowSettings to change location, north, date or times.");

      var sc = Scenario.Get(doc);
      if (sc == null) return Result.Cancel;
      s = ShadowSettings.Load(); // may have changed via the Settings option

      double tol = doc.ModelAbsoluteTolerance;
      var gn = new GetNumber();
      gn.SetCommandPrompt("Ground elevation that receives shadow");
      gn.SetDefaultNumber(sc.Bounds.Min.Z);
      gn.AcceptNothing(true);
      var gr = gn.Get();
      if (gr == GetResult.Cancel) return Result.Cancel;
      double groundZ = gr == GetResult.Number ? gn.Number() : sc.Bounds.Min.Z;
      var existingCasters = sc.Context.Concat(sc.Removed).ToList();
      var proposedCasters = sc.Context.Concat(sc.Proposed).ToList();

      // ---- compute every frame first so the labels can sit clear of all shadows
      var frames = new List<Frame>();
      var bounds = sc.Bounds;
      double stepH = Math.Max(1, s.DiagramInterval) / 60.0;
      for (double t = s.StartHour; t <= s.EndHour + 1e-6; t += stepH)
      {
        var f = new Frame { Hour = t, Sun = s.SunAt(t) };
        if (!f.Sun.IsUp)
        {
          RhinoApp.WriteLine($"  {ShadowSettings.TimeLabel(t)}: sun is below the horizon - skipped.");
          continue;
        }
        f.ToSun = s.SunVector(f.Sun);
        f.Existing = ShadowEngine.CastShadow(existingCasters, f.ToSun, groundZ, tol);
        f.Proposed = ShadowEngine.CastShadow(proposedCasters, f.ToSun, groundZ, tol);
        f.Additional = f.Proposed.Minus(f.Existing);
        if (!f.Existing.IsEmpty) bounds.Union(f.Existing.Bounds);
        if (!f.Proposed.IsEmpty) bounds.Union(f.Proposed.Bounds);
        frames.Add(f);
      }
      if (frames.Count == 0) { RhinoApp.WriteLine("No daylight frames in the chosen time range."); return Result.Nothing; }

      ClearLayerTree(doc, RootLayer);
      int solid = SolidHatchIndex(doc);
      double m2model = RhinoMath.UnitScale(UnitSystem.Meters, doc.ModelUnitSystem);
      double model2m = 1.0 / m2model;
      // text height: ~1/80 of the drawing, clamped to 200-600 mm real size
      double h = Math.Min(Math.Max(bounds.Diagonal.Length / 80.0, 0.2 * m2model), 0.6 * m2model);
      var labelOrigin = new Point3d(bounds.Min.X, bounds.Min.Y - 2.5 * h, groundZ);
      var arrowCentre = new Point3d(bounds.Max.X + 5 * h, bounds.Max.Y - 4 * h, groundZ);
      var northCentre = new Point3d(bounds.Max.X + 5 * h, bounds.Max.Y - 13 * h, groundZ);

      int rootIdx = Layers.Ensure(doc, RootLayer, null, LabelColor, true);
      AddNorthArrow(doc, s, northCentre, h, rootIdx);

      var report = new StringBuilder();
      report.AppendLine($"ResCode shadow diagrams - {s.DateLabel} {s.Year} ({s.ZoneLabel})");
      report.AppendLine("  Time       Alt    Azim   Additional shadow (m2)");

      bool first = true;
      foreach (var f in frames)
      {
        string key = ShadowSettings.TimeKey(f.Hour);
        string timeName = $"{key} {s.DateLabel}";
        int tIdx = Layers.Ensure(doc, timeName, RootLayer, LabelColor, first);
        string tPath = RootLayer + "::" + timeName;
        int exIdx = Layers.Ensure(doc, "Existing shadow", tPath, ExistingColor, true);
        int adIdx = Layers.Ensure(doc, "Additional shadow (proposal)", tPath, AdditionalColor, true);
        int olIdx = Layers.Ensure(doc, "Proposed shadow outline", tPath, OutlineColor, true);
        int lbIdx = Layers.Ensure(doc, "Labels", tPath, LabelColor, true);
        // one diagram visible at a time, like separate Revit views
        var tLayer = doc.Layers[tIdx];
        if (tLayer.IsVisible != first) { tLayer.IsVisible = first; tLayer.CommitChanges(); }
        first = false;

        int failed = 0;
        foreach (var g in f.Existing.HatchGroups()) failed += AddHatches(doc, g, solid, exIdx, tol);
        foreach (var g in f.Additional.HatchGroups()) failed += AddHatches(doc, g, solid, adIdx, tol);
        if (failed > 0)
          RhinoApp.WriteLine($"  {ShadowSettings.TimeLabel(f.Hour)}: {failed} region(s) could not be hatched - their outlines are still drawn.");
        double addArea = f.Additional.Area;

        var olAttr = Attr(doc, olIdx);
        foreach (var c in f.Proposed.Loops()) doc.Objects.AddCurve(c, olAttr);

        // labels
        var lbAttr = Attr(doc, lbIdx);
        string title = $"SHADOW DIAGRAM - {ShadowSettings.TimeLabel(f.Hour)} {s.DateLabel} ({s.ZoneLabel})";
        string sub = $"Sun altitude {f.Sun.AltitudeDeg:0.0}°, azimuth {f.Sun.AzimuthDeg:0.0}° from true north";
        AddText(doc, title, labelOrigin, h, lbAttr);
        AddText(doc, sub, labelOrigin - Vector3d.YAxis * 1.6 * h, h * 0.6, lbAttr);

        // legend swatches
        var lg = labelOrigin - Vector3d.YAxis * 3.4 * h;
        AddSwatch(doc, lg, h, solid, exIdx, tol);
        AddText(doc, "Existing shadow", lg + new Vector3d(1.6 * h, 0, 0), h * 0.6, lbAttr);
        var lg2 = lg - Vector3d.YAxis * 1.4 * h;
        AddSwatch(doc, lg2, h, solid, adIdx, tol);
        AddText(doc, "Additional shadow cast by proposal", lg2 + new Vector3d(1.6 * h, 0, 0), h * 0.6, lbAttr);

        // sun arrow: points the way the light travels
        var flat = new Vector3d(-f.ToSun.X, -f.ToSun.Y, 0);
        if (flat.Unitize())
        {
          var arrowAttr = Attr(doc, lbIdx);
          arrowAttr.ObjectDecoration = ObjectDecoration.EndArrowhead;
          var a0 = arrowCentre - flat * 3 * h;
          doc.Objects.AddLine(new Line(a0, arrowCentre + flat * 3 * h), arrowAttr);
          doc.Objects.AddCircle(new Circle(new Plane(a0, Vector3d.ZAxis), 0.5 * h), lbAttr);
          AddText(doc, "SUN", a0 + new Vector3d(0.8 * h, 0.8 * h, 0), h * 0.5, lbAttr);
        }

        report.AppendLine($"  {ShadowSettings.TimeLabel(f.Hour),-9}  {f.Sun.AltitudeDeg,5:0.0}  {f.Sun.AzimuthDeg,5:0.0}  {addArea * model2m * model2m,8:0.0}");
      }

      doc.Views.Redraw();
      RhinoApp.WriteLine(report.ToString());
      RhinoApp.WriteLine($"Diagrams are on layer \"{RootLayer}\" - one sub-layer per time (only the first is switched on).");
      return Result.Success;
    }

    static ObjectAttributes Attr(RhinoDoc doc, int layer)
    {
      var a = doc.CreateDefaultAttributes();
      a.LayerIndex = layer;
      return a;
    }

    /// <summary>Hatches one island (outer loop + holes). Returns 1 if it could not be hatched.</summary>
    internal static int AddHatches(RhinoDoc doc, Curve[] curves, int pattern, int layer, double tol)
    {
      if (curves == null || curves.Length == 0) return 0;
      var a = Attr(doc, layer);
      foreach (var t in new[] { tol, tol * 10, tol * 100 })
      {
        var hatches = Hatch.Create(curves, pattern, 0, 1, t);
        if (hatches != null && hatches.Length > 0)
        {
          foreach (var ht in hatches) doc.Objects.AddHatch(ht, a);
          return 0;
        }
      }
      // last resort: hatch from a planar face built on the loops
      var breps = Brep.CreatePlanarBreps(curves, tol * 10);
      if (breps != null)
      {
        bool any = false;
        foreach (var b in breps)
          foreach (var face in b.Faces)
          {
            var loops = face.Loops.Select(l => l.To3dCurve()).Where(c => c != null).ToArray();
            var hs = Hatch.Create(loops, pattern, 0, 1, tol * 10);
            if (hs == null) continue;
            foreach (var ht in hs) { doc.Objects.AddHatch(ht, a); any = true; }
          }
        if (any) return 0;
      }
      return 1;
    }

    static void AddSwatch(RhinoDoc doc, Point3d p, double h, int pattern, int layer, double tol)
    {
      var rect = new Rectangle3d(new Plane(p, Vector3d.ZAxis), h, h * 0.8).ToNurbsCurve();
      AddHatches(doc, new Curve[] { rect }, pattern, layer, tol);
    }

    internal static void AddText(RhinoDoc doc, string text, Point3d p, double height, ObjectAttributes attr)
    {
      var te = TextEntity.Create(text, new Plane(p, Vector3d.ZAxis), doc.DimStyles.Current, false, 0, 0);
      if (te == null) return;
      // Labels are sized in real model units: ignore the dim style's model-space scale
      // (mm templates often carry 1:100 etc., which would blow the text up 100x).
      te.DimensionScale = 1.0;
      te.TextHeight = height;
      // Safety net for documents with model-space annotation scaling switched on:
      // measure the result and shrink/grow it back to the intended size.
      var bb = te.GetBoundingBox(true);
      double actual = bb.IsValid ? bb.Max.Y - bb.Min.Y : 0;
      if (actual > 0 && (actual > height * 3 || actual < height / 3))
        te.Transform(Transform.Scale(p, height * 1.2 / actual));
      doc.Objects.AddText(te, attr);
    }

    static void AddNorthArrow(RhinoDoc doc, ShadowSettings s, Point3d c, double h, int layer)
    {
      var n = s.NorthVector();
      var a = Attr(doc, layer);
      a.ObjectDecoration = ObjectDecoration.EndArrowhead;
      doc.Objects.AddLine(new Line(c - n * 2.5 * h, c + n * 2.5 * h), a);
      AddText(doc, "N (true)", c + n * 3.2 * h - new Vector3d(h, 0, 0), h * 0.7, Attr(doc, layer));
    }

    static double RegionArea(Curve[] region, double tol)
    {
      var breps = Brep.CreatePlanarBreps(region, tol);
      if (breps == null) return 0;
      double a = 0;
      foreach (var b in breps) { var amp = AreaMassProperties.Compute(b); if (amp != null) a += amp.Area; }
      return a;
    }

    internal static int SolidHatchIndex(RhinoDoc doc)
    {
      foreach (var hp in doc.HatchPatterns)
        if (hp != null && !hp.IsDeleted && hp.FillType == HatchPatternFillType.Solid) return hp.Index;
      var solid = new HatchPattern { Name = "Solid", FillType = HatchPatternFillType.Solid };
      return doc.HatchPatterns.Add(solid);
    }

    internal static void ClearLayerTree(RhinoDoc doc, string root)
    {
      var ids = new List<Guid>();
      foreach (var layer in doc.Layers)
      {
        if (layer == null || layer.IsDeleted) continue;
        if (layer.FullPath != root && !layer.FullPath.StartsWith(root + "::")) continue;
        var objs = doc.Objects.FindByLayer(layer);
        if (objs != null) ids.AddRange(objs.Select(o => o.Id));
      }
      foreach (var id in ids) doc.Objects.Delete(id, true);
      if (ids.Count > 0) RhinoApp.WriteLine($"Replaced {ids.Count} objects from the previous run.");
    }
  }

  // ======================================================================== ResCode B21 / A14 sunlight check

  [System.Runtime.InteropServices.Guid("8a2f13ce-ef45-42a5-a48b-d8d633173422")]
  public class ResCodeSunlightCommand : Command
  {
    public override string EnglishName => "ResCodeSunlight";
    public const string LayerName = "ResCode Sunlight to SPOS";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      var s = ShadowSettings.Load();
      s.Print();

      var go = new GetObject();
      go.SetCommandPrompt("Select closed curves outlining neighbouring secluded private open space (SPOS)");
      go.GeometryFilter = ObjectType.Curve;
      go.GeometryAttributeFilter = GeometryAttributeFilter.ClosedCurve;
      go.SubObjectSelect = false;
      go.GetMultiple(1, 0);
      if (go.CommandResult() != Result.Success) return go.CommandResult();
      var spos = go.Objects().Select(o => o.Object()).Where(o => o != null).ToList();
      doc.Objects.UnselectAll();

      var study = BoundingBox.Empty;
      foreach (var o in spos) study.Union(o.Geometry.GetBoundingBox(true));
      var sc = Scenario.Get(doc, preselect: false, studyArea: study);
      if (sc == null) return Result.Cancel;
      s = ShadowSettings.Load(); // may have changed via the Settings option

      double tol = doc.ModelAbsoluteTolerance;
      double m2model = RhinoMath.UnitScale(UnitSystem.Meters, doc.ModelUnitSystem);
      double model2m = 1.0 / m2model;
      double lift = 0.01 * m2model;

      // half-step sample times between Start and End
      int n = Math.Max(1, (int)Math.Round((s.EndHour - s.StartHour) * 60.0 / s.AnalysisStep));
      double dt = (s.EndHour - s.StartHour) / n;
      var suns = new List<Vector3d?>();
      for (int i = 0; i < n; i++)
      {
        var p = s.SunAt(s.StartHour + (i + 0.5) * dt);
        suns.Add(p.IsUp ? s.SunVector(p) : (Vector3d?)null);
      }
      var snapshotHours = new List<double>();
      for (double t = s.StartHour; t <= s.EndHour + 1e-6; t += Math.Max(1, s.DiagramInterval) / 60.0) snapshotHours.Add(t);
      var snapshotSuns = snapshotHours.Select(t => { var p = s.SunAt(t); return p.IsUp ? s.SunVector(p) : (Vector3d?)null; }).ToList();

      int layer = Layers.Ensure(doc, LayerName, null, Color.FromArgb(80, 160, 80), true);
      bool allComply = true;
      int idx = 0;

      foreach (var obj in spos)
      {
        idx++;
        var crv = (obj.Geometry as Curve)?.DuplicateCurve();
        string name = string.IsNullOrWhiteSpace(obj.Attributes.Name) ? $"SPOS {idx}" : obj.Attributes.Name;
        if (crv == null || !crv.IsClosed || !crv.TryGetPlane(out Plane cp, tol))
        { RhinoApp.WriteLine($"{name}: not a closed planar curve - skipped."); continue; }
        if (Math.Abs(cp.ZAxis.Z) < 0.999)
          RhinoApp.WriteLine($"{name}: curve is not horizontal - it is analysed in plan at its average height.");

        var bb = crv.GetBoundingBox(true);
        double z = (bb.Min.Z + bb.Max.Z) / 2;
        var hp = new Plane(new Point3d(0, 0, z), Vector3d.ZAxis);
        var flat = Curve.ProjectToPlane(crv, hp);
        var amp = AreaMassProperties.Compute(flat);
        if (amp == null) { RhinoApp.WriteLine($"{name}: could not compute area - skipped."); continue; }
        double areaM2 = amp.Area * model2m * model2m;

        double sp = s.GridSpacing * m2model;
        while ((bb.Max.X - bb.Min.X) * (bb.Max.Y - bb.Min.Y) / (sp * sp) > 250000) sp *= 1.5;
        var pts = new List<Point3d>();
        for (double x = bb.Min.X + sp / 2; x < bb.Max.X; x += sp)
          for (double y = bb.Min.Y + sp / 2; y < bb.Max.Y; y += sp)
          {
            var p = new Point3d(x, y, z);
            if (flat.Contains(p, hp, tol) == PointContainment.Inside) pts.Add(p);
          }
        if (pts.Count == 0) { RhinoApp.WriteLine($"{name}: area too small for grid spacing - skipped."); continue; }
        double cellM2 = areaM2 / pts.Count;

        var hEx = new double[pts.Count];
        var hPr = new double[pts.Count];
        var snapEx = new int[snapshotHours.Count];
        var snapPr = new int[snapshotHours.Count];
        // only casters whose shadow can reach this SPOS
        var suns0 = suns.Where(v => v != null).Select(v => v.Value).Concat(snapshotSuns.Where(v => v != null).Select(v => v.Value)).ToList();
        var area = bb; area.Inflate(sp, sp, 0);
        bool Reaches(Caster c)
        {
          var r = Culling.ShadowReach(c.Box, suns0, z);
          return r.Min.X <= area.Max.X && r.Max.X >= area.Min.X && r.Min.Y <= area.Max.Y && r.Max.Y >= area.Min.Y;
        }
        var ctx = sc.Context.Where(Reaches).ToList();
        var rem = sc.Removed.Where(Reaches).ToList();
        var pro = sc.Proposed.Where(Reaches).ToList();

        Rhino.UI.StatusBar.ShowProgressMeter(0, pts.Count, $"Sunlight {name}", true, true);
        for (int i = 0; i < pts.Count; i++)
        {
          if (i % 25 == 0) Rhino.UI.StatusBar.UpdateProgressMeter(i, true);
          for (int k = 0; k < suns.Count; k++)
          {
            if (suns[k] == null) continue;
            Test(pts[i], suns[k].Value, ctx, rem, pro, lift, out bool ex, out bool pr);
            if (!ex) hEx[i] += dt;
            if (!pr) hPr[i] += dt;
          }
          for (int k = 0; k < snapshotSuns.Count; k++)
          {
            if (snapshotSuns[k] == null) continue;
            Test(pts[i], snapshotSuns[k].Value, ctx, rem, pro, lift, out bool ex, out bool pr);
            if (!ex) snapEx[k]++;
            if (!pr) snapPr[k]++;
          }
        }
        Rhino.UI.StatusBar.HideProgressMeter();

        double req = s.RequiredHours - 1e-9;
        double sunEx = hEx.Count(v => v >= req) * cellM2;
        double sunPr = hPr.Count(v => v >= req) * cellM2;
        double required = Math.Min(areaM2 * s.RequiredPercent / 100.0, s.RequiredMaxArea);
        bool existingMeets = sunEx >= required;
        bool complies = existingMeets ? sunPr >= required : sunPr >= sunEx - cellM2;
        allComply &= complies;

        // coloured result mesh
        var mesh = new Mesh();
        double half = sp / 2;
        for (int i = 0; i < pts.Count; i++)
        {
          Color c = hPr[i] >= req ? Color.FromArgb(90, 185, 90)          // meets hours with proposal
                  : hEx[i] >= req ? Color.FromArgb(220, 60, 50)          // lost by proposal
                                  : Color.FromArgb(240, 170, 60);        // already below hours
          var p = pts[i] + Vector3d.ZAxis * lift * 0.5;
          int v0 = mesh.Vertices.Count;
          mesh.Vertices.Add(p.X - half, p.Y - half, p.Z);
          mesh.Vertices.Add(p.X + half, p.Y - half, p.Z);
          mesh.Vertices.Add(p.X + half, p.Y + half, p.Z);
          mesh.Vertices.Add(p.X - half, p.Y + half, p.Z);
          for (int q = 0; q < 4; q++) mesh.VertexColors.Add(c);
          mesh.Faces.AddFace(v0, v0 + 1, v0 + 2, v0 + 3);
        }
        mesh.Normals.ComputeNormals();
        var attr = doc.CreateDefaultAttributes();
        attr.LayerIndex = layer;
        attr.Name = $"{name} sunlight";
        doc.Objects.AddMesh(mesh, attr);

        string verdict = complies ? "COMPLIES" : "DOES NOT COMPLY";
        var dot = new TextDot($"{name}: {verdict}\n{sunPr:0.0} m² ≥{s.RequiredHours:0.#}h (req. {required:0.0} m²)", amp.Centroid);
        doc.Objects.AddTextDot(dot, attr);

        var sb = new StringBuilder();
        sb.AppendLine($"--- {name} ---  SPOS area {areaM2:0.0} m²  ({pts.Count} sample points, {sp * model2m:0.00} m grid)");
        sb.AppendLine($"  Required: lesser of {s.RequiredPercent:0}% ({areaM2 * s.RequiredPercent / 100:0.0} m²) or {s.RequiredMaxArea:0} m² = {required:0.0} m² with ≥{s.RequiredHours:0.#} h sun, {ShadowSettings.TimeLabel(s.StartHour)}-{ShadowSettings.TimeLabel(s.EndHour)} {s.DateLabel}");
        sb.AppendLine($"  Existing: {sunEx,6:0.0} m² ({100 * sunEx / areaM2:0}%)  {(existingMeets ? "meets standard" : "already below standard - must not be further reduced")}");
        sb.AppendLine($"  Proposed: {sunPr,6:0.0} m² ({100 * sunPr / areaM2:0}%)  -> {verdict}");
        sb.AppendLine("  Sunlit area at each time (m²):   existing   proposed   change");
        for (int k = 0; k < snapshotHours.Count; k++)
        {
          double e = snapEx[k] * cellM2, p = snapPr[k] * cellM2;
          sb.AppendLine($"    {ShadowSettings.TimeLabel(snapshotHours[k]),-9}                     {e,8:0.0}   {p,8:0.0}   {p - e,6:+0.0;-0.0;0.0}");
        }
        sb.Append("  Note: the 3 m minimum-dimension test for the 40 m² area is not checked automatically - verify the green zone.");
        RhinoApp.WriteLine(sb.ToString());
      }

      doc.Views.Redraw();
      RhinoApp.WriteLine(allComply ? "All selected SPOS comply with the ResCode overshadowing standard." : "One or more SPOS do NOT comply - see report above.");
      RhinoApp.WriteLine("Legend: green = ≥ required hours with proposal, red = lost by proposal, orange = already below required hours.");
      return Result.Success;
    }

    static void Test(Point3d p, Vector3d sun, List<Caster> ctx, List<Caster> rem, List<Caster> pro, double lift, out bool exShaded, out bool prShaded)
    {
      bool c = ShadowEngine.IsShaded(p, sun, ctx, lift);
      exShaded = c || ShadowEngine.IsShaded(p, sun, rem, lift);
      prShaded = c || ShadowEngine.IsShaded(p, sun, pro, lift);
    }
  }

  // ======================================================================== shared helpers

  /// <summary>The three object sets that make up "existing" and "proposed" conditions.</summary>
  internal class Scenario
  {
    public List<Caster> Proposed, Context, Removed;
    public BoundingBox Bounds = BoundingBox.Empty;

    const ObjectType CasterTypes = ObjectType.Brep | ObjectType.Extrusion | ObjectType.Mesh | ObjectType.SubD | ObjectType.InstanceReference;

    /// <param name="studyArea">Plan region the result is needed for (SPOS). Null = around the proposal's shadow.</param>
    public static Scenario Get(RhinoDoc doc, bool preselect = true, BoundingBox? studyArea = null)
    {
      var proposed = Pick(doc, "Select PROPOSED buildings (new built form, including fences/walls to be built)", false, preselect, null);
      if (proposed == null) return null;
      var removed = Pick(doc, "Select EXISTING site buildings to be DEMOLISHED (Enter for none)", true, false, null);
      if (removed == null) return null;
      var exclude = new HashSet<Guid>(proposed.Concat(removed).Select(o => o.Id));
      var context = Pick(doc, "Select retained CONTEXT - neighbours, fences, trees (AllVisible = whole model, irrelevant geometry is ignored automatically; Enter for none)", true, false, exclude);
      if (context == null) return null;

      var s = ShadowSettings.Load();
      if (s.AutoCull)
      {
        var rep = Culling.Run(doc, s, ref proposed, ref removed, ref context, studyArea);
        RhinoApp.WriteLine("Shadow relevance: " + rep);
      }

      var all = proposed.Concat(removed).Concat(context).ToList();
      int total = all.Count, done = 0;
      bool meter = total > 50;
      if (meter) Rhino.UI.StatusBar.ShowProgressMeter(0, total, "Meshing shadow casters", true, true);
      Action<int> tick = i => { if (meter && (++done % 10 == 0)) Rhino.UI.StatusBar.UpdateProgressMeter(done, true); };
      var sc = new Scenario
      {
        Proposed = ShadowEngine.BuildCasters(proposed, tick),
        Removed = ShadowEngine.BuildCasters(removed, tick),
        Context = ShadowEngine.BuildCasters(context, tick),
      };
      if (meter) Rhino.UI.StatusBar.HideProgressMeter();

      foreach (var c in sc.Proposed.Concat(sc.Removed).Concat(sc.Context)) sc.Bounds.Union(c.Box);
      if (sc.Proposed.Count == 0 && !studyArea.HasValue)
      {
        RhinoApp.WriteLine("Proposed objects could not be meshed.");
        return null;
      }
      if (!sc.Bounds.IsValid) { RhinoApp.WriteLine("Nothing to analyse."); return null; }
      RhinoApp.WriteLine($"Shadow casters: proposed {sc.Proposed.Count}, demolished {sc.Removed.Count}, context {sc.Context.Count}.");
      return sc;
    }

    static List<RhinoObject> Pick(RhinoDoc doc, string prompt, bool optional, bool preselect, HashSet<Guid> allVisibleExclude)
    {
      var go = new GetObject();
      go.SetCommandPrompt(prompt);
      go.GeometryFilter = CasterTypes;
      go.SubObjectSelect = false;
      go.GroupSelect = true;
      go.EnablePreSelect(preselect, true);
      go.DeselectAllBeforePostSelect = true;
      go.AcceptNothing(optional);
      int optSettings = optional ? -1 : go.AddOption("Settings");
      int optAll = allVisibleExclude == null ? -1 : go.AddOption("AllVisible");
      GetResult res;
      while (true)
      {
        res = go.GetMultiple(optional ? 0 : 1, 0);
        if (res == GetResult.Option && go.OptionIndex() == optSettings)
        {
          ResCodeShadowSettingsCommand.ShowSettings(doc);
          go.EnablePreSelect(false, true);
          continue;
        }
        if (res == GetResult.Option && go.OptionIndex() == optAll)
        {
          doc.Objects.UnselectAll();
          var all = AllVisible(doc, allVisibleExclude);
          RhinoApp.WriteLine($"{all.Count} visible objects collected as context.");
          return all;
        }
        break;
      }
      var list = new List<RhinoObject>();
      if (res == GetResult.Nothing) return list;
      if (res != GetResult.Object) return null;
      list.AddRange(go.Objects().Select(o => o.Object()).Where(o => o != null));
      doc.Objects.UnselectAll();
      return list;
    }

    static List<RhinoObject> AllVisible(RhinoDoc doc, HashSet<Guid> exclude)
    {
      var es = new ObjectEnumeratorSettings
      {
        ObjectTypeFilter = CasterTypes,
        VisibleFilter = true,
        NormalObjects = true,
        LockedObjects = true,
        HiddenObjects = false,
        ReferenceObjects = true,
        IncludeLights = false,
        IncludeGrips = false,
      };
      var outputRoots = new[] { ResCodeShadowsCommand.RootLayer, ResCodeSunlightCommand.LayerName };
      var list = new List<RhinoObject>();
      foreach (var o in doc.Objects.GetObjectList(es))
      {
        if (o == null || exclude.Contains(o.Id) || !o.Visible) continue;
        var layer = doc.Layers[o.Attributes.LayerIndex];
        if (layer != null && outputRoots.Any(r => layer.FullPath == r || layer.FullPath.StartsWith(r + "::"))) continue;
        list.Add(o);
      }
      return list;
    }
  }

  internal static class Layers
  {
    /// <summary>Find or create a layer (one level under <paramref name="parentPath"/>); returns its index.</summary>
    public static int Ensure(RhinoDoc doc, string name, string parentPath, Color color, bool visibleIfNew)
    {
      string full = string.IsNullOrEmpty(parentPath) ? name : parentPath + "::" + name;
      int idx = doc.Layers.FindByFullPath(full, -1);
      if (idx >= 0) return idx;
      var layer = new Layer { Name = name, Color = color, IsVisible = visibleIfNew };
      if (!string.IsNullOrEmpty(parentPath))
      {
        int pIdx = doc.Layers.FindByFullPath(parentPath, -1);
        if (pIdx >= 0) layer.ParentLayerId = doc.Layers[pIdx].Id;
      }
      return doc.Layers.Add(layer);
    }
  }
}
