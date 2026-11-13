using System.Collections.Generic;
using System.Linq;

namespace ResCodeShadows
{
  public class City
  {
    public string Name;
    public double Latitude;
    public double Longitude;
    /// <summary>Standard (non-daylight-saving) UTC offset. 22 September is before DST starts in every state.</summary>
    public double UtcOffset;
    public override string ToString() => Name;
  }

  public static class Cities
  {
    public const string Custom = "Custom location";

    public static readonly List<City> All = new List<City>
    {
      // Victoria first - ResCode is a Victorian planning provision
      new City { Name = "Melbourne, VIC",    Latitude = -37.8136, Longitude = 144.9631, UtcOffset = 10 },
      new City { Name = "Geelong, VIC",      Latitude = -38.1499, Longitude = 144.3617, UtcOffset = 10 },
      new City { Name = "Ballarat, VIC",     Latitude = -37.5622, Longitude = 143.8503, UtcOffset = 10 },
      new City { Name = "Bendigo, VIC",      Latitude = -36.7570, Longitude = 144.2794, UtcOffset = 10 },
      new City { Name = "Shepparton, VIC",   Latitude = -36.3833, Longitude = 145.4000, UtcOffset = 10 },
      new City { Name = "Wodonga, VIC",      Latitude = -36.1218, Longitude = 146.8881, UtcOffset = 10 },
      new City { Name = "Traralgon, VIC",    Latitude = -38.1950, Longitude = 146.5400, UtcOffset = 10 },
      new City { Name = "Warrnambool, VIC",  Latitude = -38.3818, Longitude = 142.4880, UtcOffset = 10 },
      new City { Name = "Mildura, VIC",      Latitude = -34.2080, Longitude = 142.1246, UtcOffset = 10 },
      new City { Name = "Horsham, VIC",      Latitude = -36.7125, Longitude = 142.1997, UtcOffset = 10 },
      new City { Name = "Mornington, VIC",   Latitude = -38.2190, Longitude = 145.0380, UtcOffset = 10 },
      // Other capitals
      new City { Name = "Sydney, NSW",       Latitude = -33.8688, Longitude = 151.2093, UtcOffset = 10 },
      new City { Name = "Newcastle, NSW",    Latitude = -32.9283, Longitude = 151.7817, UtcOffset = 10 },
      new City { Name = "Wollongong, NSW",   Latitude = -34.4278, Longitude = 150.8931, UtcOffset = 10 },
      new City { Name = "Canberra, ACT",     Latitude = -35.2809, Longitude = 149.1300, UtcOffset = 10 },
      new City { Name = "Brisbane, QLD",     Latitude = -27.4698, Longitude = 153.0251, UtcOffset = 10 },
      new City { Name = "Gold Coast, QLD",   Latitude = -28.0167, Longitude = 153.4000, UtcOffset = 10 },
      new City { Name = "Hobart, TAS",       Latitude = -42.8821, Longitude = 147.3272, UtcOffset = 10 },
      new City { Name = "Adelaide, SA",      Latitude = -34.9285, Longitude = 138.6007, UtcOffset = 9.5 },
      new City { Name = "Darwin, NT",        Latitude = -12.4634, Longitude = 130.8456, UtcOffset = 9.5 },
      new City { Name = "Perth, WA",         Latitude = -31.9505, Longitude = 115.8605, UtcOffset = 8 },
      new City { Name = "Auckland, NZ",      Latitude = -36.8485, Longitude = 174.7633, UtcOffset = 12 },
    };

    public static City Find(string name) => All.FirstOrDefault(c => c.Name == name);
  }
}
