using System;

namespace ResCodeShadows
{
  /// <summary>Sun position in true-north terms.</summary>
  public struct SunPosition
  {
    /// <summary>Degrees above the horizon.</summary>
    public double AltitudeDeg;
    /// <summary>Degrees clockwise from true north.</summary>
    public double AzimuthDeg;
    public bool IsUp => AltitudeDeg > 0.0;
  }

  /// <summary>
  /// NOAA solar position algorithm (accurate to well under 0.1 degree for 1900-2100),
  /// with standard atmospheric refraction applied to the altitude.
  /// </summary>
  public static class Solar
  {
    static double Rad(double d) => d * Math.PI / 180.0;
    static double Deg(double r) => r * 180.0 / Math.PI;

    static double Mod(double a, double m)
    {
      var r = a % m;
      return r < 0 ? r + m : r;
    }

    /// <param name="latitudeDeg">South is negative (Melbourne = -37.81).</param>
    /// <param name="longitudeDeg">East is positive (Melbourne = 144.96).</param>
    /// <param name="utcOffsetHours">Clock offset from UTC (Victoria in September = +10, AEST).</param>
    /// <param name="localHours">Local clock time in decimal hours (9.5 = 9:30 am).</param>
    public static SunPosition Compute(double latitudeDeg, double longitudeDeg, double utcOffsetHours,
                                      int year, int month, int day, double localHours)
    {
      double jd = JulianDayAt0hUT(year, month, day) + (localHours - utcOffsetHours) / 24.0;
      double T = (jd - 2451545.0) / 36525.0;

      double L0 = Mod(280.46646 + T * (36000.76983 + T * 0.0003032), 360.0);
      double M = 357.52911 + T * (35999.05029 - 0.0001537 * T);
      double e = 0.016708634 - T * (0.000042037 + 0.0000001267 * T);
      double Mr = Rad(M);
      double C = Math.Sin(Mr) * (1.914602 - T * (0.004817 + 0.000014 * T))
               + Math.Sin(2 * Mr) * (0.019993 - 0.000101 * T)
               + Math.Sin(3 * Mr) * 0.000289;
      double trueLong = L0 + C;
      double omega = 125.04 - 1934.136 * T;
      double lambda = trueLong - 0.00569 - 0.00478 * Math.Sin(Rad(omega));
      double eps0 = 23.0 + (26.0 + (21.448 - T * (46.815 + T * (0.00059 - T * 0.001813))) / 60.0) / 60.0;
      double eps = eps0 + 0.00256 * Math.Cos(Rad(omega));
      double decl = Math.Asin(Math.Sin(Rad(eps)) * Math.Sin(Rad(lambda)));

      double y = Math.Tan(Rad(eps) / 2.0);
      y *= y;
      double L0r = Rad(L0);
      double eqTimeMin = 4.0 * Deg(y * Math.Sin(2 * L0r)
                                 - 2 * e * Math.Sin(Mr)
                                 + 4 * e * y * Math.Sin(Mr) * Math.Cos(2 * L0r)
                                 - 0.5 * y * y * Math.Sin(4 * L0r)
                                 - 1.25 * e * e * Math.Sin(2 * Mr));

      double trueSolarMin = Mod(localHours * 60.0 + eqTimeMin + 4.0 * longitudeDeg - 60.0 * utcOffsetHours, 1440.0);
      double hourAngle = trueSolarMin / 4.0 - 180.0;

      double lat = Rad(latitudeDeg);
      double ha = Rad(hourAngle);
      double cosZen = Math.Sin(lat) * Math.Sin(decl) + Math.Cos(lat) * Math.Cos(decl) * Math.Cos(ha);
      cosZen = Math.Max(-1.0, Math.Min(1.0, cosZen));
      double altitude = 90.0 - Deg(Math.Acos(cosZen));
      altitude += Refraction(altitude);

      double az = Deg(Math.Atan2(Math.Sin(ha), Math.Cos(ha) * Math.Sin(lat) - Math.Tan(decl) * Math.Cos(lat))) + 180.0;

      return new SunPosition { AltitudeDeg = altitude, AzimuthDeg = Mod(az, 360.0) };
    }

    static double Refraction(double elevDeg)
    {
      if (elevDeg > 85.0) return 0.0;
      double te = Math.Tan(Rad(elevDeg));
      double arcsec;
      if (elevDeg > 5.0) arcsec = 58.1 / te - 0.07 / (te * te * te) + 0.000086 / Math.Pow(te, 5);
      else if (elevDeg > -0.575) arcsec = 1735.0 + elevDeg * (-518.2 + elevDeg * (103.4 + elevDeg * (-12.79 + elevDeg * 0.711)));
      else arcsec = -20.772 / te;
      return arcsec / 3600.0;
    }

    static double JulianDayAt0hUT(int y, int m, int d)
    {
      if (m <= 2) { y -= 1; m += 12; }
      int A = y / 100;
      int B = 2 - A + A / 4;
      return Math.Floor(365.25 * (y + 4716)) + Math.Floor(30.6001 * (m + 1)) + d + B - 1524.5;
    }
  }
}
