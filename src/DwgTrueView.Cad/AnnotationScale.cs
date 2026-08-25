using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;

namespace DwgTrueView.Cad;

/// <summary>
/// Resolves annotative context scales for TEXT/MTEXT/ATTRIB. Viewport scale is
/// unknown in this viewer, so a missing or unreadable scale defaults to 1:1
/// and never collapses geometry to zero.
/// </summary>
internal static class AnnotationScale
{
    public static float ModelFactor(Entity? entity)
    {
        try
        {
            if (entity is null)
            {
                return 1f;
            }
            Scale? scale = FromEntityContext(entity) ?? FromDocument(entity.Document);
            return Factor(scale);
        }
        catch (Exception)
        {
            return 1f;
        }
    }

    public static float Factor(Scale? scale)
    {
        if (scale is null || scale.IsUnitScale)
        {
            return 1f;
        }
        double paper = scale.PaperUnits;
        double drawing = scale.DrawingUnits;
        if (paper == 0
            || drawing == 0
            || !double.IsFinite(paper)
            || !double.IsFinite(drawing))
        {
            return 1f;
        }
        // Paper height × (drawing units / paper units) → model-space size.
        double factor = drawing / paper;
        if (factor <= 0 || !double.IsFinite(factor))
        {
            return 1f;
        }
        return (float)Math.Clamp(factor, 1e-4, 1e6);
    }

    private static Scale? FromEntityContext(Entity entity)
    {
        var contexts = new List<AnnotScaleObjectContextData>();
        Collect(entity.XDictionary, contexts, 0);
        if (contexts.Count == 0)
        {
            return null;
        }

        AnnotScaleObjectContextData? selected =
            contexts.Find(static item => item.Default)
            ?? MatchCurrent(entity.Document, contexts)
            ?? contexts[0];
        return selected.Scale;
    }

    private static AnnotScaleObjectContextData? MatchCurrent(
        CadDocument? document,
        IReadOnlyList<AnnotScaleObjectContextData> contexts)
    {
        Scale? current = FromDocument(document);
        if (current is null || string.IsNullOrWhiteSpace(current.Name))
        {
            return null;
        }
        return contexts.FirstOrDefault(item =>
            string.Equals(item.Scale?.Name, current.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static Scale? FromDocument(CadDocument? document)
    {
        if (document is null)
        {
            return null;
        }
        try
        {
            string? name = document.DictionaryVariables?.GetValue(
                DictionaryVariable.CurrentAnnotationScale);
            if (!string.IsNullOrWhiteSpace(name)
                && document.Scales is not null
                && document.Scales.TryGet(name, out Scale? named))
            {
                return named;
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    private static void Collect(
        CadDictionary? dictionary,
        List<AnnotScaleObjectContextData> destination,
        int depth)
    {
        if (dictionary is null || depth > 8)
        {
            return;
        }
        IEnumerable<string>? names = dictionary.EntryNames;
        if (names is null)
        {
            return;
        }
        foreach (string name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            if (!dictionary.TryGetEntry(name, out NonGraphicalObject? entry) || entry is null)
            {
                continue;
            }
            if (entry is AnnotScaleObjectContextData context)
            {
                destination.Add(context);
            }
            else if (entry is CadDictionary nested)
            {
                Collect(nested, destination, depth + 1);
            }
        }
    }
}
