using System.IO;
using System.Text;
using GlobeCore.Earth;
using GlobeCore.Regions;

namespace GlobeCore.Tests;

public class NaturalEarthParseTests
{
    private static string Square(double lon, double lat) =>
        $"[[[{lon},{lat}],[{lon + 1},{lat}],[{lon + 1},{lat + 1}],[{lon},{lat + 1}],[{lon},{lat}]]]";

    private static Stream Text(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));

    // South Sudan: ISO_A3_EH is SSD but ADM0_A3 is SDS, and its admin-1 units say adm0_a3 SDS. Kosovo: ISO_A3_EH is -99.
    private static RawBoundaryData Parse() => NaturalEarthBoundaries.Parse("v9.9.9", new DateOnly(2030, 1, 1),
        Text($$$"""
        {"type":"FeatureCollection","features":[
          {"type":"Feature","properties":{"NAME":"South Sudan","ISO_A3_EH":"SSD","ADM0_A3":"SDS","LABEL_X":31,"LABEL_Y":7},
           "geometry":{"type":"Polygon","coordinates":{{{Square(30, 5)}}}}},
          {"type":"Feature","properties":{"NAME":"Sudan","ISO_A3_EH":"SDN","ADM0_A3":"SDN","LABEL_X":31,"LABEL_Y":15},
           "geometry":{"type":"Polygon","coordinates":{{{Square(30, 15)}}}}},
          {"type":"Feature","properties":{"NAME":"Kosovo","ISO_A3_EH":"-99","ADM0_A3":"KOS","LABEL_X":21,"LABEL_Y":42},
           "geometry":{"type":"Polygon","coordinates":{{{Square(20, 42)}}}}}
        ]}
        """),
        Text("""{"type":"FeatureCollection","features":[]}"""),
        Text($$$"""
        {"type":"FeatureCollection","features":[
          {"type":"Feature","properties":{"iso_3166_2":"SS-EC","adm0_a3":"SDS","name":"Eastern Equatoria"},
           "geometry":{"type":"Polygon","coordinates":{{{Square(30, 5)}}}}},
          {"type":"Feature","properties":{"iso_3166_2":"XK-01","adm0_a3":"KOS","name":"Prishtina"},
           "geometry":{"type":"Polygon","coordinates":{{{Square(20, 42)}}}}}
        ]}
        """));

    [Fact]
    public void SubdivisionParentUsesTheSameIdAsTheRegion()
    {
        var raw = Parse();
        Assert.Equal(["SSD", "SDN", "KOS"], raw.Regions.Select(r => r.Id));
        Assert.Equal("SSD", raw.Subdivisions.Single(s => s.Code == "SS-EC").ParentId);   // not SDS
        Assert.Equal("KOS", raw.Subdivisions.Single(s => s.Code == "XK-01").ParentId);   // the ADM0_A3 fallback
    }

    [Fact]
    public void MoveAdmin1RuleCanNameSouthSudanBySsd()
    {
        var rules = BoundaryRules.Parse(new MemoryStream(Encoding.UTF8.GetBytes("""
            {"rules":[{"kind":"moveAdmin1","codes":["SS-EC"],"from":"SSD","to":"SDN","expectParts":1,"expectLines":0,
              "description":"test","reason":"test ruling"}]}
            """)));
        var set = rules.Apply(Parse());
        Assert.Empty(set.Regions.ById("SSD")!.Polygons);
        Assert.Equal(2, set.Regions.ById("SDN")!.Polygons.Count);
    }
}
