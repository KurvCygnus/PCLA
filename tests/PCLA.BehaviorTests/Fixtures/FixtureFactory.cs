using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using PCL;

namespace PCLA.BehaviorTests.Fixtures;

internal static class FixtureFactory
{
    public static string PrepareInstance(InstanceCase c)
    {
        var root = BehaviorTestHost.CreateCaseRoot("instance-" + c.Name);
        Directory.CreateDirectory(Path.Combine(root, "versions"));

        if (c.InheritsFrom is not null)
        {
            WriteVanilla(root, c.InheritsFrom);
        }

        var versionDir = Path.Combine(root, "versions", c.Name);
        Directory.CreateDirectory(versionDir);

        if (c.BrokenJson)
        {
            File.WriteAllText(Path.Combine(versionDir, c.Name + ".json"), "{");
        }
        else if (!c.MissingJson)
        {
            WriteVersion(root, c);
        }

        if (c.Isolated)
        {
            var pclDir = Path.Combine(versionDir, "PCL");
            Directory.CreateDirectory(pclDir);
            //* PCL 的 ini 是扁平的 key:value，没有节名。
            File.WriteAllText(Path.Combine(pclDir, "Setup.ini"),
                "VersionArgumentIndieV2:true\r\n");
        }

        return root;
    }

    public static void WriteVersion(string root, InstanceCase c)
    {
        var dir = Path.Combine(root, "versions", c.Name);
        Directory.CreateDirectory(dir);

        var json = new JObject
        {
            ["id"] = c.Id,
            ["type"] = c.Type,
            ["mainClass"] = "net.minecraft.client.main.Main",
            ["releaseTime"] = "2023-06-12T10:00:00+00:00",
            ["time"] = "2023-06-12T10:00:00+00:00",
            ["assets"] = "5",
            ["assetIndex"] = new JObject { ["id"] = "5" },
            ["libraries"] = new JArray(c.Libraries.Select(l => new JObject { ["name"] = l }))
        };
        if (c.InheritsFrom is not null) json["inheritsFrom"] = c.InheritsFrom;

        File.WriteAllText(Path.Combine(dir, c.Name + ".json"), json.ToString());
    }

    private static void WriteVanilla(string root, string name)
    {
        WriteVersion(root, new InstanceCase(name, name, "release", null, [],
            McInstance.McInstanceState.Original, name));
    }
}
