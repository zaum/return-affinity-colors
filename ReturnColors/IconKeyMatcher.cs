using System.Collections;
using System.Collections.Generic;
using System.Resources;

namespace ReturnColors;

internal static class IconKeyMatcher
{
    private static readonly Dictionary<string, string> ExplicitOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["resources/icons/tools/brushtool.imageset/paint%20brush%20tool_2.png"] =
            "resources/icons/tools/brushtool.imageset/paint%20brush%20tool.png",
        ["resources/icons/tools/brushtool.imageset/paint%20brush%20tool@2x_2.png"] =
            "resources/icons/tools/brushtool.imageset/paint%20brush%20tool@2x.png",
        ["resources/icons/tools/objectselectiontool.imageset/object%20selection%20tool.png"] =
            "resources/icons/tools/objectselectiontool.imageset/object_selection_tool.png",
        ["resources/icons/tools/objectselectiontool.imageset/object%20selection%20tool@2x.png"] =
            "resources/icons/tools/objectselectiontool.imageset/object_selection_tool@2x.png",
        ["resources/icons/tools/measuretool.imageset/measure%20tool.png"] =
            "resources/icons/tools/measuretool.imageset/measuretool.png",
        ["resources/icons/tools/measuretool.imageset/measure%20tool@2x.png"] =
            "resources/icons/tools/measuretool.imageset/measuretool@2x.png",
        ["resources/icons/tools/strokewidthtool.imageset/line%20width%20tool%20mono.png"] =
            "resources/icons/tools/strokewidthtool.imageset/line%20width%20tool.png",
        ["resources/icons/tools/strokewidthtool.imageset/line%20width%20tool%20mono@2x.png"] =
            "resources/icons/tools/strokewidthtool.imageset/line%20width%20tool@2x.png",
        ["resources/icons/tools/inpaintingbrushtool.imageset/inpainting%20tool.png"] =
            "resources/icons/tools/inpaintingbrushtool.imageset/inpainting%20brush%20tool.png",
        ["resources/icons/tools/inpaintingbrushtool.imageset/inpainting%20tool@2x.png"] =
            "resources/icons/tools/inpaintingbrushtool.imageset/inpainting%20brush%20tool@2x.png",
    };

    public static object? FindV2Resource(string v3Key, ResourceReader v2ResourceReader) =>
        FindV2Resource(v3Key, BuildIndex(v2ResourceReader));

    public static Dictionary<string, object?> BuildIndex(ResourceReader reader)
    {
        var index = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in reader)
            index[entry.Key.ToString() ?? string.Empty] = entry.Value;
        return index;
    }

    public static object? FindV2Resource(string v3Key, Dictionary<string, object?> v2Index)
    {
        if (ExplicitOverrides.TryGetValue(v3Key, out var explicitKey)
            && v2Index.TryGetValue(explicitKey, out var explicitValue))
        {
            return explicitValue;
        }

        foreach (var candidate in EnumerateCandidates(v3Key))
        {
            if (v2Index.TryGetValue(candidate, out var value))
                return value;
        }

        Log.Warning($"Failed to resolve v2 resource for \"{v3Key}\"; keeping original.");
        return null;
    }

    private static IEnumerable<string> EnumerateCandidates(string key)
    {
        yield return key;

        var variants = new List<string> { key };

        foreach (var transformed in Transform(key))
            variants.Add(transformed);

        foreach (var v in variants)
        {
            yield return v;
            foreach (var t in Transform(v))
                yield return t;
        }
    }

    private static IEnumerable<string> Transform(string key)
    {
        yield return key.Replace("%20", " ", StringComparison.OrdinalIgnoreCase);
        yield return key.Replace(" ", "%20", StringComparison.OrdinalIgnoreCase);
        yield return RemoveScale(key);
        yield return AddScale(key);
        yield return key.Replace("_2.png", ".png", StringComparison.OrdinalIgnoreCase);
        yield return key.Replace(".png", "_2.png", StringComparison.OrdinalIgnoreCase);
        yield return key.Replace("_mono", "", StringComparison.OrdinalIgnoreCase);
        yield return key.Replace("mono", "", StringComparison.OrdinalIgnoreCase);
    }

    private static string RemoveScale(string key)
    {
        var withoutAt = key.Replace("@2x", "", StringComparison.OrdinalIgnoreCase);
        return withoutAt.Replace("_2.png", ".png", StringComparison.OrdinalIgnoreCase);
    }

    private static string AddScale(string key)
    {
        var withUnderscore = key.Replace(".png", "_2.png", StringComparison.OrdinalIgnoreCase);
        return withUnderscore.Replace(".png", "@2x.png", StringComparison.OrdinalIgnoreCase);
    }
}
